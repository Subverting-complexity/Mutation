using CognitiveSupport;

namespace Mutation.Ui.Core;

/// <summary>
/// What a finished transcript run does: which beep the user hears, what they are told, and
/// whether the shortcut configured to run afterwards is sent.
/// </summary>
/// <param name="Beep">The beep to play. Never absent — the user is driving by ear.</param>
/// <param name="FailureMessage">
/// Null when the text landed. Otherwise what went wrong, and where the text still is.
/// </param>
/// <param name="SendConfiguredHotkey">
/// Whether to send <c>SendHotkeyAfterTranscriptionOperation</c>. Only on success: that
/// shortcut is whatever the user does next with the text — file it, submit it, delete a
/// line — and firing it after a delivery that did not land aims it at the wrong thing.
/// </param>
internal readonly record struct TranscriptCompletionPlan(
	BeepType Beep,
	string? FailureMessage,
	bool SendConfiguredHotkey)
{
	public bool Succeeded => FailureMessage is null;
}

/// <summary>
/// The end of a transcript run, decided in one place.
/// <para>
/// This was three lines inline in two handlers, which made it invisible that all three
/// outcomes hang together: the success beep, the status, and the shortcut sent afterwards
/// are one decision, and when a delivery was wrongly reported as failed the user lost all
/// three at once (issue #325).
/// </para>
/// </summary>
internal static class TranscriptCompletionPlanner
{
	/// <param name="clipboardCopied">Whether the text reached the clipboard.</param>
	/// <param name="outcome">How far the insert into the other application got.</param>
	/// <param name="subject">Names the text in the message — "transcript", "processed text".</param>
	public static TranscriptCompletionPlan Plan(
		bool clipboardCopied,
		TranscriptDeliveryOutcome outcome,
		string subject)
	{
		string? failure = TranscriptDeliveryMessages.Failure(clipboardCopied, outcome, subject);

		return failure is null
			? new TranscriptCompletionPlan(BeepType.Success, null, SendConfiguredHotkey: true)
			: new TranscriptCompletionPlan(BeepType.Failure, failure, SendConfiguredHotkey: false);
	}
}
