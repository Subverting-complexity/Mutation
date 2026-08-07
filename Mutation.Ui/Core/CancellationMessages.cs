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

	/// <summary>Progress: the cancel has been asked for and the model call is winding down.</summary>
	internal const string LlmRequested = "Cancelling language model processing...";

	/// <summary>Completion: the model call has let go.</summary>
	internal const string LlmCompleted = "Language model processing cancelled.";

	/// <summary>
	/// Cancelling the language-model step does not throw the dictation away — the
	/// rules-formatted transcript is delivered as usual, which is the same thing that
	/// happens when the model call fails. So the cancel travels with the delivery
	/// announcement instead of being raised on its own: status supersedes rather than
	/// queues, and a separate line would be talked straight over by the delivery.
	/// </summary>
	internal static string LlmCancelledThen(string deliveryMessage) =>
		$"{LlmCompleted} {deliveryMessage}";
}
