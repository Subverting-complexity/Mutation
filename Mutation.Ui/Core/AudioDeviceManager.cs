using CoreAudio;
using Mutation.Ui.Core;
using NAudio.Wave;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Mutation.Ui;

/// Manages enumeration and selection of audio capture devices as well as
/// mute and unmute operations.  This keeps audio related responsibilities
/// away from the WinForms logic in <see cref="MutationForm"/>.
///
/// Two locks, with a strict rule about which threads may take them:
///
/// <c>_sync</c> guards the device fields and is only ever held for a field read or
/// swap — never across a COM call. The UI thread takes it (the CaptureDeviceInfos,
/// SelectedMicrophone and MicrophoneDeviceIndex properties, SelectMicrophoneById,
/// GetEndpoint), so anything that can block on hardware must stay outside it or the
/// window and the screen reader freeze with it.
///
/// <c>_comGate</c> serializes the operations that actually talk to the devices — the
/// mute toggle, the level-endpoint refresh, and the hot-plug re-sync — so two of them
/// never write the same endpoint at once. It is held across slow COM work and is
/// therefore taken only from background threads (the mute-toggle coordinator, the
/// level-write worker, the OS device-notification thread). No UI-thread path may take
/// it.
public class AudioDeviceManager : IMuteEndpointProvider, ICaptureLevelEndpointProvider
{
	private readonly MMDeviceEnumerator _deviceEnumerator;
	private readonly MuteStateController _muteState;
	// Guards the capture-device list and the selected-microphone fields against the
	// background thread that OS device-change notifications arrive on: a hot-plug
	// re-enumeration must not race a selection or a property read. Held for field
	// access only — see the type-level note.
	private readonly object _sync = new();
	// Serializes device COM work. Reentrant by design: ToggleMute holds it while
	// MuteStateController calls back into RefreshEndpoints.
	private readonly object _comGate = new();
	private IReadOnlyList<CaptureDeviceEntry> _captureDevices = Array.Empty<CaptureDeviceEntry>();
	// The describable subset of _captureDevices, cached so a UI read is a field read
	// rather than a projection. Rebuilt with the list it is derived from.
	private IReadOnlyList<CaptureDeviceInfo> _captureDeviceInfos = Array.Empty<CaptureDeviceInfo>();
	private MMDevice? _microphone;
	private CaptureDeviceInfo? _microphoneInfo;
	private int _microphoneDeviceIndex = -1;

	/// <summary>
	/// Raised after a re-enumeration that actually changed which devices exist, so the
	/// UI can rebuild a list whose entries would otherwise name devices that are gone
	/// and omit ones that have appeared. Not raised for a re-enumeration that returns
	/// the same set — those happen on every mute and level retry, and re-raising there
	/// would churn the UI for nothing.
	///
	/// Raised on whichever thread performed the enumeration — the OS device-notification
	/// thread, or a coordinator worker — never the UI thread, and with <c>_comGate</c>
	/// still held. Handlers must marshal to their own thread and return promptly.
	/// </summary>
	public event EventHandler? CaptureDeviceListChanged;

	// Pairs an enumerated device with the COM-free description taken at the same
	// moment, so the two cannot drift apart across a re-enumeration. Info is null for a
	// device whose ID could not be read: it still participates in mute (which tolerates
	// a throwing endpoint) but is not offered to the user, because nothing could
	// re-resolve it later.
	private sealed record CaptureDeviceEntry(MMDevice Device, CaptureDeviceInfo? Info);

	public AudioDeviceManager(MMDeviceEnumerator deviceEnumerator, ICaptureDeviceChangeNotifier deviceChangeNotifier)
	{
		_deviceEnumerator = deviceEnumerator ?? throw new ArgumentNullException(nameof(deviceEnumerator));
		if (deviceChangeNotifier is null)
			throw new ArgumentNullException(nameof(deviceChangeNotifier));
		RefreshCaptureDevices();
		_muteState = new MuteStateController(this, ReadInitialMuteState());

		// Subscribe only after _muteState exists, so a notification that
		// arrives during construction cannot reach a half-built object.
		deviceChangeNotifier.CaptureDevicesChanged += OnCaptureDevicesChanged;
	}

