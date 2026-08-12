using Mutation.Ui.Core;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Xunit;

namespace Mutation.Tests;

/// <summary>
/// How the pointer wiggle behaves while it runs, and — the part that matters — when it gives up
/// (issue #373). A wiggle that carried on regardless would spend half a second dragging the
/// pointer back out from under a user who had picked up the mouse.
/// </summary>
public class PointerNudgeRunnerTests
{
	private static readonly CursorPoint Anchor = new(400, 300);
	private static readonly CursorPoint Away = new(401, 300);
	private static readonly TimeSpan Interval = TimeSpan.FromMilliseconds(50);

	/// <summary>
	/// A pointer that only exists in the test. Writing to <see cref="Position"/> from the
	/// clock's callback is how a test plays whatever else might grab the pointer part-way
	/// through the run.
	/// </summary>
	private sealed class FakeCursor : ICursorPosition
	{
		public CursorPoint Position = Anchor;
		public bool CanRead = true;
		public bool WriteSucceeds = true;
		public int? FailWriteAtIndex;

		/// <summary>
		/// Columns the pointer is allowed to occupy. A write outside them is accepted and then
		/// quietly ignored, which is what Windows does with a move into the empty space beside a
		/// monitor: the pointer is confined to the union of the monitors, not to the rectangle
		/// drawn around them.
		/// </summary>
		public int? ClampMinX;
		public int? ClampMaxX;

		public List<CursorPoint> Writes { get; } = new();

		public bool TryGet(out CursorPoint position)
		{
			if (!CanRead)
			{
				position = default;
				return false;
			}

			position = Position;
			return true;
		}

		public bool TrySet(CursorPoint position)
		{
			int index = Writes.Count;
			Writes.Add(position);
			if (!WriteSucceeds || FailWriteAtIndex == index)
				return false;

			bool clamped = (ClampMaxX is int max && position.X > max)
				|| (ClampMinX is int min && position.X < min);
			if (!clamped)
				Position = position;

			return true;
		}
	}

	// Runs the whole sequence with no waiting at all, and records what was asked for so a test
	// can check the pacing without spending the half second it takes on a real screen.
	private sealed class FakeClock
	{
		public List<TimeSpan> Waits { get; } = new();
		public Action? OnWait;

		public Func<TimeSpan, Task> Delay => interval =>
		{
			Waits.Add(interval);
			OnWait?.Invoke();
			return Task.CompletedTask;
		};
	}

	private static IReadOnlyList<CursorPoint> PlanOf(int moves)
	{
		var plan = new List<CursorPoint>(moves);
		for (int i = 0; i < moves; i++)
			plan.Add(i % 2 == 0 ? Away : Anchor);
		return plan;
	}

	[Fact]
	public async Task UninterruptedRun_AppliesEveryMoveAndWaitsBeforeEachOne()
	{
		var cursor = new FakeCursor();
		var clock = new FakeClock();

		int applied = await PointerNudgeRunner.RunAsync(cursor, clock.Delay, Anchor, PlanOf(4), Interval);

		Assert.Equal(4, applied);
		Assert.Equal(new[] { Away, Anchor, Away, Anchor }, cursor.Writes);
		Assert.Equal(4, clock.Waits.Count);
		Assert.All(clock.Waits, w => Assert.Equal(Interval, w));
		Assert.Equal(Anchor, cursor.Position);
	}

	[Fact]
	public async Task SomethingElseMovesThePointer_TheWiggleGetsOutOfTheWay()
	{
		// The user has picked up the mouse. Half a second of an application hauling the pointer
		// back would be far worse than the magnifier looking at the wrong place.
		var cursor = new FakeCursor();
		var clock = new FakeClock();
		int waits = 0;
		clock.OnWait = () =>
		{
			if (++waits == 3)
				cursor.Position = new CursorPoint(900, 700);
		};

		int applied = await PointerNudgeRunner.RunAsync(cursor, clock.Delay, Anchor, PlanOf(6), Interval);

		Assert.Equal(2, applied);
		Assert.Equal(new CursorPoint(900, 700), cursor.Position);
	}

