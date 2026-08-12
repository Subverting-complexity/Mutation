using Mutation.Ui.Core;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Xunit;

namespace Mutation.Tests;

/// <summary>
/// The watch that keeps the pointer where a capture left it while a magnifier tries to move it
/// somewhere else (issue #379).
///
/// <para>
/// The rule that makes a watch this long safe is the one worth pinning hardest: it stands down
/// for good the instant a hand touches the mouse, and never touches the pointer again after that.
/// Without it this would be an application dragging the pointer back out from under its user for
/// a second and a half.
/// </para>
/// </summary>
public class PointerHoldTests
{
	private static readonly TimeSpan Poll = TimeSpan.FromMilliseconds(40);
	private static readonly TimeSpan Hold = TimeSpan.FromMilliseconds(400);

	// A clock the test drives instead of waiting, so a second-and-a-half watch takes no time at
	// all to exercise. Each wait advances it by the interval asked for.
	private sealed class FakeClock
	{
		public TimeSpan Now { get; private set; } = TimeSpan.Zero;
		public List<TimeSpan> Waits { get; } = new();
		public Action? OnWait;

		public Func<TimeSpan, Task> Delay => interval =>
		{
			Waits.Add(interval);
			Now += interval;
			OnWait?.Invoke();
			return Task.CompletedTask;
		};

		public Func<TimeSpan> Elapsed => () => Now;
	}

	private static Task<PointerHoldOutcome> Run(
		FakeClock clock,
		Func<bool> userHasTheMouse,
		Func<bool> restoreIfDrifted,
		Func<bool>? stillCurrent = null,
		Func<Task>? afterRestore = null,
		TimeSpan? hold = null) =>
		PointerHold.RunAsync(
			userHasTheMouse,
			stillCurrent ?? (() => true),
			restoreIfDrifted,
			afterRestore ?? (() => Task.CompletedTask),
			clock.Delay,
			clock.Elapsed,
			Poll,
			hold ?? Hold);

	[Fact]
	public async Task NothingMovesThePointer_TheWatchJustRunsOutOfTime()
	{
		var clock = new FakeClock();
		int checks = 0;

		var outcome = await Run(clock, () => false, () => { checks++; return false; });

		Assert.Equal(PointerHoldOutcome.TimeUp, outcome);
		Assert.Equal(10, checks); // 400 ms at 40 ms a look
	}

	[Fact]
	public async Task SomethingMovesThePointer_ItIsPutBackEveryTime()
	{
		// The magnifier can do this more than once — on the focus change, and again when the
		// caret it is following moves. A watch that corrected once would be beaten by the second.
		var clock = new FakeClock();
		int corrections = 0;

		var outcome = await Run(clock, () => false, () => { corrections++; return true; });

		Assert.Equal(PointerHoldOutcome.TimeUp, outcome);
		Assert.Equal(10, corrections);
	}

	[Fact]
	public async Task AHandOnTheMouse_EndsTheWatchAndThePointerIsNeverTouchedAgain()
	{
		// The whole reason this is allowed to run for as long as it does.
		var clock = new FakeClock();
		int corrections = 0;
		bool userHasTheMouse = false;
		clock.OnWait = () =>
		{
			if (clock.Now >= TimeSpan.FromMilliseconds(120))
				userHasTheMouse = true;
		};

		var outcome = await Run(clock, () => userHasTheMouse, () => { corrections++; return true; });

		Assert.Equal(PointerHoldOutcome.UserTookTheMouse, outcome);
		Assert.Equal(2, corrections);
	}

	[Fact]
	public async Task AHandOnTheMouseFromTheStart_MeansNoCorrectionAtAll()
	{
		var clock = new FakeClock();
		int corrections = 0;

		var outcome = await Run(clock, () => true, () => { corrections++; return true; });

		Assert.Equal(PointerHoldOutcome.UserTookTheMouse, outcome);
		Assert.Equal(0, corrections);
	}