	// When a microphone is added or removed at the OS level, re-enumerate so
	// ToggleMute() acts on the current set, then re-apply the current mute
	// state so a newly connected mic adopts it (muted stays muted). Runs on the
	// OS notification thread, so it may hold _comGate across the COM writes.
	private void OnCaptureDevicesChanged(object? sender, EventArgs e)
	{
		lock (_comGate)
		{
			RefreshCaptureDevices();
			_muteState.SynchronizeDevices();
		}
	}

	/// <summary>
	/// The current capture devices, as immutable descriptions. This is what the UI
	/// binds: the list itself is replaced wholesale on each enumeration, so the
	/// reference handed out here never mutates and never needs copying.
	/// </summary>
	public IReadOnlyList<CaptureDeviceInfo> CaptureDeviceInfos
	{
		get { lock (_sync) return _captureDeviceInfos; }
	}

	/// <summary>
	/// The selected microphone's description, or <c>null</c> when none is selected.
	/// COM-free, so the UI can read it without risking a stall.
	/// </summary>
	public CaptureDeviceInfo? SelectedMicrophone { get { lock (_sync) return _microphoneInfo; } }

	// Read under _sync: a hot-plug on the notification thread can re-point
	// _microphone / _microphoneDeviceIndex while a caller reads them.
	public int MicrophoneDeviceIndex { get { lock (_sync) return _microphoneDeviceIndex; } }

	// Reflects the aggregate mute state that was actually written to the
	// capture devices and confirmed by read-back — not an optimistic guess
	// and not a stale read of a single unrelated device instance.
	public bool IsMuted => _muteState.IsMuted;

	// Private because it is only safe under _comGate: it disposes wrappers that an
	// in-flight endpoint batch may still be driving, and only the COM-serialized
	// callers guarantee no such batch is running.
	private void RefreshCaptureDevices()
	{
		// Enumerate and describe before taking _sync: both are COM calls and can stall
		// on a slow or failing device. Describing here — once, on the enumerating
		// thread — is what lets every later read of a device's name and ID be a plain
		// field read (issue #264).
		var devices = _deviceEnumerator
			.EnumerateAudioEndPoints(DataFlow.Capture, DeviceState.Active)
			.Select(device => new CaptureDeviceEntry(device, Describe(device)))
			.ToList();

		var infos = devices
			.Where(entry => entry.Info is not null)
			.Select(entry => entry.Info!)
			.ToList();

		IReadOnlyList<CaptureDeviceEntry> superseded;
		bool listChanged;
		lock (_sync)
		{
			superseded = _captureDevices;
			listChanged = !SameDeviceIds(_captureDeviceInfos, infos);
			_captureDevices = devices;
			_captureDeviceInfos = infos;
		}

		// Dispose the COM wrappers from the previous enumeration so they do not
		// accumulate until finalization — outside _sync, because releasing a COM
		// wrapper is itself a call into the device. The selected mic is skipped: it
		// stays live for recording and level/mute operations, and
		// RefreshActiveMicrophone swaps it for a fresh instance itself.
		//
		// The selection is re-read per device rather than sampled once above: this loop
		// runs without _sync, so a selection on the UI thread can adopt one of these
		// superseded wrappers while it is in progress, and disposing the device the
		// user just chose would leave the selected mic dead until restart.
		DisposeSuperseded(superseded.Select(entry => entry.Device), IsSelectedMicrophone);

		if (listChanged)
			CaptureDeviceListChanged?.Invoke(this, EventArgs.Empty);
	}

	// The COM-free description of a device, or null when its identity cannot be read —
	// a device nothing could re-resolve later is not one to offer the user.
	private static CaptureDeviceInfo? Describe(MMDevice? device)
	{
		if (device is null)
			return null;

		try
		{
			string id = device.ID;
			if (string.IsNullOrEmpty(id))
				return null;

			return new CaptureDeviceInfo(id, ReadFriendlyName(device));
		}
		catch
		{
			return null;
		}
	}

