using Mutation.Ui.Core;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Xunit;

namespace Mutation.Tests;

/// <summary>
/// The watch that keeps the pointer where a capture — or the user's own hand — left it, undoing
/// anything else that moves it (issues #379 and #382).
///
/// <para>
/// The rule worth pinning hardest is that the hand is never fought. Not by standing down on the
/// first real event — a hand resting on a high-polling-rate mouse streams real events while
/// doing nothing, and a hold that stood down on one never corrected anything — but by following:
/// hand movement re-baselines the defended position, so a correction can never land where the
/// hand is not.
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

	// A pointer the test moves at will and that records every write.
	private sealed class FakeCursor : ICursorPosition
	{
		public CursorPoint Position;
		public bool Readable = true;
		public bool Writable = true;
		public List<CursorPoint> Writes { get; } = new();

		public bool TryGet(out CursorPoint position)
		{
			position = Position;
			return Readable;
		}

		public bool TrySet(CursorPoint position)
		{
			if (!Writable)
				return false;
			Writes.Add(position);
			Position = position;
			return true;
		}
	}

	private static readonly CursorPoint Baseline = new(100, 100);

	private sealed class Harness
	{
		public FakeClock Clock { get; } = new();
		public FakeCursor Cursor { get; } = new() { Position = Baseline };
		public long Acts;
		public long Steps;
		public long Grabs;
		public List<CursorPoint> Followed { get; } = new();
		public int Wiggles;
		public Func<Task<CursorPoint?>>? OnWiggle;
		public bool Current = true;

		public Task<PointerHoldOutcome> Run(TimeSpan? hold = null) =>
			PointerHold.RunAsync(
				Cursor,
				Baseline,
				() => Acts,
				() => Steps,
				() => Grabs,
				() => Current,
				Followed.Add,
				() => { Wiggles++; return OnWiggle?.Invoke() ?? Task.FromResult<CursorPoint?>(null); },
				Clock.Delay,
				Clock.Elapsed,
				Poll,
				hold ?? Hold);
	}

	[Fact]
	public async Task NothingMovesThePointer_TheWatchJustRunsOutOfTime()
	{
		var h = new Harness();

		var outcome = await h.Run();

		Assert.Equal(PointerHoldOutcome.TimeUp, outcome);
		Assert.Equal(10, h.Clock.Waits.Count); // 400 ms at 40 ms a look
		Assert.Empty(h.Cursor.Writes);
		Assert.Empty(h.Followed);
	}

	[Fact]
	public async Task AGrab_IsUndoneAndTheViewIsWiggledBack()
	{
		// The pointer moved and no hand moved it — a magnifier routing to a caret.
		var h = new Harness();
		h.Clock.OnWait = () => h.Cursor.Position = new CursorPoint(500, 300);

		var outcome = await h.Run();

		Assert.Equal(PointerHoldOutcome.TimeUp, outcome);
		Assert.All(h.Cursor.Writes, w => Assert.Equal(Baseline, w));
		Assert.Equal(10, h.Cursor.Writes.Count); // grabbed on every tick, restored on every tick
		Assert.Equal(9, h.Wiggles); // the last tick's wiggle is given up — see the bound test
	}

	[Fact]
	public async Task RestingHandJitter_IsFollowedNotFought()
	{
		// The measured failure of the first design (issue #382): a resting hand streams real
		// micro-moves. Each one must walk the baseline along, never trigger a correction.
		var h = new Harness();
		int tick = 0;
		h.Clock.OnWait = () =>
		{
			tick++;
			h.Steps++; // the hand made this move
			h.Cursor.Position = new CursorPoint(100 + (tick % 2), 100);
		};

		var outcome = await h.Run();

		Assert.Equal(PointerHoldOutcome.TimeUp, outcome);
		Assert.Empty(h.Cursor.Writes);
		Assert.Equal(10, h.Followed.Count);
	}

	[Fact]
	public async Task AGrabAfterTheHandMoved_IsUndoneToWhereTheHandWas()
	{
		// The hand travels somewhere and rests; then the magnifier grabs. The pointer must come
		// back to where the hand left it, not to where the capture ended.
		var h = new Harness();
		var handRestedAt = new CursorPoint(240, 180);
		int tick = 0;
		h.Clock.OnWait = () =>
		{
			tick++;
			if (tick == 1)
			{
				h.Steps++;
				h.Cursor.Position = handRestedAt;
			}
			if (tick == 3)
				h.Cursor.Position = new CursorPoint(700, 20); // the grab, no steps behind it
		};

		var outcome = await h.Run();

		Assert.Equal(PointerHoldOutcome.TimeUp, outcome);
		Assert.Equal(new[] { handRestedAt }, h.Followed);
		Assert.All(h.Cursor.Writes, w => Assert.Equal(handRestedAt, w));
		Assert.Single(h.Cursor.Writes);
	}

	[Fact]
	public async Task AGrabInTheSameLookAsRestingJitter_IsUndoneNotFollowed()
	{
		// The measured defeat of the first counter design (issue #384): the resting hand's
		// jitter advances the step count in every look, so the look containing the magnifier's
		// caret grab also says "the hand moved" — and a follow would adopt the grabbed position.
		// The teleport count is what says a grab happened in the window, whatever the jitter did.
		var h = new Harness();
		int tick = 0;
		h.Clock.OnWait = () =>
		{
			tick++;
			h.Steps++; // resting jitter, every look
			if (tick == 3)
			{
				h.Grabs++;
				h.Cursor.Position = new CursorPoint(700, 20); // the caret grab
			}
		};

		var outcome = await h.Run();

		Assert.Equal(PointerHoldOutcome.TimeUp, outcome);
		Assert.Equal(new[] { Baseline }, h.Cursor.Writes);
		Assert.Empty(h.Followed);
	}

	[Fact]
	public async Task GenuineTravelSharingALookWithAGrab_IsRestoredOnceThenFollowedAgain()
	{
		// The accepted cost of asking about the grab first: a hand whose real travel lands in
		// the same look as a teleport is pulled back once, and its very next step is followed.
		var h = new Harness();
		var handKeptGoingTo = new CursorPoint(160, 160);
		int tick = 0;
		h.Clock.OnWait = () =>
		{
			tick++;
			if (tick == 1)
			{
				h.Steps++;
				h.Grabs++;
				h.Cursor.Position = new CursorPoint(150, 150);
			}
			if (tick == 2)
			{
				h.Steps++;
				h.Cursor.Position = handKeptGoingTo;
			}
		};

		var outcome = await h.Run();

		Assert.Equal(PointerHoldOutcome.TimeUp, outcome);
		Assert.Equal(new[] { Baseline }, h.Cursor.Writes);
		Assert.Equal(new[] { handKeptGoingTo }, h.Followed);
	}

	[Fact]
	public async Task AButton_EndsTheWatchAtOnce()
	{
		var h = new Harness();
		h.Clock.OnWait = () =>
		{
			if (h.Clock.Now >= TimeSpan.FromMilliseconds(120))
				h.Acts++;
		};

		var outcome = await h.Run();

		Assert.Equal(PointerHoldOutcome.UserTookTheMouse, outcome);
		Assert.Equal(3, h.Clock.Waits.Count);
	}

	[Fact]
	public async Task AButtonDuringTheWiggle_StopsBeforeTheNextCorrection()
	{
		// The wiggle that follows a correction takes a while, so the user can easily click while
		// it runs. Asking again straight after it is what stops the watch correcting once more
		// first.
		var h = new Harness();
		h.Clock.OnWait = () => h.Cursor.Position = new CursorPoint(500, 300); // grabbed every tick
		h.OnWiggle = () => { h.Acts++; return Task.FromResult<CursorPoint?>(null); };

		var outcome = await h.Run();

		Assert.Equal(PointerHoldOutcome.UserTookTheMouse, outcome);
		Assert.Single(h.Cursor.Writes);
		Assert.Equal(1, h.Wiggles);
	}

	[Fact]
	public async Task TheWiggleReportsTheHandTookThePointer_TheHoldFollowsItThere()
	{
		// The hand moves while the wiggle runs; the wiggle stops for it and says where. The hold
		// follows to the reported position rather than re-judging the wiggle's whole stretch
		// from the counters, which cannot say who moved the pointer last.
		var h = new Harness();
		var handWentTo = new CursorPoint(300, 300);
		int tick = 0;
		h.Clock.OnWait = () =>
		{
			tick++;
			if (tick == 1)
				h.Cursor.Position = new CursorPoint(500, 300); // a grab; correction follows
		};
		h.OnWiggle = () =>
		{
			h.Steps++; // the hand moved while the wiggle ran, and the wiggle saw it
			h.Cursor.Position = handWentTo;
			return Task.FromResult<CursorPoint?>(handWentTo);
		};

		var outcome = await h.Run();

		Assert.Equal(PointerHoldOutcome.TimeUp, outcome);
		Assert.Single(h.Cursor.Writes); // only the one grab was corrected
		Assert.Equal(new[] { handWentTo }, h.Followed);
	}

	[Fact]
	public async Task ASecondGrabDuringTheWiggle_CannotOutvoteTheHandTheWiggleSaw()
	{
		// The compounding case (issue #384): the wiggle's stretch contains both a second grab
		// and genuine hand travel, in that order or any other. The counters alone cannot say
		// who had the last word — only the wiggle saw the order, and its report wins. Judging
		// by the counters here restored a stale baseline over the hand's chosen position.
		var h = new Harness();
		var handSettledAt = new CursorPoint(320, 240);
		int tick = 0;
		h.Clock.OnWait = () =>
		{
			tick++;
			if (tick == 1)
				h.Cursor.Position = new CursorPoint(500, 300); // the first grab; wiggle follows
		};
		h.OnWiggle = () =>
		{
			h.Grabs++; // a second grab landed during the wiggle and was reclaimed by it
			h.Steps++; // then the hand genuinely took the pointer
			h.Cursor.Position = handSettledAt;
			return Task.FromResult<CursorPoint?>(handSettledAt);
		};

		var outcome = await h.Run();

		Assert.Equal(PointerHoldOutcome.TimeUp, outcome);
		// One write: the first grab's correction. The hand's position was never overwritten.
		Assert.Equal(new[] { Baseline }, h.Cursor.Writes);
		Assert.Equal(new[] { handSettledAt }, h.Followed);
	}

	[Fact]
	public async Task ANewCaptureTakesOver_AndThisWatchStandsDown()
	{
		var h = new Harness();
		h.Clock.OnWait = () =>
		{
			if (h.Clock.Now >= TimeSpan.FromMilliseconds(80))
				h.Current = false;
		};

		var outcome = await h.Run();

		Assert.Equal(PointerHoldOutcome.Superseded, outcome);
	}

	[Fact]
	public async Task ZeroLength_NeverLooksAtThePointerAtAll()
	{
		var h = new Harness();

		var outcome = await h.Run(hold: TimeSpan.Zero);

		Assert.Equal(PointerHoldOutcome.TimeUp, outcome);
		Assert.Empty(h.Clock.Waits);
		Assert.Empty(h.Cursor.Writes);
	}

	[Fact]
	public async Task ACorrectionOnTheLastTick_DoesNotStartAWiggleThatOutlivesTheWatch()
	{
		// The wiggle runs for as long as the user set it to, up to several seconds. Starting one
		// on the last tick would keep the whole apparatus alive well past the length the watch
		// promised. The correction has already been made by then; only the wiggle that would
		// have advertised it is given up.
		var h = new Harness();
		h.Clock.OnWait = () => h.Cursor.Position = new CursorPoint(500, 300);

		var outcome = await h.Run(hold: Poll); // one tick, and it is over

		Assert.Equal(PointerHoldOutcome.TimeUp, outcome);
		Assert.Single(h.Cursor.Writes);
		Assert.Equal(0, h.Wiggles);
	}

	[Fact]
	public async Task AnUnreadablePointer_IsLeftAlone()
	{
		// Not knowing where the pointer is, is not a good enough reason to move it.
		var h = new Harness();
		h.Cursor.Readable = false;
		h.Cursor.Position = new CursorPoint(500, 300);

		var outcome = await h.Run();

		Assert.Equal(PointerHoldOutcome.TimeUp, outcome);
		Assert.Empty(h.Cursor.Writes);
	}

	[Fact]
	public async Task AFailedRestore_DoesNotStartTheWiggle()
	{
		// The wiggle advertises a correction. If the write failed there is nothing to
		// advertise, and wiggling around the grabbed position would advertise the grab.
		var h = new Harness();
		h.Cursor.Writable = false;
		h.Clock.OnWait = () => h.Cursor.Position = new CursorPoint(500, 300);

		var outcome = await h.Run();

		Assert.Equal(PointerHoldOutcome.TimeUp, outcome);
		Assert.Equal(0, h.Wiggles);
	}

	[Fact]
	public async Task NullArgumentsAreRejected()
	{
		var clock = new FakeClock();
		var cursor = new FakeCursor();
		Func<long> zero = () => 0;
		Func<bool> yes = () => true;
		Action<CursorPoint> follow = _ => { };
		Func<Task<CursorPoint?>> nothing = () => Task.FromResult<CursorPoint?>(null);

		await Assert.ThrowsAsync<ArgumentNullException>(() =>
			PointerHold.RunAsync(null!, Baseline, zero, zero, zero, yes, follow, nothing, clock.Delay, clock.Elapsed, Poll, Hold));
		await Assert.ThrowsAsync<ArgumentNullException>(() =>
			PointerHold.RunAsync(cursor, Baseline, null!, zero, zero, yes, follow, nothing, clock.Delay, clock.Elapsed, Poll, Hold));
		await Assert.ThrowsAsync<ArgumentNullException>(() =>
			PointerHold.RunAsync(cursor, Baseline, zero, null!, zero, yes, follow, nothing, clock.Delay, clock.Elapsed, Poll, Hold));
		await Assert.ThrowsAsync<ArgumentNullException>(() =>
			PointerHold.RunAsync(cursor, Baseline, zero, zero, null!, yes, follow, nothing, clock.Delay, clock.Elapsed, Poll, Hold));
		await Assert.ThrowsAsync<ArgumentNullException>(() =>
			PointerHold.RunAsync(cursor, Baseline, zero, zero, zero, null!, follow, nothing, clock.Delay, clock.Elapsed, Poll, Hold));
		await Assert.ThrowsAsync<ArgumentNullException>(() =>
			PointerHold.RunAsync(cursor, Baseline, zero, zero, zero, yes, null!, nothing, clock.Delay, clock.Elapsed, Poll, Hold));
		await Assert.ThrowsAsync<ArgumentNullException>(() =>
			PointerHold.RunAsync(cursor, Baseline, zero, zero, zero, yes, follow, null!, clock.Delay, clock.Elapsed, Poll, Hold));
	}

	[Fact]
	public async Task APollIntervalOfNothingIsRejected()
	{
		// It would spin without ever advancing the clock.
		var clock = new FakeClock();
		var cursor = new FakeCursor();
		Func<long> zero = () => 0;

		await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() =>
			PointerHold.RunAsync(
				cursor, Baseline, zero, zero, zero, () => true, _ => { },
				() => Task.FromResult<CursorPoint?>(null),
				clock.Delay, clock.Elapsed, TimeSpan.Zero, Hold));
	}
}
