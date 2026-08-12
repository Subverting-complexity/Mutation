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
/// Thread-safe, because it has to be. The capture's own steps run on the UI thread, but the last
/// one — putting the pointer back after the foreground has been handed over, and the optional
/// wiggle that may follow it for seconds afterwards — runs off it. Without the lock,
/// <see cref="ClearIfCurrent"/> could read a matching generation, be overtaken by the next
/// capture's <see cref="Capture"/> on the UI thread, and then wipe the anchor that capture had
/// just taken, leaving it with no pointer defence at all. The lock is uncontended in practice:
/// one capture at a time, a handful of calls each.
/// </para>
/// </summary>
public sealed class CursorAnchor
{
	private readonly ICursorPosition _cursor;
	private readonly object _gate = new();
	private CursorPoint _anchor;
	private bool _hasAnchor;
	private int _generation;

	public CursorAnchor(ICursorPosition cursor)
	{
		_cursor = cursor ?? throw new ArgumentNullException(nameof(cursor));
	}

	/// <summary>
	/// Whether there is a remembered position to go back to. False before the first successful
	/// <see cref="Capture"/>, and again after <see cref="Clear"/>.
	/// </summary>
	public bool HasAnchor
	{
		get { lock (_gate) return _hasAnchor; }
	}

	/// <summary>
	/// The remembered position. Meaningless while <see cref="HasAnchor"/> is false.
	/// </summary>
	public CursorPoint Anchor
	{
		get { lock (_gate) return _anchor; }
	}

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
	public int Generation
	{
		get { lock (_gate) return _generation; }
	}

	/// <summary>
	/// Remembers where the pointer is now, replacing any position remembered before. Returns
	/// whether a position was read; a failure leaves nothing remembered, so a later
	/// <see cref="Restore"/> does nothing rather than send the pointer to a stale place.
	/// </summary>
	public bool Capture()
	{
		bool read = _cursor.TryGet(out var position);
		lock (_gate)
		{
			_generation++;
			_anchor = read ? position : default;
			_hasAnchor = read;
		}
		return read;
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
	public bool Restore() => Restore(null);

	/// <summary>
	/// As <see cref="Restore"/>, but does nothing unless <paramref name="generation"/> is still
	/// the current one. Deferred work uses this so a restore scheduled by a capture that has
	/// since finished cannot move the pointer during the next one.
	/// </summary>
	public bool RestoreIfCurrent(int generation) => Restore(generation);

	// The generation is checked under the same lock that reads the anchor, so a caller can never
	// act on a generation that agreed a moment ago and an anchor that has since been replaced.
	private bool Restore(int? requiredGeneration)
	{
		CursorPoint target;
		lock (_gate)
		{
			if (!_hasAnchor)
				return false;

			if (requiredGeneration is int required && required != _generation)
				return false;

			target = _anchor;
		}

		if (!_cursor.TryGet(out var current))
			return false;

		if (current == target)
			return false;

		return _cursor.TrySet(target);
	}

	/// <summary>
	/// Moves the remembered position without starting a new generation, so work already scheduled
	/// against this anchor keeps running and now defends the new place. The hold uses this when
	/// the user's own hand moves the pointer during the defended window: the defense follows the
	/// hand rather than fighting it (issue #382), and everything else aimed at the anchor — the
	/// wiggle, the settle — aims at where the hand actually is.
	/// </summary>
	public void RebaseIfCurrent(int generation, CursorPoint position)
	{
		lock (_gate)
		{
			if (generation != _generation || !_hasAnchor)
				return;

			_anchor = position;
		}
	}

	/// <summary>
	/// Forgets the remembered position, so nothing can be restored to it later. Called once a
	/// capture is over and the pointer belongs to the user again.
	/// </summary>
	public void Clear()
	{
		lock (_gate)
			ClearLocked();
	}

	/// <summary>
	/// As <see cref="Clear"/>, but does nothing unless <paramref name="generation"/> is still
	/// the current one — so a late tidy-up from a finished capture cannot throw away the anchor
	/// the next capture has just taken.
	/// </summary>
	public void ClearIfCurrent(int generation)
	{
		lock (_gate)
		{
			if (generation == _generation)
				ClearLocked();
		}
	}

	private void ClearLocked()
	{
		_generation++;
		_anchor = default;
		_hasAnchor = false;
	}
}
