using Mutation.Ui.Core;
using Xunit;

namespace Mutation.Tests;

/// <summary>
/// The shape of the pointer wiggle that pulls a magnifier's view back to the mouse after a
/// capture (issue #373). Two properties carry the whole feature: the pointer moves at all, and
/// the pointer finishes on exactly the pixel it started from.
/// </summary>
public class PointerNudgePlannerTests
{
	private static readonly CursorPoint Anchor = new(400, 300);

	private static PointerNudgeOptions On(int intervalMs = 50, int durationMs = 500, int distancePx = 1) =>
		new(true, intervalMs, durationMs, distancePx);

	[Fact]
	public void SwitchedOff_ThePointerIsNeverTouched()
	{
		Assert.Empty(PointerNudgePlanner.Plan(Anchor, PointerNudgeOptions.Off));
	}

	[Fact]
	public void DefaultTimings_GiveOneMovePerIntervalForTheWholeDuration()
	{
		// Half a second at one move every 50 ms is ten moves, and ten is even, so the last of
		// them is already the way home — no eleventh move is needed.
		var plan = PointerNudgePlanner.Plan(Anchor, On());

		Assert.Equal(10, plan.Count);
		Assert.Equal(Anchor, plan[plan.Count - 1]);
	}

	[Fact]
	public void EveryPlanEndsOnTheAnchor()
	{
		// The capture works hard to put the pointer back where the user left it. A wiggle that
		// finished a pixel out would quietly undo that on every capture.
		for (int durationMs = 50; durationMs <= 1000; durationMs += 50)
		{
			var plan = PointerNudgePlanner.Plan(Anchor, On(durationMs: durationMs));
			Assert.NotEmpty(plan);
			Assert.Equal(Anchor, plan[plan.Count - 1]);
		}
	}

	[Fact]
	public void OddNumberOfMoves_GetsOneExtraMoveHome()
	{
		// Three intervals' worth would end on the offset pixel, so a fourth move is added.
		var plan = PointerNudgePlanner.Plan(Anchor, On(intervalMs: 50, durationMs: 150));

		Assert.Equal(4, plan.Count);
		Assert.Equal(Anchor, plan[plan.Count - 1]);
	}

	[Fact]
	public void ThePointerActuallyAlternates()
	{
		var plan = PointerNudgePlanner.Plan(Anchor, On());

		for (int i = 0; i < plan.Count; i++)
			Assert.Equal(i % 2 == 0 ? new CursorPoint(Anchor.X + 1, Anchor.Y) : Anchor, plan[i]);
	}

	[Fact]
	public void MovementIsHorizontalOnly()
	{
		var plan = PointerNudgePlanner.Plan(Anchor, On());

		Assert.All(plan, p => Assert.Equal(Anchor.Y, p.Y));
	}

	[Fact]
	public void DurationShorterThanOneInterval_StillMovesOnce()
	{
		// A setting that is switched on but silently does nothing is the worst answer available,
		// and worst of all for someone who cannot see that nothing happened.
		var plan = PointerNudgePlanner.Plan(Anchor, On(intervalMs: 500, durationMs: 50));

		Assert.Equal(2, plan.Count);
		Assert.Equal(new CursorPoint(Anchor.X + 1, Anchor.Y), plan[0]);
		Assert.Equal(Anchor, plan[1]);
	}

	[Theory]
	// One column either side, same row: the only two places a wiggle around the anchor ever
	// parks the pointer, and so the only displacement it is safe to undo on the wiggle's behalf.
	[InlineData(401, 300, true)]
	[InlineData(399, 300, true)]
	[InlineData(400, 300, false)] // the anchor itself is not a displacement
	[InlineData(402, 300, false)] // too far to be ours
	[InlineData(401, 301, false)] // a wiggle never changes the row
	[InlineData(900, 700, false)] // the user has moved the mouse; leave it alone
	public void OnlyTheWigglesOwnDisplacementIsRecognised(int x, int y, bool expected)
	{
		Assert.Equal(expected, PointerNudgePlanner.IsWiggleDisplacement(Anchor, new CursorPoint(x, y), 1));
	}

	[Theory]
	[InlineData(408, 300, true)]
	[InlineData(392, 300, true)]
	[InlineData(401, 300, false)] // one pixel out is not where an eight-pixel wiggle parks
	[InlineData(400, 300, false)]
	public void AtALargerDistance_TheRecognisedDisplacementMovesWithIt(int x, int y, bool expected)
	{
		Assert.Equal(expected, PointerNudgePlanner.IsWiggleDisplacement(Anchor, new CursorPoint(x, y), 8));
	}

	[Fact]
	public void ALargerDistance_MovesThePointerThatFarAndStillComesHome()
	{
		// The distance is settable because a magnifier that filters small movements as jitter
		// would otherwise need a new build to satisfy.
		var plan = PointerNudgePlanner.Plan(Anchor, On(distancePx: 8));

		Assert.Equal(new CursorPoint(Anchor.X + 8, Anchor.Y), plan[0]);
		Assert.Equal(Anchor, plan[1]);
		Assert.Equal(Anchor, plan[plan.Count - 1]);
	}

	[Theory]
	[InlineData(0, 500)]
	[InlineData(-50, 500)]
	[InlineData(50, 0)]
	[InlineData(50, -500)]
	public void UnusableTimings_LeaveThePointerAlone(int intervalMs, int durationMs)
	{
		Assert.Empty(PointerNudgePlanner.Plan(Anchor, On(intervalMs, durationMs)));
	}
}