	private static string ReadFriendlyName(MMDevice device)
	{
#pragma warning disable CS0618 // Fall back when DeviceFriendlyName is not populated.
		string? name = device.DeviceFriendlyName;
		if (string.IsNullOrWhiteSpace(name))
			name = device.FriendlyName;
#pragma warning restore CS0618
		return string.IsNullOrWhiteSpace(name) ? "Unknown microphone" : name;
	}

	// Whether two enumerations describe the same devices. This is what keeps the
	// CaptureDeviceListChanged event — and with it a UI rebuild and its screen-reader
	// announcement — to genuine hot-plugs: the mute and level retry paths re-enumerate
	// routinely and return the same set.
	// Internal so the rule is unit-testable without CoreAudio devices.
	internal static bool SameDeviceIds(IReadOnlyList<CaptureDeviceInfo> left, IReadOnlyList<CaptureDeviceInfo> right)
	{
		if (left.Count != right.Count)
			return false;

		for (int i = 0; i < left.Count; i++)
		{
			if (!string.Equals(left[i].Id, right[i].Id, StringComparison.OrdinalIgnoreCase))
				return false;
		}

		return true;
	}

	private bool IsSelectedMicrophone(MMDevice device)
	{
		lock (_sync)
		{
			return ReferenceEquals(device, _microphone);
		}
	}

	/// <summary>
	/// Selects the microphone with the given endpoint ID, resolving the live device out
	/// of the current enumeration. Returns false when no such device exists any more.
	///
	/// Takes an ID rather than an <c>MMDevice</c> deliberately: a caller's device
	/// reference can predate a re-enumeration, and such a wrapper is a disposed COM
	/// proxy that throws on the first property read (issue #264). An ID cannot go stale
	/// — it either matches a live device or it does not.
	/// </summary>
	public bool SelectMicrophoneById(string deviceId)
	{
		if (string.IsNullOrEmpty(deviceId))
			throw new ArgumentException("A device ID is required.", nameof(deviceId));

		MMDevice device;
		string friendlyName;
		CaptureDeviceInfo info;
		lock (_sync)
		{
			var entry = _captureDevices.FirstOrDefault(
				e => e.Info is not null && string.Equals(e.Info.Id, deviceId, StringComparison.OrdinalIgnoreCase));
			if (entry is null)
				return false;

			device = entry.Device;
			info = entry.Info!;
			friendlyName = info.FriendlyName;
		}

		// Resolved outside the lock, and from the cached name rather than the device, so
		// this costs no COM call at all.
		int deviceIndex = ResolveNAudioDeviceIndex(friendlyName);

		// The device, its description and its index are published together so a
		// concurrent reader never sees one without the others.
		lock (_sync)
		{
			_microphone = device;
			_microphoneInfo = info;
			_microphoneDeviceIndex = deviceIndex;
		}

		return true;
	}

	public void EnsureDefaultMicrophoneSelected()
	{
		lock (_sync)
		{
			if (_microphone != null)
				return;
		}

		MMDevice? defaultMic = null;
		CaptureDeviceInfo? info;
		int deviceIndex;
		try
		{
			// COM: resolved outside _sync so a stalled enumerator cannot block the
			// property readers on the UI thread. Every call stays inside the guard —
			// this runs from the window constructor, where a throw from a flaky device
			// would surface as a fatal startup error instead of simply leaving no
			// microphone selected.
			defaultMic = _deviceEnumerator.GetDefaultAudioEndpoint(DataFlow.Capture, Role.Console);
			if (defaultMic is null)
				return;

			info = Describe(defaultMic);
			deviceIndex = ResolveNAudioDeviceIndex(info?.FriendlyName);
		}
		catch
		{
			DisposeQuietly(defaultMic);
			return;
		}

		bool adopted = false;
		lock (_sync)
		{
			// Re-check: a selection may have landed while the default was being
			// resolved, and an explicit choice must not be overwritten by the default.
			if (_microphone is null)
			{
				_microphone = defaultMic;
				_microphoneInfo = info;
				_microphoneDeviceIndex = deviceIndex;
				adopted = true;
			}
		}

		// Not adopted, and not part of any enumeration this type owns — release it here
		// rather than leaving it to finalization.
		if (!adopted)
			DisposeQuietly(defaultMic);
	}

