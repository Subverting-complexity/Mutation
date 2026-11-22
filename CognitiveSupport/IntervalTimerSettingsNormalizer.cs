using System;
using System.Collections.Generic;

namespace CognitiveSupport;

/// <summary>
/// Repairs interval timers loaded from a settings file that was hand-edited or written by an
/// older version, so the rest of the app can trust every timer it sees (issue #456).
/// </summary>
public static class IntervalTimerSettingsNormalizer
{
	/// <summary>
	/// Drops null entries, gives every timer a unique id and a name, and clamps the interval.
	/// Returns true when anything changed, so the caller knows the file needs saving.
	/// </summary>
	public static bool Normalize(List<IntervalTimerSettings> timers)
	{
		ArgumentNullException.ThrowIfNull(timers);

		bool changed = timers.RemoveAll(t => t is null) > 0;
		var ids = new HashSet<string>(StringComparer.Ordinal);

		foreach (var timer in timers)
		{
			// A copied-and-pasted timer in the JSON shares its id with the original. Two timers
			// with one id would share one countdown, so the second gets an id of its own.
			if (string.IsNullOrWhiteSpace(timer.Id) || !ids.Add(timer.Id))
			{
				timer.Id = Guid.NewGuid().ToString("N");
				ids.Add(timer.Id);
				changed = true;
			}

			if (string.IsNullOrWhiteSpace(timer.Name))
			{
				timer.Name = IntervalTimerText.NextDefaultName(timers.ConvertAll(t => t.Name));
				changed = true;
			}

			int clamped = IntervalTimerSettings.ClampInterval(timer.IntervalMinutes);
			if (clamped != timer.IntervalMinutes)
			{
				timer.IntervalMinutes = clamped;
				changed = true;
			}
		}

		return changed;
	}
}
