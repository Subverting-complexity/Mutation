using CoreAudio;
using System;

namespace Mutation.Ui.Core;

/// <summary>
/// CoreAudio-backed <see cref="IMuteEndpoint"/> over a capture
/// <see cref="MMDevice"/>. Reading or writing the mute flag touches the
/// device's COM proxy, which can throw when the underlying device reference is
/// stale — surfaced to the caller so it can retry against a fresh reference.
/// </summary>
public sealed class MmDeviceMuteEndpoint : IMuteEndpoint
{
	private readonly MMDevice _device;

	public MmDeviceMuteEndpoint(MMDevice device)
	{
		_device = device ?? throw new ArgumentNullException(nameof(device));
	}

	public void SetMute(bool mute)
	{
		var volume = _device.AudioEndpointVolume
			?? throw new InvalidOperationException("Device has no audio endpoint volume.");
		volume.Mute = mute;
	}

	public bool GetMute()
	{
		var volume = _device.AudioEndpointVolume
			?? throw new InvalidOperationException("Device has no audio endpoint volume.");
		return volume.Mute;
	}
}
