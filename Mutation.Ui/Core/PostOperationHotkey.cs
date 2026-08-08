namespace Mutation.Ui.Core;

/// <summary>
/// How long to wait before sending the shortcut a user has configured to run when an
/// operation finishes.
/// <para>
/// The delay exists because the shortcut lands in whichever window has the keyboard — the
/// user's editor, not Mutation — and that window needs the moment after the text arrives
/// before it can act on it.
/// </para>
/// </summary>
internal static class PostOperationHotkey
{
	/// <summary>After a run that delivered text, which the shortcut is about to act on.</summary>
	public const int SuccessDelayMs = 100;

	/// <summary>
	/// After a run that did not. Shorter, because there is no text settling in the other
	/// window to wait for.
	/// </summary>
	public const int FailureDelayMs = 50;

	/// <summary>
	/// The delay for an OCR run. OCR sends the shortcut either way: the OCR shortcuts are
	/// commonly routed to a screen-reader command that reads the result area, and a user
	/// working by ear needs that to happen just as much when the answer is an error as when
	/// it is text.
	/// </summary>
	public static int OcrDelay(bool success) => success ? SuccessDelayMs : FailureDelayMs;

	/// <summary>
	/// Whether an OCR run left anything for the shortcut to act on.
	/// <para>
	/// Sending after a failure is deliberate, and stays: an error in the OCR box wants reading
	/// as much as a result does. A refusal is not that kind of failure. Pressing an OCR
	/// shortcut while a capture is already on screen changes nothing — the OCR box still holds
	/// the last run's answer — and the thing in front of the user at that moment is the capture
	/// overlay, so the keystroke would be typed into it (issue #342).
	/// </para>
	/// </summary>
	public static bool ShouldSendAfterOcr(OcrRunOutcome outcome) => outcome != OcrRunOutcome.Refused;
}
