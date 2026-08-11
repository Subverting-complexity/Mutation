using Microsoft.UI.Xaml.Controls;

namespace Mutation.Ui.Core;

/// <summary>
/// What to tell the user about where a capture ended up — the picture, the text, or neither.
/// <para>
/// Pure, and out here rather than in the window, because the decisions are worth pinning: which
/// of five outcomes a plain screenshot gets, how loudly each is said, and which of two clipboard
/// failures is worth saying when both happened. The window can only be checked by running it, so
/// a decision left in it is a decision nothing holds in place.
/// </para>
/// </summary>
public static class ClipboardCopyMessages
{
	public const string ScreenshotCopied = "Screenshot copied to the clipboard.";

	public const string ScreenshotCancelled = "Screenshot cancelled. Nothing was copied to the clipboard.";

	/// <summary>
	/// Said when a screenshot was captured but the clipboard would not take it. It names the
	/// cause, because the cause is the whole of the advice: something else has the clipboard for
	/// a moment, and trying again almost always gets in.
	/// <para>
	/// "Try again", not "press the shortcut again". The same sentence is said to someone who
	/// clicked the button, and telling them to press a shortcut they did not use points them at
	/// the wrong control — which matters most to the user who is listening rather than looking.
	/// </para>
	/// </summary>
	public const string ScreenshotClipboardBusy =
		"Another program is using the clipboard, so the screenshot was not copied. Try again in a moment.";

	/// <summary>
	/// Said when a screenshot press arrives while a capture overlay is already on screen. It
	/// describes where the user is rather than what the press did, because the press did nothing
	/// and the overlay is what they now have to deal with.
	/// <para>
	/// "Select a region", not "press Escape", even though escaping is the way out. The common
	/// case is a second press that landed because the first was not heard, and that user wants to
	/// go on with the capture they started, not throw it away.
	/// </para>
	/// </summary>
	public const string ScreenshotOverlayWaiting =
		"A screenshot is already in progress. Select a region, or press Escape to cancel it.";

	/// <summary>
	/// Said when a screenshot press arrives while a capture is running with no overlay on screen
	/// — during the clipboard write, or the whole reading when a screenshot-and-OCR run holds
	/// the guard.
	/// <para>
	/// Both sentences used to be <see cref="ScreenshotOverlayWaiting"/>, which told the user to
	/// select a region that did not exist and offered an Escape that would have gone to whatever
	/// application actually had the keyboard (issue #367). It names no control at all now,
	/// because there is none: waiting is genuinely the whole of the advice.
	/// </para>
	/// </summary>
	public const string ScreenshotCaptureRunning =
		"A screenshot is already in progress. Wait for it to finish, then try again.";

	/// <summary>
	/// Put in the OCR result box when a reading press arrives while a capture is already under
	/// way. Shorter than either screenshot refusal and deliberately so: it names no control,
	/// because it is said in both states and the advice differs between them. The plain
	/// screenshot path can afford two sentences because it knows which state it is in; this one
	/// would have to guess (issue #367).
	/// <para>
	/// A constant rather than a literal in the guard, because it is read aloud from the OCR box
	/// and quoted in the user guide, so three copies had to be kept in step by hand (issue
	/// #368). Nothing in production reads it back to make a decision — that is what
	/// <c>OcrRunOutcome</c> is for, and comparing this text instead is the mistake issue #342
	/// was filed about.
	/// </para>
	/// </summary>
	public const string OcrCaptureAlreadyInProgress = "Screenshot already in progress";

	/// <summary>
	/// Said when the text was read but the clipboard would not take it. Both halves matter: the
	/// user has to know the copy did not happen, and — because the shortcut that runs next is
	/// usually a screen-reader command aimed at the result — that the text itself is not lost.
	/// </summary>
	public const string OcrTextNotCopied =
		"The text was recognised, but it could not be copied to the clipboard. It is in the OCR results box.";

	/// <summary>
	/// Said when a screenshot was read successfully but the picture itself did not reach the
	/// clipboard. Leads with the good news, because the text is what the user asked for and it
	/// is safely on the clipboard — only the picture is missing.
	/// </summary>
	public const string OcrPictureNotCopied =
		"The text was recognised and copied, but the screenshot picture could not be put on the clipboard.";

