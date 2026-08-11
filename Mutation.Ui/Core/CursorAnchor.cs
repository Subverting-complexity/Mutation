using System;

namespace Mutation.Ui.Core;

/// <summary>
/// Remembers where the mouse pointer was, and puts it back if something moved it.
///
/// <para>
/// A screen capture opens a full-screen topmost overlay, takes the foreground, and moves
/// keyboard focus onto the overlay; when the selection ends it hides the overlay and hands the
/// foreground back. Every one of those is a focus change, and a magnifier or screen reader that
/// follows focus answers a focus change by moving the pointer. To the person capturing, the
/// pointer jumps: they aimed at a paragraph, pressed the hotkey, and the crosshair came up
/// somewhere else. Mutation's own code never moves the pointer, so without this there is nothing
/// standing between the user and that jump.
/// </para>
///
/// <para>
/// The screenshot is taken before the overlay appears, so the overlay shows the screen exactly
/// as it was. This does the same for the pointer: capture the position before the focus change,
/// restore it after, and the whole capture looks like one still frame.
/// </para>
///
/// <para>
/// The one rule worth stating out loud is that an unmoved pointer is left alone.
/// <see cref="Restore"/> reads the live position first and writes only on a genuine difference,
/// because writing the coordinates the pointer is already at still generates a mouse-move
/// message — enough to cancel a hover, disturb a drag, or wake a window that was quietly idle.
/// Doing nothing has to actually mean doing nothing.
/// </para>
///
/// <para>
/// Not thread-safe, and not meant to be: each capture drives one anchor from its own window.
/// </para>
/// </summary>
public sealed class CursorAnchor
{
	private readonly ICursorPosition _cursor;
	private CursorPoint _anchor;

	public CursorAnchor(ICursorPosition cursor)
	{
		_cursor = cursor ?? throw new ArgumentNullException(nameof(cursor));
	}

	/// <summary>
	/// Whether there is a remembered position to go back to. False before the first successful
	/// <see cref="Capture"/>, and again after <see cref="Clear"/>.
	/// </summary>
	public bool HasAnchor { get; private set; }

	/// <summary>
	/// The remembered position. Meaningless while <see cref="HasAnchor"/> is false.
	/// </summary>
	public CursorPoint Anchor => _anchor;

	/// <summary>
	/// Which anchor this is. Changes on every <see cref="Capture"/> and every
	/// <see cref="Clear"/>, so work that was scheduled against one anchor can tell that a newer
	/// one has replaced it.
	/// <para>
	/// The capture overlay is created once and reused, so one anchor serves every capture the
	/// app ever makes. Some of the restores are deferred — onto a dispatcher turn, or onto a
	/// continuation that waits for the foreground to be handed back — and a slow one could
	/// otherwise still be pending when the next capture starts, and clear or restore against an
	/// anchor that belongs to a capture that finished.
	/// </para>
	/// </summary>
	public int Generation { get; private set; }

	/// <summary>
	/// Remembers where the pointer is now, replacing any position remembered before. Returns
	/// whether a position was read; a failure leaves nothing remembered, so a later
	/// <see cref="Restore"/> does nothing rather than send the pointer to a stale place.
	/// </summary>
	public bool Capture()
	{
		Generation++;
		if (_cursor.TryGet(out var position))
		{
			_anchor = position;
			HasAnchor = true;
			return true;
		}

		_anchor = default;
		HasAnchor = false;
		return false;
	}

	/// <summary>
	/// Puts the pointer back where <see cref="Capture"/> found it, if it has drifted since.
	/// Returns true only when the pointer was actually moved — the caller uses that to decide
	/// whether anything drawn from the old position, such as the crosshair, has to be redrawn.
	/// <para>
	/// Keeps the anchor afterwards, so the same position can be defended across more than one
	/// focus change: a capture restores once when the overlay appears and again on the following
	/// dispatcher turn, because a tool that follows focus reacts after the activation call has
	/// already returned.
	/// </para>
	/// <para>
	/// A position that cannot be read is treated as no drift rather than as a reason to write
	/// blindly. Not knowing where the pointer is, is not a good enough reason to move it.
	/// </para>
	/// </summary>
	public bool Restore()
	{
		if (!HasAnchor)
			return false;

		if (!_cursor.TryGet(out var current))
			return false;

		if (current == _anchor)
			return false;

		return _cursor.TrySet(_anchor);
	}

	/// <summary>
	/// As <see cref="Restore"/>, but does nothing unless <paramref name="generation"/> is still
	/// the current one. Deferred work uses this so a restore scheduled by a capture that has
	/// since finished cannot move the pointer during the next one.
	/// </summary>
	public bool RestoreIfCurrent(int generation) => generation == Generation && Restore();

	/// <summary>
	/// Forgets the remembered position, so nothing can be restored to it later. Called once a
	/// capture is over and the pointer belongs to the user again.
	/// </summary>
	public void Clear()
	{
		Generation++;
		_anchor = default;
		HasAnchor = false;
	}

	/// <summary>
	/// As <see cref="Clear"/>, but does nothing unless <paramref name="generation"/> is still
	/// the current one — so a late tidy-up from a finished capture cannot throw away the anchor
	/// the next capture has just taken.
	/// </summary>
	public void ClearIfCurrent(int generation)
	{
		if (generation == Generation)
			Clear();
	}
}
