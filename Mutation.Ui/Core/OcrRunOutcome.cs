namespace Mutation.Ui.Core;

/// <summary>
/// What became of an OCR run, for the one decision that cannot be made from success alone:
/// whether there is anything for the configured post-run shortcut to act on.
/// </summary>
public enum OcrRunOutcome
{
	/// <summary>
	/// The run reached an answer — recognised text, or a message saying why not. Either is
	/// worth reading, which is why the shortcut is sent after a failure as well as a success.
	/// </summary>
	Answered,

	/// <summary>
	/// The run never started, because a capture was already under way. Nothing changed and
	/// there is no new answer in the OCR box, so the shortcut has nothing to act on — and when
	/// the capture is still at its overlay, that overlay has the keyboard, so a shortcut sent
	/// now would land in it rather than anywhere it was aimed (issue #342).
	/// <para>
	/// Withheld either way. A capture past its overlay has no keyboard to steal, but it also has
	/// no new answer to read, so sending the shortcut would aim a screen-reader command at the
	/// previous run's text. One value covers both because the answer is the same; the plain
	/// screenshot path splits its refusal in two because there the advice differs (issue #367).
	/// </para>
	/// </summary>
	Refused,
}
