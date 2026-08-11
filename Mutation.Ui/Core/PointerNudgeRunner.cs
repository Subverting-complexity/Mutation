using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Mutation.Ui.Core;

/// <summary>
/// Walks a nudge plan, one position per interval, and gets out of the way the moment the pointer
/// stops being ours to move.
///
/// <para>
/// The stand-down rule is the whole reason this is not a plain loop. Before each move it checks
/// that the pointer is still exactly where the previous move left it. If it is not, something
/// else has taken hold of it — the user has picked up the mouse, or the next capture has already
/// started and put the pointer somewhere of its own — and the nudge stops immediately. Half a
/// second of an application dragging the pointer back under the user's hand would be far worse
/// than the problem being solved.
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
	/// before each one. Returns how many were applied, which is the length of the plan on an
	/// uninterrupted run.
	/// </summary>
	/// <param name="cursor">Where the pointer is read and written.</param>
	/// <param name="delay">How to wait one interval.</param>
	/// <param name="anchor">Where the pointer is expected to be found when the run starts — the
	/// position the capture put it back on, and the position it must be left on. Seeding the
	/// expectation from here rather than from a live read is what stops a pointer somebody else
	/// has already taken over from being hauled back to a place it left.</param>
	/// <param name="plan">Positions to move through, from <see cref="PointerNudgePlanner"/>.</param>
	/// <param name="interval">How long to wait before each move.</param>
	/// <param name="stillWanted">Asked before each move. False stops the nudge — used to drop it
	/// when the capture that started it is over. Null means never asked.</param>
	public static async Task<int> RunAsync(
		ICursorPosition cursor,
		Func<TimeSpan, Task> delay,
		CursorPoint anchor,
		IReadOnlyList<CursorPoint> plan,
		TimeSpan interval,
		Func<bool>? stillWanted = null)
	{
		if (cursor is null) throw new ArgumentNullException(nameof(cursor));
		if (delay is null) throw new ArgumentNullException(nameof(delay));
		if (plan is null) throw new ArgumentNullException(nameof(plan));

		if (plan.Count == 0)
			return 0;

		// Where the pointer has to be found before each move, starting from where the capture
		// left it.
		var expected = anchor;
		bool mirrored = false;
		bool directionSettled = false;
		int applied = 0;

		foreach (var position in plan)
		{
			await delay(interval).ConfigureAwait(false);

			if (stillWanted is not null && !stillWanted())
				return Settle(cursor, anchor, expected, applied);

			// The user has the mouse. Leave the pointer exactly as they left it, drift and all —
			// the one exit that must not tidy up after itself.
			if (!cursor.TryGet(out var current) || current != expected)
				return applied;

			var target = mirrored ? Mirror(anchor, position) : position;

			if (!cursor.TrySet(target))
				return Settle(cursor, anchor, expected, applied);

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
						return Settle(cursor, anchor, expected, applied);
				}
			}

			expected = target;
			applied++;
		}

		return applied;
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
