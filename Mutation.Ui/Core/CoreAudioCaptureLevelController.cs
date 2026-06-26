using CoreAudio;
using System;

namespace Mutation.Ui.Core;

/// <summary>
/// CoreAudio-backed <see cref="ICaptureLevelController"/>. Resolves the active
/// microphone lazily through a delegate (over
/// <see cref="AudioDeviceManager.Microphone"/>) so it always targets the
/// currently-selected device, and so this controller does not depend on the
/// device manager directly (which would form a cycle through the pin service).
/// </summary>
public sealed class CoreAudioCaptureLevelController : ICaptureLevelController
{
	private readonly Func<MMDevice?> _activeDevice;

	public CoreAudioCaptureLevelController(Func<MMDevice?> activeDevice)
	{
		_activeDevice = activeDevice ?? throw new ArgumentNullException(nameof(activeDevice));
	}

	public bool IsLevelControlSupported => GetLevelScalar() is not null;

	public float? GetLevelScalar()
	{
		var volume = _activeDevice()?.AudioEndpointVolume;
		if (volume is null)
			return null;

		try
		{
			return volume.MasterVolumeLevelScalar;
		}
		catch
		{
			// Hardware-fixed or transient failure — treat as unreadable, never throw.
			return null;
		}
	}

	public void SetLevelScalar(float scalar)
	{
		var volume = _activeDevice()?.AudioEndpointVolume;
		if (volume is null)
			return;

		float clamped = scalar < 0f ? 0f : scalar > 1f ? 1f : scalar;
		try
		{
			// Only the level scalar is written; Mute is deliberately left untouched.
			volume.MasterVolumeLevelScalar = clamped;
		}
		catch
		{
			// Hardware-fixed or transient failure — skip silently per the story's
			// graceful-degradation requirement.
		}
	}
}