	[Fact]
	public async Task PointerAlreadyElsewhereBeforeTheFirstMove_NothingHappens()
	{
		// The run expects to find the pointer where the capture left it. Finding it somewhere
		// else means somebody got there first, and wiggling the plan's positions would haul the
		// pointer back to a place it had already left — the very jump this all exists to stop.
		var cursor = new FakeCursor { Position = new CursorPoint(900, 700) };
		var clock = new FakeClock();

		int applied = await PointerNudgeRunner.RunAsync(cursor, clock.Delay, Anchor, PlanOf(4), Interval);

		Assert.Equal(0, applied);
		Assert.Empty(cursor.Writes);
	}

	[Fact]
	public async Task NoLongerWanted_StopsBeforeTheNextMove()
	{
		// What drops the wiggle when the next capture has already claimed the pointer.
		var cursor = new FakeCursor();
		var clock = new FakeClock();
		int checks = 0;

		int applied = await PointerNudgeRunner.RunAsync(
			cursor, clock.Delay, Anchor, PlanOf(6), Interval, verdict: () => ++checks <= 2 ? PointerNudgeVerdict.Continue : PointerNudgeVerdict.StopAndSettle);

		Assert.Equal(2, applied);
	}

	[Fact]
	public async Task EmptyPlan_DoesNotEvenReadThePointer()
	{
		var cursor = new FakeCursor { CanRead = false };
		var clock = new FakeClock();

		int applied = await PointerNudgeRunner.RunAsync(cursor, clock.Delay, Anchor, Array.Empty<CursorPoint>(), Interval);

		Assert.Equal(0, applied);
		Assert.Empty(clock.Waits);
	}

	[Fact]
	public async Task PointerThatCannotBeRead_IsNotWiggledBlindly()
	{
		var cursor = new FakeCursor { CanRead = false };
		var clock = new FakeClock();

		int applied = await PointerNudgeRunner.RunAsync(cursor, clock.Delay, Anchor, PlanOf(4), Interval);

		Assert.Equal(0, applied);
		Assert.Empty(cursor.Writes);
	}

	[Fact]
	public async Task RefusedMove_EndsTheWiggleRatherThanCarryingOnBlind()
	{
		// After a refused write the pointer is no longer where the run believes it is, so every
		// later check would compare against a lie.
		var cursor = new FakeCursor { WriteSucceeds = false };
		var clock = new FakeClock();

		int applied = await PointerNudgeRunner.RunAsync(cursor, clock.Delay, Anchor, PlanOf(6), Interval);

		Assert.Equal(0, applied);
		Assert.Single(cursor.Writes);
	}

	[Fact]
	public async Task MoveClampedAtAMonitorEdge_TheWiggleGoesTheOtherWayInstead()
	{
		// The pointer is at the right edge of a monitor with empty space beside it. Windows
		// accepts the move and clamps it away, so the magnifier sees nothing at all — the same
		// silent failure the planner's old right-edge rule missed, one monitor edge inwards.
		var cursor = new FakeCursor { ClampMaxX = Anchor.X };
		var clock = new FakeClock();

		int applied = await PointerNudgeRunner.RunAsync(cursor, clock.Delay, Anchor, PlanOf(4), Interval);

		Assert.Equal(4, applied);
		Assert.Equal(new CursorPoint(Anchor.X + 1, Anchor.Y), cursor.Writes[0]);
		Assert.Equal(new CursorPoint(Anchor.X - 1, Anchor.Y), cursor.Writes[1]);
		Assert.Equal(Anchor, cursor.Position);
	}

