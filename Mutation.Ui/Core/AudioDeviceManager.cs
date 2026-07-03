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
public class AudioDeviceManager : IMuteEndpointProvider, ICaptureLevelEndpointProvider
{
        private readonly MMDeviceEnumerator _deviceEnumerator;
        private readonly MuteStateController _muteState;
        // Guards the capture-device list and the mute application against the
        // background thread that OS device-change notifications arrive on: a
        // hot-plug re-enumeration must not race the UI-thread mute toggle.
        private readonly object _sync = new();
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
        // state so a newly connected mic adopts it (muted stays muted).
        private void OnCaptureDevicesChanged(object? sender, EventArgs e)
        {
                lock (_sync)
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

        public MMDevice? Microphone => _microphone;

        public int MicrophoneDeviceIndex => _microphoneDeviceIndex;

        // Reflects the aggregate mute state that was actually written to the
        // capture devices and confirmed by read-back — not an optimistic guess
        // and not a stale read of a single unrelated device instance.
        public bool IsMuted => _muteState.IsMuted;

        public void RefreshCaptureDevices()
        {
                var devices = _deviceEnumerator.EnumerateAudioEndPoints(DataFlow.Capture, DeviceState.Active);
                lock (_sync)
                {
                        _captureDevices = devices.ToList();
                }
        }

        public void SelectMicrophone(MMDevice device)
        {
                _microphone = device ?? throw new ArgumentNullException(nameof(device));
                SelectCaptureDeviceForNAudio();
        }

	public void EnsureDefaultMicrophoneSelected()
	{
		if (_microphone != null)
			return;

                try
                {
                        var defaultMic = _deviceEnumerator.GetDefaultAudioEndpoint(DataFlow.Capture, Role.Console);
                        if (defaultMic != null)
                        {
                                _microphone = defaultMic;
                                SelectCaptureDeviceForNAudio();
                        }
                }
                catch { }
        }

	private void SelectCaptureDeviceForNAudio()
	{
		if (_microphone == null)
		{
			_microphoneDeviceIndex = -1;
			return;
		}

                // NAudio's WaveIn device ProductName often matches the CoreAudio friendly name exactly
                // (e.g., "Microphone (Realtek(R) Audio)"). The previous implementation appended " (" to
                // the friendly name before comparison, preventing any matches and leaving the device
                // index at -1 (so no waveform capture would occur). Use more flexible matching.
		int deviceCount = WaveIn.DeviceCount;
                string friendly = _microphone.DeviceFriendlyName;
                int bestMatchIndex = -1;
                for (int i = 0; i < deviceCount; i++)
                {
                        string product = WaveInEvent.GetCapabilities(i).ProductName;
                        if (string.Equals(product, friendly, StringComparison.OrdinalIgnoreCase))
                        {
                                bestMatchIndex = i; // exact match wins immediately
                                break;
                        }
                        // Fallback heuristics (partial contains either direction)
                        if (bestMatchIndex == -1 && (product.Contains(friendly, StringComparison.OrdinalIgnoreCase) ||
                                                      friendly.Contains(product, StringComparison.OrdinalIgnoreCase)))
                        {
                                bestMatchIndex = i;
                        }
                }
                _microphoneDeviceIndex = bestMatchIndex;
	}

        /// Flips the mute state on all capture devices, confirms the write by
        /// reading it back, and retries once with fresh device references if the
        /// write fails. Returns whether the new state was confirmed and the mute
        /// state to report to the user.
        public MuteToggleResult ToggleMute()
        {
                // Serialize with the device-change handler so a hot-plug refresh
                // cannot swap the device list out from under an in-flight toggle.
                lock (_sync)
                {
                        return _muteState.Toggle();
                }
        }

        IReadOnlyList<IMuteEndpoint> IMuteEndpointProvider.GetEndpoints()
        {
                lock (_sync)
                {
                        return BuildEndpoints();
                }
        }

        IReadOnlyList<IMuteEndpoint> IMuteEndpointProvider.RefreshEndpoints()
        {
                lock (_sync)
                {
                        RefreshCaptureDevices();
                        return BuildEndpoints();
                }
        }

        // The active microphone's level endpoint, over the currently-held device
        // reference. Serialized with the device-change handler so a hot-plug
        // refresh cannot swap the selected device out mid-read.
        ICaptureLevelEndpoint ICaptureLevelEndpointProvider.GetEndpoint()
        {
                lock (_sync)
                {
                        return new MmDeviceCaptureLevelEndpoint(_microphone);
                }
        }

        // Re-resolves the selected microphone to a fresh reference, then returns its
        // endpoint — so a level write that failed on a stale COM proxy can be
        // retried against a live reference.
        ICaptureLevelEndpoint ICaptureLevelEndpointProvider.RefreshEndpoint()
        {
                lock (_sync)
                {
                        RefreshActiveMicrophone();
                        return new MmDeviceCaptureLevelEndpoint(_microphone);
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
                if (_microphone is null)
                        return;

                string? id;
                try { id = _microphone.ID; }
                catch { id = null; }

                RefreshCaptureDevices();

                if (id is null)
                        return;

                var fresh = _captureDevices.FirstOrDefault(device => MatchesId(device, id));
                if (fresh != null)
                {
                        _microphone = fresh;
                        SelectCaptureDeviceForNAudio();
                }
        }

        private static bool MatchesId(MMDevice? device, string id)
        {
                try { return device != null && string.Equals(device.ID, id, StringComparison.OrdinalIgnoreCase); }
                catch { return false; }
        }

        // Callers hold _sync; the lock is reentrant so RefreshCaptureDevices'
        // own lock nests harmlessly.
        private IReadOnlyList<IMuteEndpoint> BuildEndpoints() =>
                _captureDevices
                        .Where(device => device != null)
                        .Select(device => (IMuteEndpoint)new MmDeviceMuteEndpoint(device))
                        .ToList();

        // Best-effort read of the current device mute state at startup so the
        // tracked state starts in sync with the hardware. Defaults to unmuted if
        // no device can be read.
        private bool ReadInitialMuteState()
        {
                foreach (var device in _captureDevices)
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
