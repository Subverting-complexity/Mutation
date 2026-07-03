using System;

namespace Mutation.Ui.Core;

/// <summary>
/// Applies a user-pinned capture level to the active microphone and re-asserts
/// it on record/dictate, mic selection, and app startup, so the Windows capture
/// level stays consistent regardless of what other apps do. All hardware access
/// goes through <see cref="ICaptureLevelController"/>, keeping the rules here
/// unit-testable without audio hardware.
/// </summary>
public sealed class MicrophoneLevelPinService
{
	/// <summary>
	/// Levels closer than this (on the 0–100 scale) count as already correct, so
	/// no redundant write is issued. One unit of 100 equals a 0.01 scalar.
	/// </summary>
	public const int LevelEpsilon = 1;

	public const int MinLevel = 0;
	public const int MaxLevel = 100;

	private readonly ICaptureLevelController _controller;

	public MicrophoneLevelPinService(ICaptureLevelController controller)
	{
		_controller = controller ?? throw new ArgumentNullException(nameof(controller));
	}

	/// <summary>
	/// True when the active device exposes a controllable software level. The UI
	/// uses this to disable the control on hardware-fixed devices.
	/// </summary>
	public bool IsLevelControlSupported => _controller.IsLevelControlSupported;

	/// <summary>
	/// Re-asserts the pinned level. A <c>null</c> target means pinning is disabled,
	/// which is a no-op. Returns true when the hardware level was actually changed.
	/// </summary>
	public bool ReassertPinnedLevel(int? pinnedLevel)
	{
		if (pinnedLevel is null)
			return false;

		return WriteLevel(pinnedLevel.Value);
	}

	/// <summary>
	/// Applies a level the user is setting live (e.g. dragging the slider), giving
	/// instant feedback even when not recording. Honors the same epsilon and
	/// mute rules as <see cref="ReassertPinnedLevel"/>. Returns true when the
	/// hardware level was changed.
	/// </summary>
	public bool ApplyLevel(int level) => WriteLevel(level);

	/// <summary>
	/// Reads the active device's current capture level as a 0–100 value for
	/// display, or <c>null</c> when it cannot be read — an unsupported /
	/// hardware-fixed device or a transient failure. This is a pure read: it never
	/// writes the level or touches mute. Callers use it to sync a UI display to the
	/// real OS level; the deliberate <c>null</c> (rather than a default) lets them
	/// leave the display unchanged instead of showing a misleading value.
	/// </summary>
	public int? ReadCurrentLevel()
	{
		if (!_controller.IsLevelControlSupported)
			return null;

		if (_controller.GetLevelScalar() is not float scalar)
			return null;

		float bounded = scalar < 0f ? 0f : scalar > 1f ? 1f : scalar;
		return (int)Math.Round(bounded * 100f);
	}

	private bool WriteLevel(int level)
	{
		if (!_controller.IsLevelControlSupported)
			return false;

		int target = Clamp(level);
		float? current = _controller.GetLevelScalar();

		// Skip redundant writes when the current level is already within epsilon.
		if (current is float c && Math.Abs(c * 100f - target) <= LevelEpsilon)
			return false;

		_controller.SetLevelScalar(target / 100f);
		return true;
	}

	private static int Clamp(int level) =>
		level < MinLevel ? MinLevel : level > MaxLevel ? MaxLevel : level;
}