	[Fact]
	public async Task PointerBoxedInOnBothSides_TheRunGivesUpAndLeavesItOnTheAnchor()
	{
		var cursor = new FakeCursor { ClampMinX = Anchor.X, ClampMaxX = Anchor.X };
		var clock = new FakeClock();

		int applied = await PointerNudgeRunner.RunAsync(cursor, clock.Delay, Anchor, PlanOf(4), Interval);

		Assert.Equal(0, applied);
		Assert.Equal(Anchor, cursor.Position);
	}

	[Fact]
	public async Task StoppedOnAnOddStep_ThePointerIsPutBackOnTheAnchor()
	{
		// The next capture claims the pointer part-way through. Stopping where it stood would
		// leave the pointer a pixel out, and the next capture would anchor on that drift and keep
		// it — undoing the whole point of putting the pointer back at all.
		var cursor = new FakeCursor();
		var clock = new FakeClock();
		int checks = 0;

		int applied = await PointerNudgeRunner.RunAsync(
			cursor, clock.Delay, Anchor, PlanOf(6), Interval, verdict: () => ++checks <= 1 ? PointerNudgeVerdict.Continue : PointerNudgeVerdict.StopAndSettle);

		Assert.Equal(1, applied);
		Assert.Equal(Anchor, cursor.Position);
	}

	[Fact]
	public async Task RefusedMoveOnAnOddStep_StillPutsThePointerBack()
	{
		var cursor = new FakeCursor { FailWriteAtIndex = 1 };
		var clock = new FakeClock();

		int applied = await PointerNudgeRunner.RunAsync(cursor, clock.Delay, Anchor, PlanOf(6), Interval);

		Assert.Equal(1, applied);
		Assert.Equal(Anchor, cursor.Position);
	}

	[Fact]
	public async Task UserTookTheMouse_ThePointerIsLeftExactlyWhereTheyPutIt()
	{
		// The one exit that must not tidy up after itself. The pointer is theirs now, drift and
		// all, and hauling it to the anchor would be the jump this feature exists to avoid.
		var elsewhere = new CursorPoint(900, 700);
		var cursor = new FakeCursor();
		var clock = new FakeClock();
		int waits = 0;
		clock.OnWait = () =>
		{
			// After one move, so the run believes the pointer is on the offset pixel.
			if (++waits == 2)
				cursor.Position = elsewhere;
		};

		await PointerNudgeRunner.RunAsync(cursor, clock.Delay, Anchor, PlanOf(6), Interval);

		Assert.Equal(elsewhere, cursor.Position);
		Assert.DoesNotContain(Anchor, cursor.Writes);
	}

	[Fact]
	public async Task DragStarted_TheWiggleStopsWhereItStandsRatherThanTidyingUp()
	{
		// The user has put the mouse button down while the wiggle had the pointer a pixel out.
		// The rectangle they are drawing starts from where the pointer is, so putting it back on
		// the anchor now would move the edge of their selection just as they pressed.
		var cursor = new FakeCursor();
		var clock = new FakeClock();
		int checks = 0;

		int applied = await PointerNudgeRunner.RunAsync(
			cursor, clock.Delay, Anchor, PlanOf(6), Interval,
			verdict: () => ++checks <= 1 ? PointerNudgeVerdict.Continue : PointerNudgeVerdict.StopAndLeave);

		Assert.Equal(1, applied);
		Assert.Equal(Away, cursor.Position);
		Assert.DoesNotContain(Anchor, cursor.Writes);
	}

	[Fact]
	public async Task NullArguments_AreRejected()
	{
		var cursor = new FakeCursor();
		var clock = new FakeClock();

		await Assert.ThrowsAsync<ArgumentNullException>(() =>
			PointerNudgeRunner.RunAsync(null!, clock.Delay, Anchor, PlanOf(2), Interval));
		await Assert.ThrowsAsync<ArgumentNullException>(() =>
			PointerNudgeRunner.RunAsync(cursor, null!, Anchor, PlanOf(2), Interval));
		await Assert.ThrowsAsync<ArgumentNullException>(() =>
			PointerNudgeRunner.RunAsync(cursor, clock.Delay, Anchor, null!, Interval));
	}
}
