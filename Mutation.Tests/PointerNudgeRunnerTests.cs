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
			Writes.Add(position);
			if (!WriteSucceeds)
				return false;

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
			cursor, clock.Delay, Anchor, PlanOf(6), Interval, stillWanted: () => ++checks <= 2);

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
