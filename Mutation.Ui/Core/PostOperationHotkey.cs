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

	/// <summary>
	/// Whether an OCR run left its text on the clipboard, where a paste would find it.
	/// <para>
	/// The whitespace check is not tidiness. A read that recognised nothing still comes back
	/// as a success with an empty message, and the copy is skipped rather than failed —
	/// there was nothing to copy. What the clipboard still holds at that point is whatever
	/// the run put there a moment earlier, which on the screenshot paths is the screenshot
	/// itself and on the clipboard-image path is the source image. Pasting then puts a
	/// picture into the user's document.
	/// </para>
	/// </summary>
	public static bool ClipboardHoldsOcrText(bool success, bool clipboardCopyFailed, string? text) =>
		success && !clipboardCopyFailed && !string.IsNullOrWhiteSpace(text);

	/// <summary>The chord that pastes, spelled the way <c>Hotkey.Parse</c> reads it.</summary>
	/// <remarks>
	/// "Ctrl+V", not "^v": the parser has no caret syntax, so the shorthand would throw and drop
	/// the whole sequence to the WinForms fallback, which cannot say whether it was delivered.
	/// </remarks>
	public const string PasteChord = "Ctrl+V";

	/// <summary>
	/// Everything an OCR run sends to the other window, in order: the paste, when the user has
	/// asked for the recognised text to land in the app they were working in, then whatever they
	/// configured to run afterwards. Null when there is nothing to send.
	/// <para>
	/// One comma-separated string rather than two sends, because the order is the whole point.
	/// The shortcut people put in "Send hotkey after OCR" is usually a screen-reader command
	/// aimed at the result, and a command that reads before the paste arrives reads the wrong
	/// thing. A single sequence is delivered chord by chord in order by <c>SendHotkey</c>, so
	/// the two cannot race.
	/// </para>
	/// </summary>
	public static string? AfterOcr(bool paste, string? configured)
	{
		string? trimmed = string.IsNullOrWhiteSpace(configured) ? null : configured.Trim();

		if (!paste)
			return trimmed;

		return trimmed is null ? PasteChord : $"{PasteChord}, {trimmed}";
	}
}
