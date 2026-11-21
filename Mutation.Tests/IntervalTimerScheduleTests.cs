using System;
using System.Collections.Generic;
using CognitiveSupport;
using Xunit;

namespace Mutation.Tests;

public class IntervalTimerScheduleTests
{
	private static readonly DateTime Start = new(2026, 11, 21, 10, 0, 0, DateTimeKind.Utc);

	private DateTime _now = Start;

	private IntervalTimerSchedule NewSchedule() => new(() => _now);

	private static IntervalTimerSettings Timer(string name, int minutes, bool enabled = true, DateTime? nextDue = null) => new()
	{
		Name = name,
		IntervalMinutes = minutes,
		Enabled = enabled,
		NextDueUtc = nextDue,
	};

	[Fact]
	public void NewTimerThatIsOn_IsDueOneIntervalFromNow()
	{
		var timer = Timer("Stretch", 45);
		var schedule = NewSchedule();

		Assert.True(schedule.Sync(new[] { timer }));

		Assert.Equal(Start.AddMinutes(45), timer.NextDueUtc);
		Assert.Equal(TimeSpan.FromMinutes(45), schedule.Remaining(timer));
	}

	[Fact]
	public void TimerThatIsOff_HasNoDueTimeAndNoCountdown()
	{
		var timer = Timer("Stretch", 45, enabled: false, nextDue: Start.AddMinutes(5));
		var schedule = NewSchedule();

		schedule.Sync(new[] { timer });

		Assert.Null(timer.NextDueUtc);
		Assert.Null(schedule.Remaining(timer));
	}

	[Fact]
	public void SavedDueTime_IsKeptAtStartup()
	{
		var timer = Timer("Stretch", 45, nextDue: Start.AddMinutes(12));
		var schedule = NewSchedule();

		Assert.False(schedule.Sync(new[] { timer }));

		Assert.Equal(Start.AddMinutes(12), timer.NextDueUtc);
	}

	[Fact]
	public void SavedDueTimeFurtherThanOneInterval_IsPulledInToOneInterval()
	{
		var timer = Timer("Stretch", 45, nextDue: Start.AddHours(5));
		var schedule = NewSchedule();

		schedule.Sync(new[] { timer });

		Assert.Equal(Start.AddMinutes(45), timer.NextDueUtc);
	}

	[Fact]
	public void Tick_BeforeDue_PlaysNothing()
	{
		var timer = Timer("Stretch", 45);
		var schedule = NewSchedule();
		schedule.Sync(new[] { timer });

		_now = Start.AddMinutes(44);
		var result = schedule.Tick(new[] { timer });

		Assert.Empty(result.Due);
		Assert.False(result.ScheduleChanged);
	}

	[Fact]
	public void Tick_AtDueTime_PlaysAndMovesOnOneInterval()
	{
		var timer = Timer("Stretch", 45);
		var schedule = NewSchedule();
		schedule.Sync(new[] { timer });

		_now = Start.AddMinutes(45).AddSeconds(1);
		var result = schedule.Tick(new[] { timer });

		Assert.Equal(new[] { timer }, result.Due);
		Assert.True(result.ScheduleChanged);
		Assert.Equal(Start.AddMinutes(90), timer.NextDueUtc);
	}

	[Fact]
	public void Tick_FoundLaterThanThePlayWindow_SkipsTheSoundButKeepsTheRhythm()
	{
		// Mutation closed at 10:00 with a 45-minute timer due at 10:45, and opened again at 11:40.
		var timer = Timer("Stretch", 45, nextDue: Start.AddMinutes(45));
		var schedule = NewSchedule();
		_now = Start.AddMinutes(100);
		schedule.Sync(new[] { timer });

		var result = schedule.Tick(new[] { timer });

		Assert.Empty(result.Due);
		Assert.True(result.ScheduleChanged);
		Assert.Equal(Start.AddMinutes(135), timer.NextDueUtc); // 10:45 + 2 × 45 = 12:15
	}

	[Fact]
	public void Tick_JustInsideThePlayWindow_StillPlays()
	{
		var timer = Timer("Stretch", 10);
		var schedule = NewSchedule();
		schedule.Sync(new[] { timer });

		_now = Start.AddMinutes(10) + IntervalTimerSchedule.PlayWindow;
		Assert.Single(schedule.Tick(new[] { timer }).Due);
	}

	[Fact]
	public void Tick_TwoTimersDueTogether_ReturnsBothInListOrder()
	{
		var water = Timer("Water", 30);
		var stretch = Timer("Stretch", 30);
		var timers = new[] { water, stretch };
		var schedule = NewSchedule();
		schedule.Sync(timers);

		_now = Start.AddMinutes(30);
		var result = schedule.Tick(timers);

		Assert.Equal(new[] { water, stretch }, result.Due);
	}

