using System;
using System.Collections.Generic;

namespace CognitiveSupport;

/// <summary>
/// Decides when each interval timer plays (issue #456). Pure: the clock is injected and
/// nothing here plays a sound or touches the disk.
/// <para>
/// The schedule, not the settings, owns each timer's next due time. The Settings dialog edits
/// a copy of the settings taken when it opened, and Save copies that copy back over the live
/// settings — including a due time that went stale while the dialog was open. If the saved
/// value were trusted, a timer that played while the dialog was open would be put back to
/// "due a moment ago" and play a second time. <see cref="Sync"/> therefore keeps its own due
/// time for any timer it already knows, and writes it back over whatever the settings say.
/// </para>
/// </summary>
public sealed class IntervalTimerSchedule
{
	/// <summary>
	/// How late a due time may be found and still play. A tick runs every second, so a timer is
	/// normally found well inside this. Anything later means Mutation was closed or the PC was
	/// asleep through the due time, and a reminder that is that late is skipped rather than
	/// played out of the blue.
	/// </summary>
	public static readonly TimeSpan PlayWindow = TimeSpan.FromSeconds(30);

	private readonly Func<DateTime> _utcNow;
	private readonly Dictionary<string, Entry> _entries = new(StringComparer.Ordinal);

	public IntervalTimerSchedule(Func<DateTime> utcNow)
	{
		_utcNow = utcNow ?? throw new ArgumentNullException(nameof(utcNow));
	}

	/// <summary>
	/// Brings the schedule in line with the timers as they are now: at startup, after a
	/// settings save, and after a toggle on the main window. Returns true when any timer's
	/// <see cref="IntervalTimerSettings.NextDueUtc"/> changed, so the caller knows to save.
	/// <list type="bullet">
	/// <item>A timer that is off has no due time.</item>
	/// <item>A timer that was just turned on, or whose interval changed, starts a full interval now.</item>
	/// <item>A timer the schedule already knows keeps its own due time, whatever the settings say.</item>
	/// <item>A timer seen for the first time (startup) keeps its saved due time, or starts now if it has none.</item>
	/// </list>
	/// </summary>
	public bool Sync(IReadOnlyList<IntervalTimerSettings> timers)
	{
		ArgumentNullException.ThrowIfNull(timers);

		DateTime now = _utcNow();
		bool changed = false;
		var seen = new HashSet<string>(StringComparer.Ordinal);

		foreach (var timer in timers)
		{
			if (string.IsNullOrWhiteSpace(timer.Id))
			{
				timer.Id = Guid.NewGuid().ToString("N");
				changed = true;
			}
			seen.Add(timer.Id);

			int interval = IntervalTimerSettings.ClampInterval(timer.IntervalMinutes);
			TimeSpan length = TimeSpan.FromMinutes(interval);
			DateTime? next;

			if (!timer.Enabled)
				next = null;
			else if (_entries.TryGetValue(timer.Id, out var known))
				next = !known.Enabled || known.IntervalMinutes != interval || known.NextDueUtc is null
					? now + length
					: known.NextDueUtc;
			else
				next = timer.NextDueUtc ?? now + length;

			// A due time further away than one interval can only come from a clock that moved
			// back or a hand-edited file. Waiting it out would leave the timer silent for longer
			// than the user asked for.
			if (next is DateTime n && n > now + length)
				next = now + length;

			_entries[timer.Id] = new Entry(timer.Enabled, interval, next);
			if (timer.NextDueUtc != next)
			{
				timer.NextDueUtc = next;
				changed = true;
			}
		}

		foreach (var id in new List<string>(_entries.Keys))
		{
			if (!seen.Contains(id))
				_entries.Remove(id);
		}

		return changed;
	}

	/// <summary>
	/// Moves every timer whose due time has come on to its next due time, and returns the ones
	/// that should play now, in list order. A due time found later than <see cref="PlayWindow"/>
	/// is skipped silently. Either way the next due time stays on the original rhythm: a
	/// 45-minute timer due at 10:00 that was missed is next due at 10:45, 11:30 and so on.
	/// </summary>
	public IntervalTimerTickResult Tick(IReadOnlyList<IntervalTimerSettings> timers)
	{
		ArgumentNullException.ThrowIfNull(timers);

		DateTime now = _utcNow();
		var due = new List<IntervalTimerSettings>();
		bool changed = false;

		foreach (var timer in timers)
		{
			if (!timer.Enabled || !_entries.TryGetValue(timer.Id, out var entry) || entry.NextDueUtc is not DateTime dueAt)
				continue;
			if (now < dueAt)
				continue;

			TimeSpan length = TimeSpan.FromMinutes(entry.IntervalMinutes);
			DateTime next = NextAfter(dueAt, length, now);
			_entries[timer.Id] = entry with { NextDueUtc = next };
			timer.NextDueUtc = next;
			changed = true;

			if (now - dueAt <= PlayWindow)
				due.Add(timer);
		}

		return new IntervalTimerTickResult(due, changed);
	}

	/// <summary>Time left until the timer plays, or null when it is off or not yet scheduled.</summary>
	public TimeSpan? Remaining(IntervalTimerSettings timer)
	{
		ArgumentNullException.ThrowIfNull(timer);

		if (!timer.Enabled || !_entries.TryGetValue(timer.Id, out var entry) || entry.NextDueUtc is not DateTime dueAt)
			return null;

		TimeSpan left = dueAt - _utcNow();
		return left < TimeSpan.Zero ? TimeSpan.Zero : left;
	}

	/// <summary>The first due time on the rhythm <paramref name="dueAt"/> + k × <paramref name="interval"/> that is after <paramref name="now"/>.</summary>
	public static DateTime NextAfter(DateTime dueAt, TimeSpan interval, DateTime now)
	{
		if (interval <= TimeSpan.Zero)
			throw new ArgumentOutOfRangeException(nameof(interval));
		if (now < dueAt)
			return dueAt;

		long steps = (now - dueAt).Ticks / interval.Ticks + 1;
		return dueAt + TimeSpan.FromTicks(interval.Ticks * steps);
	}

	private sealed record Entry(bool Enabled, int IntervalMinutes, DateTime? NextDueUtc);
}

/// <param name="Due">The timers to play now, in list order.</param>
/// <param name="ScheduleChanged">True when any due time moved, so the settings need saving.</param>
public sealed record IntervalTimerTickResult(IReadOnlyList<IntervalTimerSettings> Due, bool ScheduleChanged);
