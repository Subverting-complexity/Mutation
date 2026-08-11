namespace Mutation.Ui.Core;

/// <summary>
/// Reads and writes where the mouse pointer is on screen.
/// <para>
/// The seam exists so that <see cref="CursorAnchor"/> — which holds all the rules about when
/// the pointer may be moved — can be tested without a real mouse, and so that a test run does
/// not fling the developer's pointer across the screen.
/// </para>
/// <para>
/// Neither method throws. The pointer is a nicety; a capture that cannot read or write it
/// still has to complete.
/// </para>
/// </summary>
public interface ICursorPosition
{
	/// <summary>
	/// Where the pointer is now. Returns false, with <paramref name="position"/> left at its
	/// default, when the position could not be read.
	/// </summary>
	bool TryGet(out CursorPoint position);

	/// <summary>
	/// Puts the pointer at <paramref name="position"/>. Returns whether the move was accepted.
	/// </summary>
	bool TrySet(CursorPoint position);
}
