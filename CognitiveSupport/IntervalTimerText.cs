using System;
using System.Collections.Generic;

namespace CognitiveSupport;

/// <summary>
/// The words the main window shows for an interval timer (issue #456).
/// </summary>
public static class IntervalTimerText
{
	public const string Off = "off";

	public const string SoundFailedWarning = "The sound file could not be played, so the built-in beep played instead.";

	/// <summary>
	/// "next in 1 hour and 12 minutes", "next in 2 hours", "next in 5 minutes" or
	/// "next in 45 seconds"; "off" when <paramref name="remaining"/> is null.
	/// <para>
	/// Everything rounds up, starting from whole seconds. Rounding down would say "0 minutes"
	/// for most of the last minute, and "4 minutes" at 4 minutes 59 seconds. Rounding seconds
	/// first is what keeps 59.5 seconds from reading "60 seconds": it becomes 60 whole seconds,
	/// which is "1 minute".
	/// </para>
	/// </summary>
	public static string Countdown(TimeSpan? remaining)
	{
		if (remaining is not TimeSpan left)
			return Off;

		long seconds = Math.Max(1, (long)Math.Ceiling(Math.Max(0, left.TotalSeconds)));
		if (seconds < 60)
			return $"next in {Plural(seconds, "second")}";

		long minutes = (seconds + 59) / 60;
		if (minutes < 60)
			return $"next in {Plural(minutes, "minute")}";

		long hours = minutes / 60;
		long rest = minutes % 60;
		return rest == 0
			? $"next in {Plural(hours, "hour")}"
			: $"next in {Plural(hours, "hour")} and {Plural(rest, "minute")}";
	}

	/// <summary>
	/// What a screen reader calls the timer's toggle: "Stretch timer". A name that already ends
	/// in "timer" is left alone, so "Tea timer" is not read as "Tea timer timer".
	/// </summary>
	public static string AccessibleName(string? name)
	{
		string trimmed = (name ?? string.Empty).Trim();
		if (trimmed.Length == 0)
			return "Timer";
		return trimmed.EndsWith("timer", StringComparison.OrdinalIgnoreCase) ? trimmed : $"{trimmed} timer";
	}

	/// <summary>"Timer 1", or the next number up that no other timer is already called.</summary>
	public static string NextDefaultName(IEnumerable<string?> existingNames)
	{
		var taken = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
		foreach (var name in existingNames)
		{
			if (!string.IsNullOrWhiteSpace(name))
				taken.Add(name.Trim());
		}

		for (int n = 1; ; n++)
		{
			string candidate = $"Timer {n}";
			if (!taken.Contains(candidate))
				return candidate;
		}
	}

	private static string Plural(long count, string unit) => count == 1 ? $"1 {unit}" : $"{count} {unit}s";
}
