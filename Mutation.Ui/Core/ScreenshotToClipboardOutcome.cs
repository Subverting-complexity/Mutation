namespace Mutation.Ui.Core;

/// <summary>
/// What became of a plain screenshot capture — the one whose only product is a picture on the
/// clipboard.
/// <para>
/// Three answers, not two, because a busy clipboard is neither of the other two. It used to
/// reach the caller as a thrown exception and an error dialog, which is the wrong weight for
/// something that clears on its own within a second (issue #360), and reporting it as a
/// cancellation instead would tell the user they cancelled something they did not.
/// </para>
/// </summary>
public enum ScreenshotToClipboardOutcome
{
	/// <summary>The picture reached the clipboard.</summary>
	Copied,

	/// <summary>
	/// The user dismissed the region overlay without selecting anything, or a capture was
	/// already on screen. Nothing was captured and nothing on the clipboard changed.
	/// </summary>
	Cancelled,

	/// <summary>
	/// A region was captured, but another program held the clipboard open through every retry,
	/// so the picture never got there. The clipboard still holds whatever it held before.
	/// </summary>
	ClipboardUnavailable,
}
