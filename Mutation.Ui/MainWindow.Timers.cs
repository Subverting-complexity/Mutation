using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using CognitiveSupport;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;

namespace Mutation.Ui;

// The interval timers (issue #456): the once-a-second tick that plays a timer when it is due,
// and the Timers strip under the heading that shows each one.
public sealed partial class MainWindow
{
	private static readonly TimeSpan TimerTickInterval = TimeSpan.FromSeconds(1);

	private readonly IntervalTimerSchedule _timerSchedule = new(() => DateTime.UtcNow);
	private readonly HashSet<string> _timerSoundFailures = new(StringComparer.Ordinal);
	private DispatcherQueueTimer? _timerTicker;

	public ObservableCollection<IntervalTimerRow> TimerRows { get; } = new();

	private List<IntervalTimerSettings> IntervalTimers => _settings.IntervalTimers ??= new List<IntervalTimerSettings>();

	private void InitializeIntervalTimers()
	{
		RebuildTimerRows();

		_timerTicker = DispatcherQueue.CreateTimer();
		_timerTicker.Interval = TimerTickInterval;
		_timerTicker.IsRepeating = true;
		_timerTicker.Tick += (_, _) => OnTimerTick();
		_timerTicker.Start();
	}

	/// <summary>
	/// Called after a settings save. The save may have added, removed, reordered, renamed or
	/// retimed timers, so the rows are rebuilt from scratch. Sound warnings are cleared too: the
	/// user may just have fixed the file, and a timer that still cannot play will warn again
	/// the next time it is due.
	/// </summary>
	private void ApplyIntervalTimerSettings()
	{
		_timerSoundFailures.Clear();
		RebuildTimerRows();
	}

	private void RebuildTimerRows()
	{
		if (_timerSchedule.Sync(IntervalTimers))
			_settingsSaveDebouncer.Trigger();

		TimerRows.Clear();
		foreach (var timer in IntervalTimers)
			TimerRows.Add(new IntervalTimerRow(timer, OnTimerRowToggled));

		TimersStrip.Visibility = TimerRows.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
		UpdateTimerRows();
	}

	private void OnTimerTick()
	{
		try
		{
			IntervalTimerTickResult result = _timerSchedule.Tick(IntervalTimers);
			if (result.Due.Count > 0)
				PlayDueTimers(result.Due);
			if (result.ScheduleChanged)
				_settingsSaveDebouncer.Trigger();
			UpdateTimerRows();
		}
		catch (Exception ex)
		{
			// A failed tick must not stop the ticker: the next one tries again.
			ErrorLogger.LogError("Interval timer tick", ex);
		}
	}

	private void PlayDueTimers(IReadOnlyList<IntervalTimerSettings> due)
	{
		var paths = new List<string?>(due.Count);
		foreach (var timer in due)
			paths.Add(AudioFilePathResolver.Resolve(timer.SoundFile ?? string.Empty));

		IReadOnlyList<bool> played = BeepPlayer.PlayFilesInSequence(paths);
		for (int i = 0; i < due.Count; i++)
		{
			if (played[i])
			{
				_timerSoundFailures.Remove(due[i].Id);
			}
			else
			{
				_timerSoundFailures.Add(due[i].Id);
				ErrorLogger.LogError("Interval timer", new System.IO.FileNotFoundException(
					$"The sound for the \"{due[i].Name}\" timer could not be played, so the built-in beep played instead.",
					due[i].SoundFile));
			}
		}
	}

	// The toggle on the main window and the toggle in Settings are the same switch, saved at once.
	private void OnTimerRowToggled(IntervalTimerRow row, bool isOn)
	{
		row.Timer.Enabled = isOn;
		_timerSchedule.Sync(IntervalTimers);
		_settingsSaveDebouncer.Trigger();
		UpdateTimerRows();
		row.RefreshHelpText();
	}

	private void UpdateTimerRows()
	{
		foreach (var row in TimerRows)
		{
			string countdown = IntervalTimerText.Countdown(_timerSchedule.Remaining(row.Timer));
			string? warning = _timerSoundFailures.Contains(row.Timer.Id) ? IntervalTimerText.SoundFailedWarning : null;
			row.Update(countdown, warning);
		}
	}

	private void StopIntervalTimers() => _timerTicker?.Stop();
}
