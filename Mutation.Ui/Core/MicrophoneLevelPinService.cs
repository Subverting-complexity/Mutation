using System;

namespace Mutation.Ui.Core;

/// <summary>
/// Applies a user-pinned capture level to the active microphone and re-asserts
/// it on record/dictate, mic selection, and app startup, so the Windows capture
/// level stays consistent regardless of what other apps do. Every write is
/// confirmed by reading the level back within <see cref="LevelEpsilon"/>; a
/// thrown exception or a read-back mismatch is a failure, and the service then
/// re-acquires fresh device references once and retries before declaring
/// failure — the same stale-proxy recovery <see cref="MuteStateController"/>
/// uses for mute. All hardware access goes through
/// <see cref="ICaptureLevelEndpointProvider"/>, keeping the rules here
/// unit-testable without audio hardware.
/// </summary>
public sealed class MicrophoneLevelPinService
{
	/// <summary>
	/// Levels closer than this (on the 0–100 scale) count as already correct, so
	/// no redundant write is issued and a read-back this close counts as verified.
	/// One unit of 100 equals a 0.01 scalar.
	/// </summary>
	public const int LevelEpsilon = 1;

	public const int MinLevel = 0;
	public const int MaxLevel = 100;

	private readonly ICaptureLevelEndpointProvider _provider;

	public MicrophoneLevelPinService(ICaptureLevelEndpointProvider provider)
	{
		_provider = provider ?? throw new ArgumentNullException(nameof(provider));
	}

	/// <summary>
	/// Probes the active device once for both facts the UI needs to set its level
	/// controls up: whether the device exposes a controllable software level, and what
	/// that level currently is. Both are COM reads, so they are taken together — asking
	/// separately doubles the exposure to a stalled device and can straddle a device
	/// change. Callers on the UI thread must route this through
	/// <see cref="MicrophoneLevelWriteCoordinator"/> rather than calling it directly.
	/// </summary>
	public CaptureLevelState ReadLevelState()
	{
		var endpoint = _provider.GetEndpoint();
		if (!endpoint.IsLevelControlSupported)
			return new CaptureLevelState(IsSupported: false, Level: null);

		return new CaptureLevelState(IsSupported: true, Level: ToLevel(TryReadScalar(endpoint)));
	}

	/// <summary>
	/// Re-asserts the pinned level. A <c>null</c> target means pinning is disabled,
	/// which is <see cref="CaptureLevelOutcome.Unchanged"/> (nothing to assert).
	/// </summary>
	public CaptureLevelResult ReassertPinnedLevel(int? pinnedLevel)
	{
		if (pinnedLevel is null)
			return new CaptureLevelResult(CaptureLevelOutcome.Unchanged);

		return WriteLevel(pinnedLevel.Value);
	}

	/// <summary>
	/// Applies a level the user is setting live (e.g. dragging the slider), giving
	/// instant feedback even when not recording. Honors the same epsilon and
	/// verification rules as <see cref="ReassertPinnedLevel"/>.
	/// </summary>
	public CaptureLevelResult ApplyLevel(int level) => WriteLevel(level);

	/// <summary>
	/// Reads the active device's current capture level as a 0–100 value for
	/// display, or <c>null</c> when it cannot be read — an unsupported /
	/// hardware-fixed device or a transient failure. This is a pure read: it never
	/// writes the level or touches mute. Callers use it to sync a UI display to the
	/// real OS level; the deliberate <c>null</c> (rather than a default) lets them
	/// leave the display unchanged instead of showing a misleading value.
	/// </summary>
	public int? ReadCurrentLevel() => ReadLevelState().Level;

	// Converts a raw level scalar to the 0–100 display scale, propagating "unreadable"
	// as null rather than inventing a value.
	private static int? ToLevel(float? scalar)
	{
		if (scalar is not float value)
			return null;

		float bounded = value < 0f ? 0f : value > 1f ? 1f : value;
		return (int)Math.Round(bounded * 100f);
	}

	private CaptureLevelResult WriteLevel(int level)
	{
		var endpoint = _provider.GetEndpoint();
		if (!endpoint.IsLevelControlSupported)
			return new CaptureLevelResult(CaptureLevelOutcome.Unsupported);

		int target = Clamp(level);

		// Skip redundant writes when the current level is already within epsilon.
		if (TryReadScalar(endpoint) is float current && IsWithinEpsilon(current, target))
			return new CaptureLevelResult(CaptureLevelOutcome.Unchanged);

		if (TryApplyAndVerify(endpoint, target))
			return new CaptureLevelResult(CaptureLevelOutcome.Applied);

		// Stale-proxy recovery: obtain a fresh device reference and retry the write
		// exactly once, verifying the read-back again, before declaring failure.
		var fresh = _provider.RefreshEndpoint();
		if (TryApplyAndVerify(fresh, target))
			return new CaptureLevelResult(CaptureLevelOutcome.Applied);

		return new CaptureLevelResult(CaptureLevelOutcome.Failed);
	}

	/// <summary>
	/// Writes the target then confirms it by read-back within epsilon. A thrown
	/// exception (stale proxy) or a read-back that does not match counts as a
	/// failed write.
	/// </summary>
	private static bool TryApplyAndVerify(ICaptureLevelEndpoint endpoint, int target)
	{
		try
		{
			endpoint.SetLevelScalar(target / 100f);
			return IsWithinEpsilon(endpoint.GetLevelScalar(), target);
		}
		catch
		{
			return false;
		}
	}

	// Reads the level scalar, treating any failure (stale proxy) as unreadable so
	// the redundant-write skip and the display read both degrade to "unknown"
	// rather than throwing.
	private static float? TryReadScalar(ICaptureLevelEndpoint endpoint)
	{
		try
		{
			return endpoint.GetLevelScalar();
		}
		catch
		{
			return null;
		}
	}

	private static bool IsWithinEpsilon(float scalar, int target) =>
		Math.Abs(scalar * 100f - target) <= LevelEpsilon;

	private static int Clamp(int level) =>
		level < MinLevel ? MinLevel : level > MaxLevel ? MaxLevel : level;
}
