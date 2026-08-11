namespace Mutation.Ui.Core;

/// <summary>
/// What became of a plain screenshot capture — the one whose only product is a picture on the
/// clipboard.
/// <para>
/// Five answers, not two, because neither a busy clipboard nor a refused press is a
/// cancellation. A busy clipboard used to reach the caller as a thrown exception and an error
/// dialog, which is the wrong weight for something that clears on its own within a second
/// (issue #360), and reporting either of them as a cancellation tells the user they cancelled
/// something they did not.
/// </para>
/// <para>
/// A refused press splits in two because the user's situation splits in two. A capture holds
/// the guard from the moment it starts until the picture is on the clipboard, but the overlay
/// is on screen for only the first part of that. Telling someone with no overlay in front of
/// them to select a region is an instruction they cannot follow (issue #367).
/// </para>
/// </summary>
public enum ScreenshotToClipboardOutcome
{
	/// <summary>
	/// The user dismissed the region overlay without selecting anything. Nothing was captured
	/// and nothing on the clipboard changed.
	/// <para>
	/// First, so that it is what <c>default</c> gives. Nothing happened is the only one of the
	/// five that is safe to say by accident: a zero value meaning <see cref="Copied"/> would
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
	/// is still in front of them and wants a selection.
	/// </para>
	/// </summary>
	RefusedOverlayWaiting,

	/// <summary>
	/// The press arrived while a capture was running but past the point of showing an overlay —
	/// during the crop, the clipboard write and its retries, or the whole reading when the
	/// capture that holds the guard is a screenshot-and-OCR run. It started nothing, and there
	/// is nothing on screen for the user to do anything with.
	/// <para>
	/// Split from <see cref="RefusedOverlayWaiting"/> because the advice differs and the wrong
	/// half is actively harmful. "Select a region, or press Escape to cancel it" cannot be
	/// followed when no overlay exists, and the Escape it invites goes to whatever application
	/// actually holds the keyboard (issue #367). All the user can usefully do is wait.
	/// </para>
	/// </summary>
	RefusedCaptureRunning,
}
