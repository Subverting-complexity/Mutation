using Mutation.Ui.Core;

namespace Mutation.Tests;

// Covers issue #299: cancelling a transcription announced the identical
// "Transcription cancelled." from two places — the cancel request and the operation
// unwinding a second or two later. MainWindow.AnnounceStatus raises a UIA notification on
// every status change by design (issue #164), so nothing deduplicated them and a
// screen-reader user heard the same sentence twice, as though a second thing had been
// cancelled.
public class CancellationMessagesTests
{
	[Fact]
	public void TheRequestAndTheCompletionDoNotSayTheSameThing()
	{
		Assert.NotEqual(
			CancellationMessages.TranscriptionRequested,
			CancellationMessages.TranscriptionCompleted);
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
	public void LlmAndTranscriptionCancelsNameDifferentThings()
	{
		// Both can be cancelled from the same key, one after the other, so "cancelled"
		// alone would leave the user unsure which step gave up.
		Assert.NotEqual(CancellationMessages.TranscriptionRequested, CancellationMessages.LlmRequested);
		Assert.NotEqual(CancellationMessages.TranscriptionCompleted, CancellationMessages.LlmCompleted);
	}

	[Fact]
	public void LlmCancelRidesWithTheDeliveryAnnouncement_SoNeitherIsTalkedOver()
	{
		// Status supersedes rather than queues, so a cancel raised on its own would be
		// wiped by the delivery line that follows it a moment later.
		string composed = CancellationMessages.LlmCancelledThen("Transcript ready and copied.");

		Assert.StartsWith(CancellationMessages.LlmCompleted, composed);
		Assert.EndsWith("Transcript ready and copied.", composed);
	}
}
