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
/// That check is exact rather than approximate because every position it compares against was
/// written by this loop through the same interface it reads back, so the value returned is the
/// value written. Nothing here has to reason about pixels or tolerances.
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
	/// position the capture put it back on. Seeding the expectation from here rather than from a
	/// live read is what stops a pointer somebody else has already taken over from being hauled
	/// back to a place it left.</param>
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
		int applied = 0;
		foreach (var position in plan)
		{
			await delay(interval).ConfigureAwait(false);

			if (stillWanted is not null && !stillWanted())
				return applied;

			if (!cursor.TryGet(out var current) || current != expected)
				return applied;

			if (!cursor.TrySet(position))
				return applied;

			expected = position;
			applied++;
		}

		return applied;
	}
}