	[Fact]
	public void TurningATimerOn_StartsAFullIntervalFromThatMoment()
	{
		var timer = Timer("Stretch", 45, enabled: false);
		var schedule = NewSchedule();
		schedule.Sync(new[] { timer });

		_now = Start.AddMinutes(7);
		timer.Enabled = true;
		schedule.Sync(new[] { timer });

		Assert.Equal(Start.AddMinutes(52), timer.NextDueUtc);
	}

	[Fact]
	public void TurningATimerOffAndOn_StartsAgainRatherThanResuming()
	{
		var timer = Timer("Stretch", 45);
		var schedule = NewSchedule();
		schedule.Sync(new[] { timer });

		_now = Start.AddMinutes(30);
		timer.Enabled = false;
		schedule.Sync(new[] { timer });
		timer.Enabled = true;
		schedule.Sync(new[] { timer });

		Assert.Equal(Start.AddMinutes(75), timer.NextDueUtc);
	}

	[Fact]
	public void ChangingTheInterval_StartsAFullIntervalFromThatMoment()
	{
		var timer = Timer("Stretch", 45);
		var schedule = NewSchedule();
		schedule.Sync(new[] { timer });

		_now = Start.AddMinutes(10);
		timer.IntervalMinutes = 20;
		schedule.Sync(new[] { timer });

		Assert.Equal(Start.AddMinutes(30), timer.NextDueUtc);
	}

	[Fact]
	public void ChangingOnlyNameOrSound_KeepsTheSchedule()
	{
		var timer = Timer("Stretch", 45);
		var schedule = NewSchedule();
		schedule.Sync(new[] { timer });

		_now = Start.AddMinutes(10);
		timer.Name = "Stand up";
		timer.SoundFile = "CustomAudio/other.wav";
		schedule.Sync(new[] { timer });

		Assert.Equal(Start.AddMinutes(45), timer.NextDueUtc);
	}

	[Fact]
	public void StaleDueTimeCopiedBackBySettingsSave_DoesNotReplayTheTimer()
	{
		// The Settings dialog copied the settings at 10:00, the timer played at 10:45 while the
		// dialog was open, and Save at 10:45:10 wrote the stale 10:45 back over the live timer.
		var timer = Timer("Stretch", 45);
		var schedule = NewSchedule();
		schedule.Sync(new[] { timer });

		_now = Start.AddMinutes(45);
		Assert.Single(schedule.Tick(new[] { timer }).Due);

		_now = Start.AddMinutes(45).AddSeconds(10);
		timer.NextDueUtc = Start.AddMinutes(45);
		Assert.True(schedule.Sync(new[] { timer }));

		Assert.Equal(Start.AddMinutes(90), timer.NextDueUtc);
		Assert.Empty(schedule.Tick(new[] { timer }).Due);
	}

	[Fact]
	public void RemovedTimer_IsForgotten_SoTheSameIdComingBackStartsFresh()
	{
		var timer = Timer("Stretch", 45);
		var schedule = NewSchedule();
		schedule.Sync(new[] { timer });

		schedule.Sync(Array.Empty<IntervalTimerSettings>());
		_now = Start.AddMinutes(10);
		timer.NextDueUtc = null;
		schedule.Sync(new[] { timer });

		Assert.Equal(Start.AddMinutes(55), timer.NextDueUtc);
	}

	[Fact]
	public void TimerWithoutAnId_IsGivenOne()
	{
		var timer = Timer("Stretch", 45);
		timer.Id = "";

		NewSchedule().Sync(new[] { timer });

		Assert.False(string.IsNullOrWhiteSpace(timer.Id));
	}

	[Fact]
	public void IntervalOutsideTheRange_IsClampedForScheduling()
	{
		var timer = Timer("Stretch", 0);

		NewSchedule().Sync(new[] { timer });

		Assert.Equal(Start.AddMinutes(1), timer.NextDueUtc);
	}

	[Theory]
	[InlineData(0, 0)]       // exactly due: the next one is one interval later
	[InlineData(10, 1)]
	[InlineData(44, 1)]
	[InlineData(45, 2)]      // exactly one interval late: that slot has also come, so the next is after it
	[InlineData(91, 3)]
	public void NextAfter_IsTheFirstDueTimeOnTheRhythmAfterNow(int minutesLate, int expectedSteps)
	{
		var dueAt = Start;
		var interval = TimeSpan.FromMinutes(45);
		var now = Start.AddMinutes(minutesLate);

		var next = IntervalTimerSchedule.NextAfter(dueAt, interval, now);

		Assert.Equal(dueAt + TimeSpan.FromTicks(interval.Ticks * Math.Max(1, expectedSteps)), next);
		Assert.True(next > now);
	}

	[Fact]
	public void Remaining_NeverGoesBelowZero()
	{
		var timer = Timer("Stretch", 1);
		var schedule = NewSchedule();
		schedule.Sync(new[] { timer });

		_now = Start.AddMinutes(5);

		Assert.Equal(TimeSpan.Zero, schedule.Remaining(timer));
	}
}
