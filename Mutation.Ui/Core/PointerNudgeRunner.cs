using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Mutation.Ui.Core;

/// <summary>
/// Whether a running wiggle may carry on, and if not, what to do with the pointer on the way out.
/// </summary>
public enum PointerNudgeVerdict
{
	/// <summary>Carry on to the next move.</summary>
	Continue,

	/// <summary>
	/// Stop, and put the pointer back on the anchor first if the wiggle had left it a pixel out.
	/// For a wiggle that has simply been called off — the capture it belonged to is over — where
	/// the pointer is still the wiggle's own to tidy up.
	/// </summary>
	StopAndSettle,

	/// <summary>
	/// Stop, and leave the pointer exactly where it stands. For a wiggle interrupted by the user
	/// putting the mouse button down: the pointer is under their hand and the rectangle they are
	/// drawing starts from it, so moving it even a pixel would move the edge of their selection.
	/// </summary>
	StopAndLeave
}

/// <summary>
/// What a wiggle run did, and — the part its callers cannot infer — why it ended.
///
/// <para>
/// The report exists because the run can last for seconds, and "did a teleport happen somewhere
/// in it?" tells a caller nothing about who owns the pointer at the end: a grab early in the run
/// and a deliberate hand movement late in it can both be true. The run is the only party that
/// saw the order, so it says the one thing that matters — whether the hand took the pointer,
/// and where it was seen holding it (issue #384).
/// </para>
/// </summary>
/// <param name="Applied">How many planned moves were made.</param>
/// <param name="HandTookItAt">Where the hand was seen holding the pointer when the run stood
/// down for it, or null when the run ended any other way — completed, called off, or unable to
/// read or write the pointer.</param>
public readonly record struct PointerNudgeResult(int Applied, CursorPoint? HandTookItAt);

