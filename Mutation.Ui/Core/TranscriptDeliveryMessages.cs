namespace Mutation.Ui.Core;

/// <summary>
/// What to tell the user about a delivery that did not fully land. Every message ends
/// by saying where the text still is, because the point of telling them is that they
/// can retrieve it.
/// </summary>
public static class TranscriptDeliveryMessages
{
	public const string StillAvailable = "It is available in the Mutation window.";

	/// <summary>
	/// The failure to announce, or null when there is nothing to report.
	/// </summary>
	/// <param name="clipboardCopied">Whether the text reached the clipboard.</param>
	/// <param name="outcome">How far the insert into the other application got.</param>
	/// <param name="subject">
	/// Names the text in the message — "transcript", "formatted transcript",
	/// "processed text".
	/// </param>
	/// <remarks>
	/// An injection failure is reported ahead of a clipboard failure: it is the one the
	/// user cannot otherwise discover. A blind user who is told "Transcript ready" has
	/// no way to notice that nothing was typed into the other window.
	/// </remarks>
	public static string? Failure(bool clipboardCopied, TranscriptDeliveryOutcome outcome, string subject)
	{
		if (outcome == TranscriptDeliveryOutcome.InjectionFailed)
			return $"The {subject} could not be sent to the other application; it may be running with higher privileges than Mutation. {StillAvailable}";

		if (!clipboardCopied || outcome == TranscriptDeliveryOutcome.ClipboardBlocked)
			return $"The clipboard is in use by another application; the {subject} could not be delivered. {StillAvailable}";

		return null;
	}
}