	// NAudio's WaveIn device ProductName often matches the CoreAudio friendly name exactly
	// (e.g., "Microphone (Realtek(R) Audio)"). An older implementation appended " (" to
	// the friendly name before comparison, preventing any matches and leaving the device
	// index at -1 (so no waveform capture would occur). Use more flexible matching.
	//
	// Takes the name rather than the device: the name was already captured when the
	// device was enumerated, so resolving an index costs no COM call and cannot throw on
	// a superseded wrapper.
	private static int ResolveNAudioDeviceIndex(string? friendlyName)
	{
		if (string.IsNullOrEmpty(friendlyName))
			return -1;

		int deviceCount = WaveIn.DeviceCount;
		string friendly = friendlyName;
		int bestMatchIndex = -1;
		for (int i = 0; i < deviceCount; i++)
		{
			string product = WaveInEvent.GetCapabilities(i).ProductName;
			if (string.Equals(product, friendly, StringComparison.OrdinalIgnoreCase))
			{
				return i; // exact match wins immediately
			}
			// Fallback heuristics (partial contains either direction)
			if (bestMatchIndex == -1 && (product.Contains(friendly, StringComparison.OrdinalIgnoreCase) ||
										  friendly.Contains(product, StringComparison.OrdinalIgnoreCase)))
			{
				bestMatchIndex = i;
			}
		}
		return bestMatchIndex;
	}

	/// Flips the mute state on all capture devices, confirms the write by
	/// reading it back, and retries once with fresh device references if the
	/// write fails. Returns whether the new state was confirmed and the mute
	/// state to report to the user.
	///
	/// Runs on a background thread (see <see cref="MicrophoneMuteToggleCoordinator"/>),
	/// which is what makes it safe to hold _comGate across the COM writes here.
	public MuteToggleResult ToggleMute()
	{
		// Serialize with the device-change handler so a hot-plug refresh
		// cannot swap the device list out from under an in-flight toggle.
		lock (_comGate)
		{
			return _muteState.Toggle();
		}
	}

	IReadOnlyList<IMuteEndpoint> IMuteEndpointProvider.GetEndpoints()
	{
		// Snapshot the list under _sync, then build the wrappers outside it — the
		// wrappers are inert until used, but the caller then drives COM through them.
		List<MMDevice> devices;
		lock (_sync)
		{
			devices = _captureDevices.Select(entry => entry.Device).ToList();
		}

		return BuildEndpoints(devices);
	}

	IReadOnlyList<IMuteEndpoint> IMuteEndpointProvider.RefreshEndpoints()
	{
		// Only reached from MuteStateController's retry, i.e. already inside
		// ToggleMute's _comGate on a background thread; the lock is reentrant.
		lock (_comGate)
		{
			RefreshCaptureDevices();

			List<MMDevice> devices;
			lock (_sync)
			{
				devices = _captureDevices.Select(entry => entry.Device).ToList();
			}

			return BuildEndpoints(devices);
		}
	}

	// The active microphone's level endpoint, over the currently-held device
	// reference. Resolving the endpoint is a pure field read — no COM — so this call
	// cannot stall behind another thread's device work. Driving the returned endpoint
	// does touch COM, so a UI-thread caller must still do that off-thread (see
	// MicrophoneLevelWriteCoordinator).
	ICaptureLevelEndpoint ICaptureLevelEndpointProvider.GetEndpoint()
	{
		MMDevice? microphone;
		lock (_sync)
		{
			microphone = _microphone;
		}

		return new MmDeviceCaptureLevelEndpoint(microphone);
	}

	// Re-resolves the selected microphone to a fresh reference, then returns its
	// endpoint — so a level write that failed on a stale COM proxy can be
	// retried against a live reference. Called from the level-write worker, off the
	// UI thread, so it may hold _comGate across the re-enumeration.
	ICaptureLevelEndpoint ICaptureLevelEndpointProvider.RefreshEndpoint()
	{
		lock (_comGate)
		{
			RefreshActiveMicrophone();

			MMDevice? microphone;
			lock (_sync)
			{
				microphone = _microphone;
			}

			return new MmDeviceCaptureLevelEndpoint(microphone);
		}
	}

