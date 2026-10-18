using System;

namespace Mutation.Ui.Core;

/// <summary>
/// What a dictation run says when the model step failed for a reason other than a cancel
/// or a refusal: a timeout, a bad API key, a server error after the retries ran out.
/// </summary>
internal static class LlmFailureMessages
{
	/// <summary>
	/// Folds the failure into the delivery announcement, for the same reason as
	/// <see cref="LlmRefusalMessages.RefusedThen"/>: said on its own, the failure would be
	/// talked over by "Transcript ready." a moment later, and the user would believe their
	/// prompt had run.
	/// </summary>
	/// <param name="failure">What went wrong, from <see cref="Describe"/>.</param>
	/// <param name="deliveryMessage">The delivery line, said last.</param>
	internal static string FailedThen(string failure, string deliveryMessage) =>
		$"The language model step failed: {failure}. Your transcript has only the basic formatting rules applied. {deliveryMessage}";

	/// <summary>
	/// A short description of the failure, with no closing full stop, so the sentence it
	/// goes into does not end in two.
	/// </summary>
	internal static string Describe(Exception ex)
	{
		ArgumentNullException.ThrowIfNull(ex);

		// A cancel the user asked for never reaches here, so a cancellation is a timeout:
		// every try ran out of time. Its own message ("The operation was canceled.") would
		// tell the user they had stopped it.
		if (ex is OperationCanceledException or TimeoutException)
			return "the model did not answer in time";

		string message = ex.Message.Trim().TrimEnd('.', ' ');
		return message.Length > 0 ? message : "an unknown error";
	}
}
