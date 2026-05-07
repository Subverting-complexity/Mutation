using CoreAudio;
using NAudio.Wave;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Mutation.Ui;

/// Manages enumeration and selection of audio capture devices as well as
/// mute and unmute operations.  This keeps audio related responsibilities
/// away from the WinForms logic in <see cref="MutationForm"/>.
public class AudioDeviceManager
{
        private readonly MMDeviceEnumerator _deviceEnumerator;
        private IList<MMDevice> _captureDevices = new List<MMDevice>();
        private MMDevice? _microphone;
        private int _microphoneDeviceIndex = -1;

        public AudioDeviceManager(MMDeviceEnumerator deviceEnumerator)
        {
                _deviceEnumerator = deviceEnumerator ?? throw new ArgumentNullException(nameof(deviceEnumerator));
                RefreshCaptureDevices();
        }

        public IEnumerable<MMDevice> CaptureDevices => _captureDevices;

        public MMDevice? Microphone => _microphone;

        public int MicrophoneDeviceIndex => _microphoneDeviceIndex;

        public bool IsMuted => _microphone is { AudioEndpointVolume: { } volume } && volume.Mute;

        public void RefreshCaptureDevices()
        {
                var devices = _deviceEnumerator.EnumerateAudioEndPoints(DataFlow.Capture, DeviceState.Active);
                _captureDevices = devices.ToList();
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

        public void ToggleMute()
        {
                bool newMuteState = !IsMuted;
                foreach (var mic in _captureDevices)
                {
                        if (mic == null)
                                continue;
                        try
                        {
                                if (mic.AudioEndpointVolume != null)
                                        mic.AudioEndpointVolume.Mute = newMuteState;
                        }
                        catch { }
                }
        }
}
