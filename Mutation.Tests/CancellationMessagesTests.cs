using Mutation.Ui.Core;

namespace Mutation.Tests;

// Covers issue #299: cancelling a transcription announced the identical
// "Transcription cancelled." from two places — the cancel request and the operation
// unwinding a second or two later. MainWindow.AnnounceStatus raises a UIA notification on
// every status change by design (issue #164), so nothing deduplicated them and a
// screen-reader user heard the same sentence twice, as though a second thing had been
// cancelled.
//
// The *wiring* — which message each moment raises, and that a repeat press says something
// else again — is asserted in DictationPressPlannerTests, because that is where the bug
// lived. What is left here is the one property the strings themselves have to hold.
public class CancellationMessagesTests
{
	[Fact]
	public void EveryCancelMessageIsDistinct()
	{
		// The whole of issue #299 in one assertion: no two moments of a cancel, and no two
		// operations that can be cancelled from the same key, may say the same words.
		string[] messages =
		[
			CancellationMessages.TranscriptionRequested,
			CancellationMessages.TranscriptionCompleted,
			CancellationMessages.LlmRequested,
			CancellationMessages.LlmCompleted,
			CancellationMessages.AlreadyStopping,
		];

		Assert.Equal(messages.Length, messages.Distinct().Count());
	}

	[Fact]
	public void TheRequestReadsAsProgress_AndTheCompletionAsDone()
	{
		// Two moments, and the wording has to tell them apart: the first says work is
		// winding down, the second that it has stopped. A blind user has only the words.
		Assert.EndsWith("...", CancellationMessages.TranscriptionRequested);
		Assert.DoesNotContain("...", CancellationMessages.TranscriptionCompleted);

		Assert.EndsWith("...", CancellationMessages.LlmRequested);
		Assert.DoesNotContain("...", CancellationMessages.LlmCompleted);
	}

	[Fact]
	public void TheLlmMessagesUseTheWordTheWindowShows()
	{
		// A screen-reader user navigates by the labels they hear. The button is
		// "Process with LLM" and the step announces "Processing with LLM...", so a cancel
		// that spoke of "language model processing" named nothing they could find.
		Assert.Contains("LLM", CancellationMessages.LlmRequested);
		Assert.Contains("LLM", CancellationMessages.LlmCompleted);
		Assert.Equal(
			RecordingUiPlanner.ProcessingWithLlmPlaceholder,
			"Processing with LLM...");
	}

	[Fact]
	public void LlmCancelRidesWithTheDeliveryAnnouncement_SoNeitherIsTalkedOver()
	{
		// Status supersedes rather than queues, so a cancel raised on its own would be wiped
		// by the delivery line that follows it a moment later. Folding it in keeps both.
		string composed = CancellationMessages.LlmCancelledThen("Transcript ready.");

		Assert.StartsWith(CancellationMessages.LlmCompleted, composed);
		Assert.EndsWith("Transcript ready.", composed);
	}
}