	[Fact]
	public async Task AHandOnTheMouseDuringTheWiggle_StopsBeforeTheNextCorrection()
	{
		// The wiggle that follows a correction moves the pointer on purpose and takes a while, so
		// the user can easily reach for the mouse while it runs. Asking again straight after it
		// is what stops the watch correcting one more time first.
		var clock = new FakeClock();
		bool userHasTheMouse = false;
		int corrections = 0;

		var outcome = await Run(
			clock,
			() => userHasTheMouse,
			() => { corrections++; return true; },
			afterRestore: () => { userHasTheMouse = true; return Task.CompletedTask; });

		Assert.Equal(PointerHoldOutcome.UserTookTheMouse, outcome);
		Assert.Equal(1, corrections);
	}

	[Fact]
	public async Task ACorrectionIsFollowedByTheWiggle_ButOnlyWhenOneWasNeeded()
	{
		// Something moved the pointer, so it probably moved the magnified view too. Nothing moved
		// it, so there is nothing for the view to be brought back to.
		var clock = new FakeClock();
		int wiggles = 0;
		bool drifted = true;

		await Run(
			clock,
			() => false,
			() => { bool answer = drifted; drifted = false; return answer; },
			afterRestore: () => { wiggles++; return Task.CompletedTask; });

		Assert.Equal(1, wiggles);
	}

	[Fact]
	public async Task ANewCaptureTakesOver_AndThisWatchStandsDown()
	{
		var clock = new FakeClock();
		int corrections = 0;
		bool current = true;
		clock.OnWait = () =>
		{
			if (clock.Now >= TimeSpan.FromMilliseconds(80))
				current = false;
		};

		var outcome = await Run(
			clock,
			() => false,
			() => { corrections++; return true; },
			stillCurrent: () => current);

		Assert.Equal(PointerHoldOutcome.Superseded, outcome);
		Assert.Equal(1, corrections);
	}

	[Fact]
	public async Task ZeroLength_NeverLooksAtThePointerAtAll()
	{
		var clock = new FakeClock();
		int corrections = 0;

		var outcome = await Run(clock, () => false, () => { corrections++; return true; }, hold: TimeSpan.Zero);

		Assert.Equal(PointerHoldOutcome.TimeUp, outcome);
		Assert.Equal(0, corrections);
		Assert.Empty(clock.Waits);
	}

	[Fact]
	public async Task ACorrectionOnTheLastTick_DoesNotStartAWiggleThatOutlivesTheWatch()
	{
		// The wiggle runs for as long as the user set it to, up to several seconds. Starting one
		// on the last tick would keep the anchor, the hook and the whole apparatus alive well
		// past the length the watch promised. The correction has already been made by then; only
		// the wiggle that would have advertised it is given up.
		var clock = new FakeClock();
		int wiggles = 0;

		var outcome = await Run(
			clock,
			() => false,
			() => true,
			afterRestore: () => { wiggles++; return Task.CompletedTask; },
			hold: Poll); // one tick, and it is over

		Assert.Equal(PointerHoldOutcome.TimeUp, outcome);
		Assert.Equal(0, wiggles);
	}

	[Fact]
	public async Task NullArgumentsAreRejected()
	{
		var clock = new FakeClock();
		Func<bool> yes = () => true;
		Func<Task> nothing = () => Task.CompletedTask;

		await Assert.ThrowsAsync<ArgumentNullException>(() =>
			PointerHold.RunAsync(null!, yes, yes, nothing, clock.Delay, clock.Elapsed, Poll, Hold));
		await Assert.ThrowsAsync<ArgumentNullException>(() =>
			PointerHold.RunAsync(yes, null!, yes, nothing, clock.Delay, clock.Elapsed, Poll, Hold));
		await Assert.ThrowsAsync<ArgumentNullException>(() =>
			PointerHold.RunAsync(yes, yes, null!, nothing, clock.Delay, clock.Elapsed, Poll, Hold));
		await Assert.ThrowsAsync<ArgumentNullException>(() =>
			PointerHold.RunAsync(yes, yes, yes, null!, clock.Delay, clock.Elapsed, Poll, Hold));
	}

	[Fact]
	public async Task APollIntervalOfNothingIsRejected()
	{
		// It would spin without ever advancing the clock.
		var clock = new FakeClock();
		Func<bool> yes = () => true;

		await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() =>
			PointerHold.RunAsync(
				() => false, yes, yes, () => Task.CompletedTask,
				clock.Delay, clock.Elapsed, TimeSpan.Zero, Hold));
	}
}
