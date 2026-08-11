namespace Mutation.Ui.Core;

/// <summary>
/// What a deferred pointer restore is allowed to do, given what the user has already started.
/// </summary>
public enum DeferredCursorRestore
{
	/// <summary>
	/// Do nothing at all. The pointer is the user's drawing hand and moving it would redraw
	/// their rectangle from under them.
	/// </summary>
	StandDown,

	/// <summary>
	/// Put the pointer back, but leave the half-built selection exactly as it is.
	/// </summary>
	MoveOnly,

	/// <summary>
	/// Put the pointer back, and seed the crosshair and the keyboard caret from where it now
	/// is. Safe only when no selection has been started, because seeding restarts one.
	/// </summary>
	MoveAndReseed
}

/// <summary>
/// Decides how far a deferred pointer restore may go.
///
/// <para>
/// The capture overlay puts the pointer back on the dispatcher turn after it opens, because a
/// magnifier or reader that follows focus moves the pointer from its own message loop, after
/// the activation call has already returned. By the time that turn runs, though, the user may
/// have started choosing a region — and the same restore also re-seeds the crosshair and the
/// keyboard caret, which means restarting the selection from scratch.
/// </para>
///
/// <para>
/// The distinction this makes is between moving the pointer and restarting the selection. Those
/// were once the same step, and a second hotkey press on a capture already in progress — which
/// brings the overlay forward again, and so runs a restore again — silently threw away a pinned
/// keyboard corner and said nothing about it. Someone who cannot see the overlay then pressed
/// Enter expecting a capture and pinned a fresh corner instead.
/// </para>
///
/// <para>
/// Putting the pointer back is still right while a keyboard selection is under way: a pointer
/// move syncs the caret but never touches the pinned corner, so restoring the pointer restores
/// the caret with it. It is only the re-seed that has to stand down.
/// </para>
/// </summary>
public static class CursorRestorePolicy
{
	/// <param name="dragInFlight">Whether a mouse button is down and a rectangle is being
	/// dragged out.</param>
	/// <param name="keyboardCornerPinned">Whether the keyboard path has pinned one corner and
	/// is waiting for the second.</param>
	public static DeferredCursorRestore Decide(bool dragInFlight, bool keyboardCornerPinned)
	{
		if (dragInFlight)
			return DeferredCursorRestore.StandDown;

		if (keyboardCornerPinned)
			return DeferredCursorRestore.MoveOnly;

		return DeferredCursorRestore.MoveAndReseed;
	}
}
