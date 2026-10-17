using CognitiveSupport;

namespace Mutation.Ui.Core;

/// <summary>
/// What a dictation run says when the model declined to tidy the transcript.
/// </summary>
internal static class LlmRefusalMessages
{
	/// <summary>
	/// Folds the refusal into the delivery announcement. The dictation is still delivered,
	/// rules-formatted, so the refusal cannot be raised on its own: status supersedes rather
	/// than queues, and the delivery's "Transcript ready." a moment later would talk over
	/// it, leaving the user believing the model had run.
	/// </summary>
	internal static string RefusedThen(string deliveryMessage) =>
		$"{LlmRefusalException.UserMessage} Your transcript has only the basic formatting rules applied. {deliveryMessage}";
}
