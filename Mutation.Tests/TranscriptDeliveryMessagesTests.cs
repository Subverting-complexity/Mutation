using Mutation.Ui.Core;

namespace Mutation.Tests;

// Covers issue #232: both delivery branches fired the injection and returned success
// immediately, and SendInput's return count was thrown away. With an elevated app in
// front, Windows drops the injected input silently — and Mutation still played the
// success beep and announced "Transcript ready." A blind user had no way to discover
// that nothing had been typed.
public class TranscriptDeliveryMessagesTests
{
	[Fact]
	public void EverythingLanded_HasNothingToReport()
	{
		Assert.Null(TranscriptDeliveryMessages.Failure(
			clipboardCopied: true, TranscriptDeliveryOutcome.Delivered, "transcript"));
	}

	[Fact]
	public void InjectionFailure_IsReported_EvenThoughTheClipboardCopyWorked()
	{
		string? failure = TranscriptDeliveryMessages.Failure(
			clipboardCopied: true, TranscriptDeliveryOutcome.InjectionFailed, "transcript");

		Assert.NotNull(failure);
		Assert.Contains("could not be sent to the other application", failure);
		Assert.Contains(TranscriptDeliveryMessages.StillAvailable, failure);
	}

	// The two failures have different causes and different remedies, so they must not
	// read the same: blaming the clipboard for a privilege problem sends the user
	// hunting for a clipboard manager that was never involved.
	[Fact]
	public void InjectionFailure_DoesNotBlameTheClipboard()
	{
		string? failure = TranscriptDeliveryMessages.Failure(
			clipboardCopied: true, TranscriptDeliveryOutcome.InjectionFailed, "transcript");

		Assert.DoesNotContain("clipboard", failure!, System.StringComparison.OrdinalIgnoreCase);
	}

	// An injection failure is the one the user cannot otherwise notice, so it wins when
	// both went wrong.
	[Fact]
	public void InjectionFailure_IsReportedAheadOfAClipboardFailure()
	{
		string? failure = TranscriptDeliveryMessages.Failure(
			clipboardCopied: false, TranscriptDeliveryOutcome.InjectionFailed, "transcript");

		Assert.Contains("could not be sent to the other application", failure);
	}

	[Fact]
	public void ClipboardFailure_IsStillReported()
	{
		string? failure = TranscriptDeliveryMessages.Failure(
			clipboardCopied: false, TranscriptDeliveryOutcome.Delivered, "transcript");

		Assert.NotNull(failure);
		Assert.Contains("clipboard is in use by another application", failure);
		Assert.Contains(TranscriptDeliveryMessages.StillAvailable, failure);
	}

	[Fact]
	public void PasteWithNoClipboard_IsReportedAsAClipboardFailure()
	{
		string? failure = TranscriptDeliveryMessages.Failure(
			clipboardCopied: true, TranscriptDeliveryOutcome.ClipboardBlocked, "transcript");

		Assert.Contains("clipboard is in use by another application", failure);
	}

	// Every failure has to end by saying where the text still is — that is the whole
	// point of telling the user.
	[Theory]
	[InlineData(false, TranscriptDeliveryOutcome.Delivered)]
	[InlineData(true, TranscriptDeliveryOutcome.ClipboardBlocked)]
	[InlineData(true, TranscriptDeliveryOutcome.InjectionFailed)]
	public void EveryFailure_SaysWhereTheTextIs(bool copied, TranscriptDeliveryOutcome outcome)
	{
		string? failure = TranscriptDeliveryMessages.Failure(copied, outcome, "transcript");

		Assert.NotNull(failure);
		Assert.EndsWith(TranscriptDeliveryMessages.StillAvailable, failure);
	}

	[Theory]
	[InlineData("transcript")]
	[InlineData("formatted transcript")]
	[InlineData("processed text")]
	public void TheMessageNamesWhatCouldNotBeDelivered(string subject)
	{
		Assert.Contains(subject, TranscriptDeliveryMessages.Failure(
			clipboardCopied: false, TranscriptDeliveryOutcome.Delivered, subject));
		Assert.Contains(subject, TranscriptDeliveryMessages.Failure(
			clipboardCopied: true, TranscriptDeliveryOutcome.InjectionFailed, subject));
	}
}
