namespace Mutation.Ui.Core;

/// <summary>
/// What the user is told when they cancel something, in one place so the two halves of a
/// cancel cannot drift into saying the same thing twice.
/// </summary>
/// <remarks>
/// A cancel has two moments, and a screen-reader user hears both. The request is
/// acknowledged the instant the key is pressed; the operation itself only unwinds a
/// second or two later, once the network call it was waiting on lets go. Both moments are
/// worth announcing — silence after a keypress reads as a dead key — but they have to say
/// different things. They used to say the identical "Transcription cancelled.", and since
/// <c>MainWindow.AnnounceStatus</c> raises a UIA notification on every status change by
/// design (issue #164), nothing deduplicated them: the same sentence arrived twice, a
/// second apart, as though a second thing had been cancelled (issue #299).
/// </remarks>
internal static class CancellationMessages
{
	/// <summary>Progress: the cancel has been asked for and the operation is winding down.</summary>
	internal const string TranscriptionRequested = "Cancelling transcription...";

	/// <summary>Completion: the transcription has let go and nothing was delivered.</summary>
	internal const string TranscriptionCompleted = "Transcription cancelled.";

	/// <summary>
	/// Progress: the cancel has been asked for and the model call is winding down.
	/// "LLM" rather than "language model" because that is the word the window already
	/// uses — the <b>Process with LLM</b> button, the <b>LLM Prompts</b> card, and the
	/// "Processing with LLM..." line this cancel interrupts. A screen-reader user
	/// navigates by the labels they hear, so the announcement has to use them.
	/// </summary>
	internal const string LlmRequested = "Cancelling LLM processing...";

	/// <summary>Completion: the model call has let go.</summary>
	internal const string LlmCompleted = "LLM processing cancelled.";

	/// <summary>
	/// The answer to a repeat press on a stop already asked for. Cancelling is not
	/// instant — the call unwinds when the request it is waiting on lets go — and a user
	/// who hears nothing presses again. Answered rather than ignored, because silence from
	/// a shortcut reads as one that did not register; and answered with something
	/// different, because repeating the request line is how issue #299 sounded.
	/// </summary>
	internal const string AlreadyStopping = "Already stopping.";

	/// <summary>
	/// Cancelling the language-model step does not throw the dictation away — the
	/// rules-formatted transcript is delivered as usual, which is the same thing that
	/// happens when the model call fails. So the cancel travels with the delivery
	/// announcement instead of being raised on its own: status supersedes rather than
	/// queues, and a separate line would be talked straight over by the delivery.
	/// </summary>
	/// <remarks>
	/// It has to be folded into the announcement the transcript *delivery* ends on, not
	/// the one the audio session raises. The session's line is superseded a moment later
	/// by the delivery's own "Transcript ready.", so a cancel folded in there is heard by
	/// nobody. This is the same trap the FastModeNotice comment on TranscriptResult warns
	/// about, and the reason both notices travel with the transcript.
	/// </remarks>
	internal static string LlmCancelledThen(string deliveryMessage) =>
		$"{LlmCompleted} {deliveryMessage}";
}