/// <summary>
/// Walks a nudge plan, one position per interval, and gets out of the way the moment the pointer
/// stops being ours to move.
///
/// <para>
/// The stand-down rule is the whole reason this is not a plain loop. Before each move it checks
/// that the pointer is still where the previous move left it. If it is not, something else has
/// taken hold of it, and what happens next depends on who. A driver teleport — a single move no
/// hand could make — is a grab, and the run reclaims: the next planned move puts the pointer
/// back within a pixel of the anchor. A drift within the resting hand's jitter radius is a hand
/// breathing on the mouse, and the run recentres and carries on. Genuine hand travel beyond
/// that radius stops the run immediately, leaving the pointer exactly where the hand put it —
/// half a second of an application dragging the pointer back under the user's hand would be far
/// worse than the problem being solved. The order matters: the grab is asked about first,
/// because the resting hand's jitter makes "did the hand move?" true in every look, and a grab
/// judged second would end the run at the very moment it is needed (issues #382 and #384).
/// </para>
///
/// <para>
/// A caller with no watch to offer passes nothing, and every foreign move then stops the run, as
/// before — with no way to tell a hand from a grab, stopping is the only safe answer.
/// </para>
///
/// <para>
/// Two things the loop has to get right, and both are about not leaving a mess behind. It checks
/// that the first move really happened, because Windows confines the pointer to the union of the
/// monitors rather than to the rectangle around them: a move into empty space beside a monitor is
/// accepted, quietly clamped away, and shows a magnifier no movement at all. When that happens
/// the run mirrors the rest of the plan and goes the other way instead. And when the run gives up
/// part-way through, on any path other than the user taking the mouse, it puts the pointer back
/// on the anchor before it leaves — a nudge that stopped on an odd step would otherwise leave the
/// pointer a pixel out, and the next capture would anchor on the drift and keep it.
/// </para>
///
/// <para>
/// The delay is injected, so the whole sequence can be tested in no time at all rather than in
/// the half second it takes on a real screen — the same arrangement <see cref="ContentReadyGate"/>
/// uses.
/// </para>
/// </summary>
public static class PointerNudgeRunner
{
	/// <summary>
	/// Applies each position in <paramref name="plan"/>, waiting <paramref name="interval"/>
	/// before each one. Reports how many were applied — the length of the plan on an
	/// uninterrupted run — and whether the run ended because the hand took the pointer, which is
	/// the one fact about a run's ending its callers cannot reconstruct afterwards.
	/// </summary>
	/// <param name="cursor">Where the pointer is read and written.</param>
	/// <param name="delay">How to wait one interval.</param>
	/// <param name="anchor">Where the pointer is expected to be found when the run starts — the
	/// position the capture put it back on, and the position it must be left on. Seeding the
	/// expectation from here rather than from a live read is what stops a pointer somebody else
	/// has already taken over from being hauled back to a place it left.</param>
	/// <param name="plan">Positions to move through, from <see cref="PointerNudgePlanner"/>.</param>
	/// <param name="interval">How long to wait before each move.</param>
	/// <param name="verdict">Asked before each move, and obeyed. Null means never asked, which
	/// is the same as always continuing.</param>
	/// <param name="handSteps">Count of hand-sized real movement events, from the watch.
	/// Monotonic; the run compares it across each look to decide whether a foreign move was the
	/// hand or a grab. Null means there is no watch, and every foreign move then ends the run.</param>
	/// <param name="teleports">Count of single-event moves too large for a hand — a driver
	/// grabbing the pointer. Checked before the hand steps, because a resting hand's jitter
	/// advances the step count in every look, and a grab landing in the same look would
	/// otherwise read as the hand and end the run exactly when it is needed (issue #384).</param>
	public static async Task<PointerNudgeResult> RunAsync(
		ICursorPosition cursor,
		Func<TimeSpan, Task> delay,
		CursorPoint anchor,
		IReadOnlyList<CursorPoint> plan,
		TimeSpan interval,
		Func<PointerNudgeVerdict>? verdict = null,
		Func<long>? handSteps = null,
		Func<long>? teleports = null)
	{
		if (cursor is null) throw new ArgumentNullException(nameof(cursor));
		if (delay is null) throw new ArgumentNullException(nameof(delay));
		if (plan is null) throw new ArgumentNullException(nameof(plan));

		if (plan.Count == 0)
			return new PointerNudgeResult(0, null);

		// Where the pointer has to be found before each move, starting from where the capture
		// left it.
		var expected = anchor;
		bool mirrored = false;
		bool directionSettled = false;
		int applied = 0;
		long steps = handSteps?.Invoke() ?? 0;
		long grabs = teleports?.Invoke() ?? 0;

		foreach (var position in plan)
		{
			await delay(interval).ConfigureAwait(false);

			switch (verdict?.Invoke() ?? PointerNudgeVerdict.Continue)
			{
				case PointerNudgeVerdict.StopAndSettle:
					return new PointerNudgeResult(Settle(cursor, anchor, expected, applied), null);
				case PointerNudgeVerdict.StopAndLeave:
					return new PointerNudgeResult(applied, null);
			}

			long stepsNow = handSteps?.Invoke() ?? 0;
			bool handMoved = stepsNow != steps;
			steps = stepsNow;

			long grabsNow = teleports?.Invoke() ?? 0;
			bool grabbed = grabsNow != grabs;
			grabs = grabsNow;

			if (!cursor.TryGet(out var current))
				return new PointerNudgeResult(applied, null);

			if (current != expected)
			{
				// No watch: no way to tell a hand from a grab, so any foreign move ends the run
				// — the only safe answer. Reported as nobody's, because nobody can say.
				if (handSteps is null)
					return new PointerNudgeResult(applied, null);

				// A grab landed in this look. Whatever the jitter also did, reclaim: the write
				// below puts the pointer back on this move's planned position, a pixel from the
				// anchor. Without this the magnifier's caret grab ended the run every time,
				// because the resting hand's jitter made "did the hand move?" true in the same
				// look (issue #384).
				if (!grabbed)
				{
					// The hand walked the pointer somewhere. Leave it exactly where it was
					// found, drift and all — the one exit that must not tidy up after itself —
					// and say so in the report, with the position the hand was seen holding.
					// The report is what lets a caller whose window spans this whole run trust
					// the hand over a teleport that landed earlier in it (issue #384).
					bool genuineTravel = handMoved
						&& (Math.Abs(current.X - expected.X) > RealMouseInput.RestingHandJitterRadiusPixels
							|| Math.Abs(current.Y - expected.Y) > RealMouseInput.RestingHandJitterRadiusPixels);
					if (genuineTravel)
						return new PointerNudgeResult(applied, current);

					// Within the jitter radius: a resting hand breathing on the mouse. The write
					// below recentres, and the run carries on. A moved pointer with no hand steps
					// at all is a silent grab and falls through to be reclaimed too.
				}
			}

			var target = mirrored ? Mirror(anchor, position) : position;

			if (!cursor.TrySet(target))
				return new PointerNudgeResult(Settle(cursor, anchor, expected, applied), null);

			if (!directionSettled && target != anchor)
			{
				directionSettled = true;
				if (!TookEffect(cursor, target))
				{
					// Nothing that way — the pointer is against a monitor edge with empty space
					// beyond it, so the move was accepted and clamped away. Go the other way for
					// the rest of the run.
					mirrored = true;
					target = Mirror(anchor, position);
					if (!cursor.TrySet(target) || !TookEffect(cursor, target))
						return new PointerNudgeResult(Settle(cursor, anchor, expected, applied), null);
				}
			}

			expected = target;
			applied++;
		}

		return new PointerNudgeResult(applied, null);
	}

	private static CursorPoint Mirror(CursorPoint anchor, CursorPoint position) =>
		new(anchor.X - (position.X - anchor.X), position.Y);

	/// <summary>
	/// Whether the pointer actually arrived where it was sent. A position that cannot be read
	/// counts as arrived: refusing to believe an accepted move on the strength of a failed read
	/// would abandon the nudge on every machine where reading the pointer is the flaky part.
	/// </summary>
	private static bool TookEffect(ICursorPosition cursor, CursorPoint target) =>
		!cursor.TryGet(out var landed) || landed == target;

	/// <summary>
	/// Leaves the pointer on the anchor when the run gives up part-way through and the pointer is
	/// still the one the run has been moving. Without this, stopping on an odd step would leave
	/// the pointer a pixel off, the next capture would anchor on that, and the drift would stay —
	/// which is the whole thing the plan's extra move home exists to prevent.
	/// </summary>
	private static int Settle(ICursorPosition cursor, CursorPoint anchor, CursorPoint expected, int applied)
	{
		if (expected == anchor)
			return applied;

		if (cursor.TryGet(out var current) && current != expected)
			return applied;

		cursor.TrySet(anchor);
		return applied;
	}
}