	// Re-enumerates the capture devices and re-points _microphone at the fresh
	// instance carrying the same device ID, so its COM proxy is live again. This
	// is the level-side mirror of RefreshEndpoints() for mute — both recover a
	// stale proxy by re-acquiring from a fresh enumeration. A no-op when no mic
	// is selected; if the previously-selected device is gone, the stale
	// reference is left in place for the caller's retry to fail against.
	private void RefreshActiveMicrophone()
	{
		MMDevice? previous;
		string? id;
		lock (_sync)
		{
			previous = _microphone;
			// The ID was captured when the device was enumerated, so recovering from a
			// stale proxy does not depend on that proxy still answering.
			id = _microphoneInfo?.Id;
		}

		if (previous is null || id is null)
			return;

		// RefreshCaptureDevices deliberately preserves the selected wrapper, so
		// `previous` is still live here; it is disposed once a fresh instance
		// replaces it below.
		RefreshCaptureDevices();

		CaptureDeviceEntry? fresh;
		lock (_sync)
		{
			fresh = _captureDevices.FirstOrDefault(
				e => e.Info is not null && string.Equals(e.Info.Id, id, StringComparison.OrdinalIgnoreCase));
		}

		if (fresh is null)
			return;

		int deviceIndex = ResolveNAudioDeviceIndex(fresh.Info!.FriendlyName);

		lock (_sync)
		{
			// Only take over the selection if it still points at the stale wrapper: a
			// selection on the UI thread may have chosen a different device while this
			// refresh was resolving, and that explicit choice wins.
			if (ReferenceEquals(_microphone, previous))
			{
				_microphone = fresh.Device;
				_microphoneInfo = fresh.Info;
				_microphoneDeviceIndex = deviceIndex;
			}
		}

		// Either way `previous` is finished with: it was deliberately skipped by the
		// re-enumeration's disposal pass because it was the selection at that moment,
		// so nothing else will release it.
		DisposeQuietly(previous);
	}

	// Releases a COM wrapper, treating a failure as nothing to act on — the wrapper is
	// being abandoned either way.
	private static void DisposeQuietly(IDisposable? disposable)
	{
		try { disposable?.Dispose(); }
		catch { }
	}

	// Disposes the COM wrappers from a superseded enumeration, skipping the one
	// that is still the selected microphone (it stays live for the caller) and
	// swallowing a per-device failure so one bad wrapper cannot strand the rest.
	// Generic and static so the "skip the selected one" rule is unit-testable
	// without real CoreAudio devices.
	//
	// The selection is supplied as a predicate, not a value, because it is evaluated
	// per device as the loop runs: another thread may take one of these wrappers as
	// the new selection partway through, and disposing it would strand the microphone.
	internal static void DisposeSuperseded<T>(IEnumerable<T> superseded, Func<T, bool> isStillSelected)
		where T : class, IDisposable
	{
		foreach (var device in superseded)
		{
			if (device is null || isStillSelected(device))
				continue;
			try { device.Dispose(); }
			catch { }
		}
	}

	// Fixed-selection overload, for callers whose selection cannot change underneath
	// them.
	internal static void DisposeSuperseded<T>(IEnumerable<T> superseded, T? selected)
		where T : class, IDisposable
		=> DisposeSuperseded(superseded, device => ReferenceEquals(device, selected));

	// Built from a snapshot taken under _sync, never from the live list — the
	// wrappers themselves touch COM once the caller uses them.
	private static IReadOnlyList<IMuteEndpoint> BuildEndpoints(IEnumerable<MMDevice> devices) =>
		devices
			.Where(device => device != null)
			.Select(device => (IMuteEndpoint)new MmDeviceMuteEndpoint(device))
			.ToList();

	// Best-effort read of the current device mute state at startup so the
	// tracked state starts in sync with the hardware. Defaults to unmuted if
	// no device can be read.
	private bool ReadInitialMuteState()
	{
		List<MMDevice> devices;
		lock (_sync)
		{
			devices = _captureDevices.Select(entry => entry.Device).ToList();
		}

		foreach (var device in devices)
		{
			try
			{
				if (device?.AudioEndpointVolume is { } volume)
					return volume.Mute;
			}
			catch { }
		}
		return false;
	}
}
