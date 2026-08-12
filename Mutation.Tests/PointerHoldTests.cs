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
		public List<CursorPoint> Followed { get; } = new();
		public int Wiggles;
		public Func<Task>? OnWiggle;
		public bool Current = true;

		public Task<PointerHoldOutcome> Run(TimeSpan? hold = null) =>
			PointerHold.RunAsync(
				Cursor,
				Baseline,
				() => Acts,
				() => Steps,
				() => Current,
				Followed.Add,
				() => { Wiggles++; return OnWiggle?.Invoke() ?? Task.CompletedTask; },
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
		h.OnWiggle = () => { h.Acts++; return Task.CompletedTask; };

		var outcome = await h.Run();

		Assert.Equal(PointerHoldOutcome.UserTookTheMouse, outcome);
		Assert.Single(h.Cursor.Writes);
		Assert.Equal(1, h.Wiggles);
	}

	[Fact]
	public async Task HandStepsDuringTheWiggle_AreFollowedOnTheNextTick_NotYankedBack()
	{
		// The hand moves while the wiggle runs. Those steps must not be swallowed by the wiggle:
		// the next tick has to read them as the hand and follow, or the movement they left
		// behind would be undone as though it were a grab.
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
			h.Steps++; // the hand moved while the wiggle ran
			h.Cursor.Position = handWentTo;
			return Task.CompletedTask;
		};

		var outcome = await h.Run();

		Assert.Equal(PointerHoldOutcome.TimeUp, outcome);
		Assert.Single(h.Cursor.Writes); // only the one grab was corrected
		Assert.Equal(new[] { handWentTo }, h.Followed);
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
		Func<Task> nothing = () => Task.CompletedTask;

		await Assert.ThrowsAsync<ArgumentNullException>(() =>
			PointerHold.RunAsync(null!, Baseline, zero, zero, yes, follow, nothing, clock.Delay, clock.Elapsed, Poll, Hold));
		await Assert.ThrowsAsync<ArgumentNullException>(() =>
			PointerHold.RunAsync(cursor, Baseline, null!, zero, yes, follow, nothing, clock.Delay, clock.Elapsed, Poll, Hold));
		await Assert.ThrowsAsync<ArgumentNullException>(() =>
			PointerHold.RunAsync(cursor, Baseline, zero, null!, yes, follow, nothing, clock.Delay, clock.Elapsed, Poll, Hold));
		await Assert.ThrowsAsync<ArgumentNullException>(() =>
			PointerHold.RunAsync(cursor, Baseline, zero, zero, null!, follow, nothing, clock.Delay, clock.Elapsed, Poll, Hold));
		await Assert.ThrowsAsync<ArgumentNullException>(() =>
			PointerHold.RunAsync(cursor, Baseline, zero, zero, yes, null!, nothing, clock.Delay, clock.Elapsed, Poll, Hold));
		await Assert.ThrowsAsync<ArgumentNullException>(() =>
			PointerHold.RunAsync(cursor, Baseline, zero, zero, yes, follow, null!, clock.Delay, clock.Elapsed, Poll, Hold));
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
				cursor, Baseline, zero, zero, () => true, _ => { }, () => Task.CompletedTask,
				clock.Delay, clock.Elapsed, TimeSpan.Zero, Hold));
	}
}