	/// <summary>
	/// What to say about a plain screenshot capture, and how loudly.
	/// <para>
	/// A busy clipboard is an error and a cancellation is not, which is the distinction the
	/// severity carries to a screen reader: an error interrupts what it is saying, and an
	/// informational message waits its turn.
	/// </para>
	/// <para>
	/// Every case is named, and the final arm is a safety net rather than a case. Two things are
	/// wanted here and only one of them can come from the compiler. A value outside the enum has
	/// to produce a sentence rather than a <c>SwitchExpressionException</c>, because throwing
	/// here would take down a hotkey handler in front of a user who cannot see the dialog; that
	/// needs the discard arm. But a discard arm also silences the exhaustiveness warning, so a
	/// sixth member added later would compile clean and be announced as "Screenshot cancelled" —
	/// which is the exact bug this class was split out to prevent, waiting for the next value.
	/// </para>
	/// <para>
	/// So the enforcement is a test, not the compiler:
	/// <c>ClipboardCopyMessagesTests.EveryOutcomeHasItsOwnAnswer</c> walks the enum and fails on
	/// any member that falls through to the net. This comment used to claim the compiler did it,
	/// which was never true and was twice edited without being checked (issue #368). If you add
	/// a member, that test is what tells you.
	/// </para>
	/// <para>
	/// Both refusals are warnings, which interrupt as an error does. Nothing failed, so neither
	/// is an error — but the user pressed a key and got no visible change, and the sentence
	/// telling them why is the only thing that explains what is or is not in front of them. Left
	/// polite it would queue behind whatever the overlay is saying, and arrive after the moment
	/// it was needed.
	/// </para>
	/// <para>
	/// The two refusals differ only in their advice, and that is exactly why they are separate.
	/// One sentence fitted to both told a user with no overlay to select a region (issue #367).
	/// </para>
	/// </summary>
	public static (string Message, InfoBarSeverity Severity) ForScreenshot(ScreenshotToClipboardOutcome outcome) =>
		outcome switch
		{
			ScreenshotToClipboardOutcome.Copied => (ScreenshotCopied, InfoBarSeverity.Success),
			ScreenshotToClipboardOutcome.ClipboardUnavailable => (ScreenshotClipboardBusy, InfoBarSeverity.Error),
			ScreenshotToClipboardOutcome.RefusedOverlayWaiting => (ScreenshotOverlayWaiting, InfoBarSeverity.Warning),
			ScreenshotToClipboardOutcome.RefusedCaptureRunning => (ScreenshotCaptureRunning, InfoBarSeverity.Warning),
			ScreenshotToClipboardOutcome.Cancelled => (ScreenshotCancelled, InfoBarSeverity.Informational),
			// The net, not a case. See the note above: a value from outside the enum must get a
			// sentence rather than an exception, and a cancellation is the only thing safe to say
			// by accident because it claims nothing.
			_ => (ScreenshotCancelled, InfoBarSeverity.Informational),
		};

	/// <summary>
	/// How loudly to say what an unsuccessful OCR run came back with.
	/// <para>
	/// A refused run is not a failure, so it does not get the failure severity. It was shown as
	/// an error, which a screen reader announces as an aborted action — for a press where
	/// nothing was attempted and nothing went wrong. Warning still interrupts, which this needs,
	/// without claiming the run broke, and it matches what the plain screenshot path says about
	/// the same press (issue #367).
	/// </para>
	/// <para>
	/// Out here rather than inline in the window for the reason this whole class exists: the
	/// window cannot be checked without running it, so a decision made there is a decision that
	/// can be undone without anything going red. That is how the wrong sentence this fix
	/// replaced survived a green suite in the first place.
	/// </para>
	/// </summary>
	/// <param name="outcome">What the run came back as.</param>
	/// <param name="whenFailed">The severity for a run that genuinely failed.</param>
	public static InfoBarSeverity ForOcrRunSeverity(OcrRunOutcome outcome, InfoBarSeverity whenFailed) =>
		outcome == OcrRunOutcome.Refused ? InfoBarSeverity.Warning : whenFailed;

	/// <summary>
	/// What to say about an OCR run's clipboard writes, or null when there is nothing to say.
	/// </summary>
	/// <param name="success">Whether the reading itself produced text.</param>
	/// <param name="textCopyFailed">Whether that text failed to reach the clipboard.</param>
	/// <param name="pictureCopyFailed">Whether the screenshot it read failed to reach the clipboard.</param>
	/// <remarks>
	/// Two rules, both worth stating. A failed text copy outranks a failed picture copy: the text
	/// is what the user asked for and the one they can still lose, while the picture is a side
	/// effect they can retake. And neither is said when the reading itself failed — the reason
	/// there is no text is the news, and a sentence about the clipboard in front of it would bury
	/// it. The clipboard is then simply unchanged.
	/// </remarks>
	public static string? ForOcrRun(bool success, bool textCopyFailed, bool pictureCopyFailed)
	{
		if (!success)
			return null;

		if (textCopyFailed)
			return OcrTextNotCopied;

		return pictureCopyFailed ? OcrPictureNotCopied : null;
	}
}
