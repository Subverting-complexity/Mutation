namespace Mutation.Ui.Core;

/// <summary>
/// What became of a plain screenshot capture — the one whose only product is a picture on the
/// clipboard.
/// <para>
/// Four answers, not two, because neither a busy clipboard nor a refused press is a
/// cancellation. A busy clipboard used to reach the caller as a thrown exception and an error
/// dialog, which is the wrong weight for something that clears on its own within a second
/// (issue #360), and reporting either of them as a cancellation tells the user they cancelled
/// something they did not.
/// </para>
/// </summary>
public enum ScreenshotToClipboardOutcome
{
	/// <summary>
	/// The user dismissed the region overlay without selecting anything. Nothing was captured
	/// and nothing on the clipboard changed.
	/// <para>
	/// First, so that it is what <c>default</c> gives. Nothing happened is the only one of the
	/// four that is safe to say by accident: a zero value meaning <see cref="Copied"/> would
	/// let a value nobody set announce a screenshot that was never taken, which is the exact
	/// thing this enum exists to prevent. Nothing may be inserted in front of it.
	/// </para>
	/// </summary>
	Cancelled,

	/// <summary>The picture reached the clipboard.</summary>
	Copied,

	/// <summary>
	/// A region was captured, but another program held the clipboard open through every retry,
	/// so the picture never got there. The clipboard still holds whatever it held before.
	/// </summary>
	ClipboardUnavailable,

	/// <summary>
	/// The press arrived while a capture overlay was already on screen, so it started nothing.
	/// The overlay is still there, still waiting for a region, and has been brought to the
	/// front.
	/// <para>
	/// Its own value rather than <see cref="Cancelled"/>, because the two are opposite news for
	/// someone who cannot see the screen: cancelled says the overlay has gone, refused says it
	/// is still in front of them and wants a selection. Named to match
	/// <c>OcrRunOutcome.Refused</c>, which the screenshot-and-OCR path has told apart from a
	/// real cancellation since issue #342.
	/// </para>
	/// </summary>
	Refused,
}
