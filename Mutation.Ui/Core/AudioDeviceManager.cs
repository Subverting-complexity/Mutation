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
/// swap — never across a COM call. The UI thread takes it (the Microphone,
/// MicrophoneDeviceIndex and CaptureDevices properties, SelectMicrophone,
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
	private IList<MMDevice> _captureDevices = new List<MMDevice>();
	private MMDevice? _microphone;
	private int _microphoneDeviceIndex = -1;

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

	public IEnumerable<MMDevice> CaptureDevices
	{
		// Return a snapshot: the backing list can be swapped out on a
		// device-change notification thread while a caller enumerates.
		get { lock (_sync) return _captureDevices.ToList(); }
	}

	// Read under _sync: a hot-plug on the notification thread can re-point
	// _microphone / _microphoneDeviceIndex while a caller reads them.
	public MMDevice? Microphone { get { lock (_sync) return _microphone; } }

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
		// Enumerate before taking _sync: EnumerateAudioEndPoints is a COM call and can
		// stall on a slow or failing device.
		var devices = _deviceEnumerator
			.EnumerateAudioEndPoints(DataFlow.Capture, DeviceState.Active)
			.ToList();

		IEnumerable<MMDevice> superseded;
		lock (_sync)
		{
			superseded = _captureDevices;
			_captureDevices = devices;
		}

		// Dispose the COM wrappers from the previous enumeration so they do not
		// accumulate until finalization — outside _sync, because releasing a COM
		// wrapper is itself a call into the device. The selected mic is skipped: it
		// stays live for recording and level/mute operations, and
		// RefreshActiveMicrophone swaps it for a fresh instance itself.
		//
		// The selection is re-read per device rather than sampled once above: this loop
		// runs without _sync, so SelectMicrophone on the UI thread can adopt one of
		// these superseded wrappers while it is in progress, and disposing the device
		// the user just chose would leave the selected mic dead until restart.
		DisposeSuperseded(superseded, IsSelectedMicrophone);
	}

	private bool IsSelectedMicrophone(MMDevice device)
	{
		lock (_sync)
		{
			return ReferenceEquals(device, _microphone);
		}
	}

	public void SelectMicrophone(MMDevice device)
	{
		if (device is null)
			throw new ArgumentNullException(nameof(device));

		// Resolve the NAudio index before taking the lock: the lookup reads the
		// device's friendly name over COM and enumerates the WaveIn devices, and this
		// runs on the UI thread.
		int deviceIndex = ResolveNAudioDeviceIndex(device);

		// The device and its index are published together so a concurrent reader never
		// sees one without the other.
		lock (_sync)
		{
			_microphone = device;
			_microphoneDeviceIndex = deviceIndex;
		}
	}

	public void EnsureDefaultMicrophoneSelected()
	{
		lock (_sync)
		{
			if (_microphone != null)
				return;
		}

		MMDevice? defaultMic = null;
		int deviceIndex;
		try
		{
			// COM: resolved outside _sync so a stalled enumerator cannot block the
			// property readers on the UI thread. Both calls stay inside the guard —
			// this runs from the window constructor, where a throw from a flaky device
			// would surface as a fatal startup error instead of simply leaving no
			// microphone selected.
			defaultMic = _deviceEnumerator.GetDefaultAudioEndpoint(DataFlow.Capture, Role.Console);
			if (defaultMic is null)
				return;

			deviceIndex = ResolveNAudioDeviceIndex(defaultMic);
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
	// Static and lock-free: it reads the device's friendly name over COM and walks the
	// WaveIn device list, so callers resolve the index first and publish it under _sync
	// afterwards.
	private static int ResolveNAudioDeviceIndex(MMDevice? microphone)
	{
		if (microphone is null)
			return -1;

		int deviceCount = WaveIn.DeviceCount;
		string friendly = microphone.DeviceFriendlyName;
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
			devices = _captureDevices.ToList();
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
				devices = _captureDevices.ToList();
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
		lock (_sync)
		{
			previous = _microphone;
		}

		if (previous is null)
			return;

		string? id;
		try { id = previous.ID; }
		catch { id = null; }

		// RefreshCaptureDevices deliberately preserves the selected wrapper, so
		// `previous` is still live here; it is disposed once a fresh instance
		// replaces it below.
		RefreshCaptureDevices();

		if (id is null)
			return;

		List<MMDevice> devices;
		lock (_sync)
		{
			devices = _captureDevices.ToList();
		}

		// MatchesId reads device.ID over COM, so the search runs on the snapshot
		// rather than under _sync.
		var fresh = devices.FirstOrDefault(device => MatchesId(device, id));
		if (fresh is null)
			return;

		int deviceIndex = ResolveNAudioDeviceIndex(fresh);

		lock (_sync)
		{
			// Only take over the selection if it still points at the stale wrapper: a
			// SelectMicrophone on the UI thread may have chosen a different device
			// while this refresh was resolving, and that explicit choice wins.
			if (ReferenceEquals(_microphone, previous))
			{
				_microphone = fresh;
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

	private static bool MatchesId(MMDevice? device, string id)
	{
		try { return device != null && string.Equals(device.ID, id, StringComparison.OrdinalIgnoreCase); }
		catch { return false; }
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
			devices = _captureDevices.ToList();
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
