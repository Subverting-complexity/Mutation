using System;
using System.Collections.Generic;

namespace Mutation.Ui.Core;

/// <summary>
/// Works out the exact sequence of positions a pointer nudge will visit.
///
/// <para>
/// Kept apart from the running of it so the shape of the movement can be checked without
/// waiting half a second per test, and so the two properties that matter are stated in one
/// place: the pointer moves, and the pointer ends up exactly where it started. The second is not
/// a nicety. The capture goes to some length to put the pointer back where the user left it, and
/// a nudge that finished one pixel off would quietly undo that every single time.
/// </para>
/// </summary>
public static class PointerNudgePlanner
{
	/// <summary>
	/// The positions to move through, in order, one per interval. The last one is always the
	/// anchor itself.
	/// <para>
	/// Empty only when the nudge is off, or when the numbers are not usable at all — a
	/// non-positive interval or duration. An empty plan means the pointer is never touched.
	/// </para>
	/// </summary>
	/// <param name="anchor">Where the pointer is, and where it must finish.</param>
	/// <param name="rightmostX">The last usable pixel column on the virtual screen. A pointer
	/// already there cannot move further right — Windows clamps to the virtual screen, so the
	/// move would be accepted and do nothing, and the magnifier would never see any movement.
	/// The nudge goes left instead.</param>
	public static IReadOnlyList<CursorPoint> Plan(CursorPoint anchor, int rightmostX, PointerNudgeOptions options)
	{
		if (!options.Enabled)
			return Array.Empty<CursorPoint>();

		if (options.IntervalMilliseconds <= 0 || options.DurationMilliseconds <= 0)
			return Array.Empty<CursorPoint>();

		// At least one move whenever the nudge is switched on. A duration shorter than a single
		// interval would otherwise divide down to nothing, and a setting that is on but silently
		// does nothing is the worst answer available — worst of all for someone who cannot see
		// that nothing happened.
		int moves = Math.Max(1, options.DurationMilliseconds / options.IntervalMilliseconds);

		int step = anchor.X >= rightmostX ? -1 : 1;
		var away = new CursorPoint(anchor.X + step, anchor.Y);

		var plan = new List<CursorPoint>(moves + 1);
		for (int i = 0; i < moves; i++)
			plan.Add(i % 2 == 0 ? away : anchor);

		// An odd number of moves ends on the offset position, so the pointer needs one more move
		// home. That extra move costs one more interval and is worth it: finishing a pixel out
		// is exactly the drift the capture spends its effort preventing.
		if (plan[plan.Count - 1] != anchor)
			plan.Add(anchor);

		return plan;
	}
}
