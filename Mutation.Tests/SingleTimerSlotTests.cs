using System;
using System.Collections.Generic;
using Mutation.Ui.Core;
using Xunit;

namespace Mutation.Tests;

// The one-timer-at-a-time rule the waveform monitor relies on. Its Initialize runs
// again on every Settings-dialog save, and used to leave the previous 30 FPS timer
// running (issue #231).
public class SingleTimerSlotTests
{
	private sealed class FakeTimer
	{
		public bool IsRunning { get; set; }
	}

	// Tracks every timer the slot ever created, so "how many are still ticking" is
	// answerable rather than assumed.
	private sealed class TimerFactory
	{
		public List<FakeTimer> Created { get; } = new();

		public SingleTimerSlot<FakeTimer> CreateSlot() => new(
			create: () =>
			{
				var timer = new FakeTimer();
				Created.Add(timer);
				return timer;
			},
			start: timer => timer.IsRunning = true,
			stop: timer => timer.IsRunning = false);

		public int RunningCount => Created.FindAll(t => t.IsRunning).Count;
	}

	[Fact]
	public void Restart_StartsATimer()
	{
		var factory = new TimerFactory();
		using var slot = factory.CreateSlot();

		slot.Restart();

		Assert.True(slot.IsRunning);
		Assert.Single(factory.Created);
		Assert.Equal(1, factory.RunningCount);
	}

	[Fact]
	public void Restart_CalledRepeatedly_LeavesExactlyOneTimerRunning()
	{
		var factory = new TimerFactory();
		using var slot = factory.CreateSlot();

		for (int i = 0; i < 10; i++)
			slot.Restart();

		Assert.Equal(10, factory.Created.Count);
		Assert.Equal(1, factory.RunningCount);
		Assert.Same(factory.Created[9], slot.Current);
	}

	[Fact]
	public void Stop_AfterRepeatedRestarts_LeavesNothingRunning()
	{
		var factory = new TimerFactory();
		using var slot = factory.CreateSlot();
		for (int i = 0; i < 5; i++)
			slot.Restart();

		slot.Stop();

		Assert.Equal(0, factory.RunningCount);
		Assert.False(slot.IsRunning);
		Assert.Null(slot.Current);
	}

	[Fact]
	public void Stop_WithNothingRunning_IsANoOp()
	{
		var factory = new TimerFactory();
		using var slot = factory.CreateSlot();

		slot.Stop();
		slot.Stop();

		Assert.Empty(factory.Created);
		Assert.False(slot.IsRunning);
	}

	[Fact]
	public void Dispose_StopsTheRunningTimer()
	{
		var factory = new TimerFactory();
		var slot = factory.CreateSlot();
		slot.Restart();

		slot.Dispose();

		Assert.Equal(0, factory.RunningCount);
		Assert.False(slot.IsRunning);
	}

	// Restarting after a stop is the visualization being switched off and on again.
	[Fact]
	public void Restart_AfterStop_StartsAFreshTimer()
	{
		var factory = new TimerFactory();
		using var slot = factory.CreateSlot();
		slot.Restart();
		var first = slot.Current;
		slot.Stop();

		slot.Restart();

		Assert.NotSame(first, slot.Current);
		Assert.Equal(1, factory.RunningCount);
	}

	// A timer whose Start throws must still be owned by the slot, or the next
	// Restart creates another one on top of a timer nobody can reach.
	[Fact]
	public void Restart_WhenStartThrows_StillHoldsTheTimerForTheNextStop()
	{
		var created = new List<FakeTimer>();
		using var slot = new SingleTimerSlot<FakeTimer>(
			create: () => { var t = new FakeTimer(); created.Add(t); return t; },
			start: _ => throw new InvalidOperationException("start failed"),
			stop: timer => timer.IsRunning = false);

		Assert.Throws<InvalidOperationException>(() => slot.Restart());

		Assert.True(slot.IsRunning);
		Assert.Same(created[0], slot.Current);
	}

	[Theory]
	[InlineData(true, false, false)]
	[InlineData(false, true, false)]
	[InlineData(false, false, true)]
	public void Constructor_NullDependency_Throws(bool nullCreate, bool nullStart, bool nullStop) =>
		Assert.Throws<ArgumentNullException>(() => new SingleTimerSlot<FakeTimer>(
			nullCreate ? null! : () => new FakeTimer(),
			nullStart ? null! : _ => { },
			nullStop ? null! : _ => { }));
}
