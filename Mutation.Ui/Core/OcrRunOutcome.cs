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
	/// The run never started, because a capture was already on screen. Nothing changed, there
	/// is no new answer in the OCR box, and the full-screen capture overlay still has the
	/// keyboard — so a shortcut sent now would land in the overlay rather than anywhere it was
	/// aimed (issue #342).
	/// </summary>
	Refused,
}
