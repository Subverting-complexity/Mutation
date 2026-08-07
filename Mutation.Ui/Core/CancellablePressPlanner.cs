namespace Mutation.Ui.Core;

/// <summary>
/// What one press of a control that both starts and stops an operation means.
/// </summary>
public enum CancellablePressAction
{
	/// <summary>Nothing in flight: run it.</summary>
	Start,

	/// <summary>Something is in flight: ask it to give up.</summary>
	Cancel,

	/// <summary>A stop has already been asked for and it is still winding down.</summary>
	AlreadyStopping,
}

/// <summary>
/// The three-way decision behind every control that doubles as its own stop button:
/// the prompt library's Run, the prompt editor's Test Run, and the record buttons.
/// Pure, so the sequence a user actually presses can be asserted without a window.
/// </summary>
/// <remarks>
/// The third case is the one that keeps getting missed. Cancelling is not instant — the
/// call unwinds when the request it is waiting on lets go, which can be seconds — and a
/// user who hears nothing in that gap presses again. Re-running the cancel branch replayed
/// the beep and the request line, which is the complaint issue #299 was filed about;
/// ignoring the press left an enabled control answering with silence, which issue #227
/// settled reads as a control that did not register. It has to be a third answer.
/// </remarks>
public static class CancellablePressPlanner
{
	public static CancellablePressAction For(bool running, bool cancelRequested)
	{
		if (!running)
			return CancellablePressAction.Start;

		return cancelRequested
			? CancellablePressAction.AlreadyStopping
			: CancellablePressAction.Cancel;
	}
}
