using System;

namespace CognitiveSupport;

/// <summary>
/// One interval timer: a named reminder that plays a sound every <see cref="IntervalMinutes"/>
/// while it is on (issue #456).
/// </summary>
public class IntervalTimerSettings
{
	public const int MinIntervalMinutes = 1;
	public const int MaxIntervalMinutes = 24 * 60;
	public const int DefaultIntervalMinutes = 30;

	/// <summary>
	/// The sound a new timer starts with. Relative, so it resolves against the install folder
	/// the same way the bundled beep files do.
	/// </summary>
	public const string DefaultSoundFile = "CustomAudio/mixkit-mouse-hard-clicking-1111.wav";

	/// <summary>
	/// Stable identity, so the schedule can tell "this timer, edited" from "a different timer
	/// now at this position". The settings dialog commits lists index by index, so position
	/// alone would carry one timer's countdown over to another after a reorder or a removal.
	/// </summary>
	public string Id { get; set; } = Guid.NewGuid().ToString("N");

	public string Name { get; set; } = string.Empty;

	public bool Enabled { get; set; }

	public int IntervalMinutes { get; set; } = DefaultIntervalMinutes;

	public string? SoundFile { get; set; } = DefaultSoundFile;

	/// <summary>
	/// When the timer next plays, in UTC. Written by <see cref="IntervalTimerSchedule"/>, which
	/// owns it; saved so the schedule carries on across a restart. Null while the timer is off.
	/// </summary>
	public DateTime? NextDueUtc { get; set; }

	public static int ClampInterval(int minutes) => Math.Clamp(minutes, MinIntervalMinutes, MaxIntervalMinutes);
}
