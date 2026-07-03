using CoreAudio;
using System;

namespace Mutation.Ui.Core;

/// <summary>
/// CoreAudio-backed <see cref="ICaptureLevelEndpoint"/> over a single capture
/// <see cref="MMDevice"/>. Reading or writing the level scalar touches the
/// device's COM proxy, which can throw when the underlying reference is stale —
/// surfaced to the caller so it can retry against a fresh reference. Mirrors
/// <see cref="MmDeviceMuteEndpoint"/>.
/// </summary>
public sealed class MmDeviceCaptureLevelEndpoint : ICaptureLevelEndpoint
{
	// Null when no microphone is selected; the endpoint is then unsupported and
	// every read/write throws, exactly as a missing device should.
	private readonly MMDevice? _device;

	public MmDeviceCaptureLevelEndpoint(MMDevice? device)
	{
		_device = device;
	}

	public bool IsLevelControlSupported
	{
		get
		{
			try
			{
				return _device?.AudioEndpointVolume is not null;
			}
			catch
			{
				// A device whose endpoint-volume object cannot even be obtained is
				// treated as uncontrollable rather than as a transient failure.
				return false;
			}
		}
	}

	public float GetLevelScalar()
	{
		var volume = _device?.AudioEndpointVolume
			?? throw new InvalidOperationException("Device has no audio endpoint volume.");
		return volume.MasterVolumeLevelScalar;
	}

	public void SetLevelScalar(float scalar)
	{
		var volume = _device?.AudioEndpointVolume
			?? throw new InvalidOperationException("Device has no audio endpoint volume.");

		// Only the level scalar is written; Mute is deliberately left untouched.
		volume.MasterVolumeLevelScalar = scalar < 0f ? 0f : scalar > 1f ? 1f : scalar;
	}
}
