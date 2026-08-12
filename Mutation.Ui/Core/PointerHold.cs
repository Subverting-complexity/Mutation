using System;
using System.Threading.Tasks;

namespace Mutation.Ui.Core;

/// <summary>Why a pointer hold finished.</summary>
public enum PointerHoldOutcome
{
	/// <summary>The time was up.</summary>
	TimeUp,

	/// <summary>
	/// The user pressed a button or turned the wheel. The pointer is theirs from that instant.
	/// </summary>
	UserTookTheMouse,

	/// <summary>A new capture claimed the pointer, so this hold is no longer anybody's business.</summary>
	Superseded
}

/// <summary>
/// Keeps the pointer where the capture — or the user's own hand — last put it, for a short while
/// after the capture is over, undoing anything else that moves it.
///
/// <para>
/// The first version of this stood down for good on the first real mouse event, on the theory
/// that real input meant a deliberate hand (issue #379). Measurement killed that theory (issue
/// #382): a hand resting on a high-polling-rate mouse streams genuine hardware events
/// continuously while doing nothing, so the watch stood down within its first tick on every
/// capture and never corrected anything. And a magnifier that moves the pointer through its
/// driver produces events flagged exactly like hardware, so the flags alone could not have
/// carried the weight anyway.
/// </para>
///
/// <para>
/// So the hold no longer asks "has real input arrived?" — it asks what moved the pointer, and
/// answers differently for each mover. Hand steps re-baseline the defended position: the defense
/// follows the hand, so it can never fight it, and a resting hand's jitter just walks the
/// baseline a pixel at a time. A movement with no hand steps behind it — a driver teleport, an
/// injected move, a silent cursor placement — is a grab, and is undone. Only a button or the
/// wheel ends the hold early, because that is a deliberate act however it arrives.
/// </para>
///
/// <para>
/// Bounded all the same. Whatever else is going on, the pointer belongs to the user alone once
/// the time is up.
/// </para>
/// </summary>
public static class PointerHold
{
	/// <summary>
	/// Watches until the time is up, the user presses a button, or a newer capture takes over.
	/// </summary>
	/// <param name="cursor">Where the pointer is read and written.</param>
	/// <param name="baseline">The position being defended to begin with — where the capture left
	/// the pointer. Moves with the user's hand thereafter.</param>
	/// <param name="handActs">Count of buttons and wheel turns seen so far, from the watch.
	/// Monotonic; only changes matter, so a snapshot taken before a wait is compared after it.</param>
	/// <param name="handSteps">Count of hand-sized real movement events seen so far, from the
	/// watch. Monotonic, compared the same way. This is what tells a followed hand from a grab.</param>
	/// <param name="stillCurrent">Whether the capture this hold belongs to is still the live one.</param>
	/// <param name="followedTo">Told each time the baseline moves to follow the hand, so whatever
	/// else aims at the defended position — the wiggle — aims at the new one.</param>
	/// <param name="afterRestore">Run when a grab was undone — the wiggle, which brings a
	/// magnified view back to the pointer, since whatever grabbed the pointer probably took the
	/// view with it.</param>
	/// <param name="delay">How to wait one poll interval.</param>
	/// <param name="elapsed">Time since the hold started. Measured rather than counted, because a
	/// busy machine makes a count of intervals mean nothing.</param>
	public static async Task<PointerHoldOutcome> RunAsync(
		ICursorPosition cursor,
		CursorPoint baseline,
		Func<long> handActs,
		Func<long> handSteps,
		Func<bool> stillCurrent,
		Action<CursorPoint> followedTo,
		Func<Task> afterRestore,
		Func<TimeSpan, Task> delay,
		Func<TimeSpan> elapsed,
		TimeSpan pollInterval,
		TimeSpan hold)
	{
		if (cursor is null) throw new ArgumentNullException(nameof(cursor));
		if (handActs is null) throw new ArgumentNullException(nameof(handActs));
		if (handSteps is null) throw new ArgumentNullException(nameof(handSteps));
		if (stillCurrent is null) throw new ArgumentNullException(nameof(stillCurrent));
		if (followedTo is null) throw new ArgumentNullException(nameof(followedTo));
		if (afterRestore is null) throw new ArgumentNullException(nameof(afterRestore));
		if (delay is null) throw new ArgumentNullException(nameof(delay));
		if (elapsed is null) throw new ArgumentNullException(nameof(elapsed));
		if (pollInterval <= TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(pollInterval));

		long acts = handActs();
		long steps = handSteps();

		while (elapsed() < hold)
		{
			await delay(pollInterval).ConfigureAwait(false);

			if (handActs() != acts)
				return PointerHoldOutcome.UserTookTheMouse;

			if (!stillCurrent())
				return PointerHoldOutcome.Superseded;

			long stepsNow = handSteps();
			bool handMoved = stepsNow != steps;
			steps = stepsNow;

			if (!cursor.TryGet(out var current) || current == baseline)
				continue;

			if (handMoved)
			{
				// The hand went there. The defense follows it — deliberately not asking whether
				// something else moved the pointer in the same interval, because when a hand and
				// a grab land together, deferring to the hand is the mistake that costs one lost
				// correction, and fighting the hand is the mistake this whole class exists to
				// never make.
				baseline = current;
				followedTo(current);
				continue;
			}

			// A grab: the pointer moved and no hand moved it.
			if (!cursor.TrySet(baseline))
				continue;

			// The bound is checked again here because what follows is not instant. The wiggle
			// runs for as long as the user set it to, up to several seconds, and starting one on
			// the last tick of the hold would keep the whole apparatus alive well past the time
			// the user was promised. The correction has already been made; only the wiggle that
			// would have advertised it is given up.
			if (elapsed() >= hold)
				return PointerHoldOutcome.TimeUp;

			await afterRestore().ConfigureAwait(false);

			// The wiggle takes a while, so the user may have clicked during it. Asked again here
			// rather than only at the top of the loop, so the hold stands down at the first
			// opportunity. Hand steps during the wiggle are left to the next tick on purpose:
			// refreshing the snapshot here would swallow them, and the movement they left behind
			// would then read as a grab and be yanked back.
			if (handActs() != acts)
				return PointerHoldOutcome.UserTookTheMouse;
		}

		return PointerHoldOutcome.TimeUp;
	}
}
