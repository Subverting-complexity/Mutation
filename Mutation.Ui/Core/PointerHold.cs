using System;
using System.Threading.Tasks;

namespace Mutation.Ui.Core;

/// <summary>Why a pointer hold finished.</summary>
public enum PointerHoldOutcome
{
	/// <summary>The time was up and the pointer was never taken.</summary>
	TimeUp,

	/// <summary>The user put a hand on the mouse. The pointer is theirs from that instant.</summary>
	UserTookTheMouse,

	/// <summary>A new capture claimed the pointer, so this hold is no longer anybody's business.</summary>
	Superseded
}

/// <summary>
/// Keeps the pointer on the position a capture recorded, for a short while after the capture is
/// over, and gets out of the way the moment the user reaches for the mouse.
///
/// <para>
/// The capture already puts the pointer back twice on its way out, and both attempts land before
/// anything else has reacted to the foreground changing. A magnifier that follows the keyboard
/// caret moves the pointer to it some time after that, so the pointer arrives back where the user
/// left it and is then taken away again — reliably, on every capture that returns to an
/// application with a text cursor in it.
/// </para>
///
/// <para>
/// A watch that simply kept restoring would fight the user, which is why this was not done
/// sooner. What makes it safe is that the two movements can be told apart after all: real mouse
/// input is not flagged as injected, and a program moving the pointer either injects the movement
/// or places the cursor without producing any mouse input at all. So the hold does not guess. It
/// keeps putting the pointer back until it sees a hand on the mouse, and from that instant it
/// never touches the pointer again.
/// </para>
///
/// <para>
/// Bounded all the same. Whatever else is going on, the pointer belongs to the user again once
/// the time is up.
/// </para>
/// </summary>
public static class PointerHold
{
	/// <summary>
	/// Watches until the time is up, the user takes the mouse, or a newer capture takes over.
	/// </summary>
	/// <param name="userHasTheMouse">Whether real mouse input has arrived. Checked before each
	/// correction, and once more before the first, so a hand already on the mouse is never
	/// argued with.</param>
	/// <param name="stillCurrent">Whether the capture this hold belongs to is still the live one.</param>
	/// <param name="restoreIfDrifted">Puts the pointer back if something moved it. Returns
	/// whether it actually had to.</param>
	/// <param name="afterRestore">Run when a correction was needed — the wiggle, which brings a
	/// magnified view back to the pointer, since whatever moved the pointer probably moved the
	/// view as well.</param>
	/// <param name="delay">How to wait one poll interval.</param>
	/// <param name="elapsed">Time since the hold started. Measured rather than counted, because a
	/// busy machine makes a count of intervals mean nothing.</param>
	public static async Task<PointerHoldOutcome> RunAsync(
		Func<bool> userHasTheMouse,
		Func<bool> stillCurrent,
		Func<bool> restoreIfDrifted,
		Func<Task> afterRestore,
		Func<TimeSpan, Task> delay,
		Func<TimeSpan> elapsed,
		TimeSpan pollInterval,
		TimeSpan hold)
	{
		if (userHasTheMouse is null) throw new ArgumentNullException(nameof(userHasTheMouse));
		if (stillCurrent is null) throw new ArgumentNullException(nameof(stillCurrent));
		if (restoreIfDrifted is null) throw new ArgumentNullException(nameof(restoreIfDrifted));
		if (afterRestore is null) throw new ArgumentNullException(nameof(afterRestore));
		if (delay is null) throw new ArgumentNullException(nameof(delay));
		if (elapsed is null) throw new ArgumentNullException(nameof(elapsed));
		if (pollInterval <= TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(pollInterval));

		while (elapsed() < hold)
		{
			await delay(pollInterval).ConfigureAwait(false);

			if (userHasTheMouse())
				return PointerHoldOutcome.UserTookTheMouse;

			if (!stillCurrent())
				return PointerHoldOutcome.Superseded;

			if (!restoreIfDrifted())
				continue;

			// The bound is checked again here because what follows is not instant. The wiggle
			// runs for as long as the user set it to, up to several seconds, and starting one on
			// the last tick of the hold would keep the whole apparatus alive well past the time
			// the user was promised — the anchor undefended by nothing, the hook still installed.
			// The correction has already been made; the wiggle that would have advertised it can
			// be given up.
			if (elapsed() >= hold)
				return PointerHoldOutcome.TimeUp;

			await afterRestore().ConfigureAwait(false);

			// The wiggle moves the pointer on purpose and takes a while doing it, so the user may
			// have reached for the mouse while it ran. Asked again here rather than only at the
			// top of the loop, so the hold stands down at the first opportunity rather than
			// correcting once more first.
			if (userHasTheMouse())
				return PointerHoldOutcome.UserTookTheMouse;
		}

		return PointerHoldOutcome.TimeUp;
	}
}
