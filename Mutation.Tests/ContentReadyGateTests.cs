using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Mutation.Ui.Core;
using Xunit;

namespace Mutation.Tests;

public class ContentReadyGateTests
{
	private static readonly TimeSpan Poll = TimeSpan.FromMilliseconds(50);
	private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(5);

	// A clock the test drives instead of waiting: each "delay" advances it by however
	// much the caller asked to wait, optionally stretched to model a UI thread that is
	// too busy to honour the interval — which is the case the real timeout exists for.
	private sealed class FakeClock
	{
		private readonly double _stretchFactor;

		public FakeClock(double stretchFactor = 1.0) => _stretchFactor = stretchFactor;

		public TimeSpan Now { get; private set; } = TimeSpan.Zero;

		public List<TimeSpan> Delays { get; } = new();

		public Func<TimeSpan, Task> Delay => interval =>
		{
			Delays.Add(interval);
			Now += interval * _stretchFactor;
			return Task.CompletedTask;
		};

		public Func<TimeSpan> Elapsed => () => Now;
	}

	[Fact]
	public async Task ContentAlreadyReady_ReturnsImmediatelyWithoutWaiting()
	{
		var clock = new FakeClock();
		int checks = 0;

		bool ready = await ContentReadyGate.WaitAsync(
			() => { checks++; return true; }, clock.Delay, clock.Elapsed, Poll, Timeout);

		Assert.True(ready);
		Assert.Equal(1, checks);
		Assert.Empty(clock.Delays);
	}

	[Fact]
	public async Task ContentReadyAfterAFewTurns_ReturnsReadyAndStopsPolling()
	{
		var clock = new FakeClock();
		int checks = 0;

		bool ready = await ContentReadyGate.WaitAsync(
			() => ++checks > 3, clock.Delay, clock.Elapsed, Poll, Timeout);

		Assert.True(ready);
		// Three "not ready" checks, so three waits, then the fourth check succeeds.
		Assert.Equal(3, clock.Delays.Count);
		Assert.All(clock.Delays, d => Assert.Equal(Poll, d));
	}

	// A window that never loads must not wedge startup: the caller has a degraded path
	// (a system message box) and has to be allowed to reach it.
	[Fact]
	public async Task ContentNeverReady_GivesUpOnceTheTimeoutHasElapsed()
	{
		var clock = new FakeClock();

		bool ready = await ContentReadyGate.WaitAsync(
			() => false,
			clock.Delay,
			clock.Elapsed,
			TimeSpan.FromMilliseconds(100),
			TimeSpan.FromMilliseconds(500));

		Assert.False(ready);
		Assert.Equal(TimeSpan.FromMilliseconds(500), clock.Now);
	}

	// The bound has to be real time, not a count of poll intervals. A UI thread too busy
	// to load its content is exactly the thread that takes far longer than the interval
	// asked for, so counting intervals would stretch a 500 ms bound to 5 seconds here.
	[Fact]
	public async Task PollsThatOverrunTheirInterval_StillStopAtTheTimeout()
	{
		var clock = new FakeClock(stretchFactor: 10);

		bool ready = await ContentReadyGate.WaitAsync(
			() => false,
			clock.Delay,
			clock.Elapsed,
			TimeSpan.FromMilliseconds(100),
			TimeSpan.FromMilliseconds(500));

		Assert.False(ready);
		// One poll costs 1000 ms of real time, so the very next check gives up.
		Assert.Single(clock.Delays);
	}

	[Fact]
	public async Task ZeroTimeout_ChecksOnceAndGivesUp()
	{
		var clock = new FakeClock();
		int checks = 0;

		bool ready = await ContentReadyGate.WaitAsync(
			() => { checks++; return false; }, clock.Delay, clock.Elapsed, Poll, TimeSpan.Zero);

		Assert.False(ready);
		Assert.Equal(1, checks);
		Assert.Empty(clock.Delays);
	}

	[Fact]
	public async Task NegativeTimeout_ChecksOnceAndGivesUp()
	{
		var clock = new FakeClock();
		int checks = 0;

		bool ready = await ContentReadyGate.WaitAsync(
			() => { checks++; return false; },
			clock.Delay,
			clock.Elapsed,
			Poll,
			TimeSpan.FromMilliseconds(-1));

		Assert.False(ready);
		Assert.Equal(1, checks);
		Assert.Empty(clock.Delays);
	}

	// A timeout that is not a whole multiple of the poll interval still stops at the
	// first check past it rather than running on to the next multiple.
	[Fact]
	public async Task TimeoutThatIsNotAMultipleOfThePollInterval_StopsAtTheFirstCheckPastIt()
	{
		var clock = new FakeClock();

		bool ready = await ContentReadyGate.WaitAsync(
			() => false,
			clock.Delay,
			clock.Elapsed,
			TimeSpan.FromMilliseconds(100),
			TimeSpan.FromMilliseconds(250));

		Assert.False(ready);
		Assert.Equal(3, clock.Delays.Count);
		Assert.Equal(TimeSpan.FromMilliseconds(300), clock.Now);
	}

	// Content that becomes ready during the last permitted wait is reported as ready,
	// not as a timeout — the check always precedes the give-up test.
	[Fact]
	public async Task ContentReadyOnTheFinalCheck_IsReportedAsReady()
	{
		var clock = new FakeClock();
		int checks = 0;

		bool ready = await ContentReadyGate.WaitAsync(
			() => ++checks > 2,
			clock.Delay,
			clock.Elapsed,
			TimeSpan.FromMilliseconds(100),
			TimeSpan.FromMilliseconds(200));

		Assert.True(ready);
		Assert.Equal(2, clock.Delays.Count);
	}

	[Theory]
	[InlineData(0)]
	[InlineData(-1)]
	public async Task NonPositivePollInterval_IsRejected(int milliseconds)
	{
		await Assert.ThrowsAsync<ArgumentOutOfRangeException>(
			() => ContentReadyGate.WaitAsync(
				() => false,
				_ => Task.CompletedTask,
				() => TimeSpan.Zero,
				TimeSpan.FromMilliseconds(milliseconds),
				Timeout));
	}

	[Fact]
	public async Task MissingCallbacks_AreRejected()
	{
		await Assert.ThrowsAsync<ArgumentNullException>(
			() => ContentReadyGate.WaitAsync(null!, _ => Task.CompletedTask, () => TimeSpan.Zero, Poll, Timeout));
		await Assert.ThrowsAsync<ArgumentNullException>(
			() => ContentReadyGate.WaitAsync(() => false, null!, () => TimeSpan.Zero, Poll, Timeout));
		await Assert.ThrowsAsync<ArgumentNullException>(
			() => ContentReadyGate.WaitAsync(() => false, _ => Task.CompletedTask, null!, Poll, Timeout));
	}
}
