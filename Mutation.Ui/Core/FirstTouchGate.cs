using System;

namespace Mutation.Ui.Core;

/// <summary>
/// Whether a page has been touched yet, and therefore whether it is allowed to read its own
/// errors out loud.
/// <para>
/// The Hotkeys page builds every row from settings before the user sees it, and a stored row
/// that is empty or unparseable already carries "Enter a hotkey." by the time it reaches the
/// screen. Each of those errors sits in an assertive live region, so opening the page queued
/// one interruption per bad row, about settings nobody had touched. Two of the app's own
/// conventions were already fighting over this: the router row's rewrite notice is committed
/// with <c>announce: false</c> at load precisely so seventeen rows are not read aloud on the
/// way in, while the error beside it had no such gate (issue #350).
/// </para>
/// <para>
/// The gate is per page rather than per row, and that is the point. A row-by-row rule — a row
/// speaks once it has been left once — would silence the case where the user edits a *core*
/// hotkey and a *router* row two sections down becomes a duplicate because of it. That row was
/// never touched, and it is exactly the news the user needs. Once anything on the page has
/// been used, every row on it may speak.
/// </para>
/// <para>
/// It gates the announcement only, never the written text. An error is on screen from the
/// moment it exists, because taking it off screen at load would be a plain regression for a
/// sighted reader — the visible half of this was never broken.
/// </para>
/// </summary>
internal sealed class FirstTouchGate
{
	/// <summary>
	/// True once the user has done something on the page. Errors announce normally from then
	/// on; before it, they are shown in silence.
	/// </summary>
	internal bool HasBeenTouched { get; private set; }

	/// <summary>
	/// Raised the once, when the page goes from untouched to touched, so anything holding a
	/// message it was told not to say can stop holding back. It does not repeat.
	/// </summary>
	internal event EventHandler? Touched;

	/// <summary>
	/// Records that the user has used the page. Idempotent — the second call and every one
	/// after it does nothing at all, which is what lets every handler on the page call it
	/// freely without checking first.
	/// </summary>
	internal void Touch()
	{
		if (HasBeenTouched)
			return;

		HasBeenTouched = true;
		Touched?.Invoke(this, EventArgs.Empty);
	}
}
