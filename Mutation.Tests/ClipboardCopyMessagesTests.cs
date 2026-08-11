using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Controls;
using Mutation.Ui.Core;
using Xunit;

namespace Mutation.Tests;

/// <summary>
/// What the user is told about where a capture ended up. The decisions are small and the cost of
/// getting one wrong is not: a blind user acts on what they hear, so announcing a copy that did
/// not happen sends them to paste something that is not there.
/// </summary>
public class ClipboardCopyMessagesTests
{
	[Fact]
	public void ACopiedScreenshotIsAnnouncedAsASuccess()
	{
		var (message, severity) = ClipboardCopyMessages.ForScreenshot(ScreenshotToClipboardOutcome.Copied);

		Assert.Equal(ClipboardCopyMessages.ScreenshotCopied, message);
		Assert.Equal(InfoBarSeverity.Success, severity);
	}

	/// <summary>
	/// A busy clipboard is an error, not a cancellation and not a success. The severity is not
	/// decoration: <c>StatusAnnouncement.GetProcessing</c> turns Error into an announcement that
	/// interrupts whatever the screen reader is saying, which is the only reason the user learns
	/// of it while working in another application.
	/// </summary>
	[Fact]
	public void ABusyClipboardIsAnnouncedAsAnError()
	{
		var (message, severity) = ClipboardCopyMessages.ForScreenshot(ScreenshotToClipboardOutcome.ClipboardUnavailable);

		Assert.Equal(ClipboardCopyMessages.ScreenshotClipboardBusy, message);
		Assert.Equal(InfoBarSeverity.Error, severity);
	}

	[Fact]
	public void ACancelledCaptureIsAnnouncedQuietly()
	{
		var (message, severity) = ClipboardCopyMessages.ForScreenshot(ScreenshotToClipboardOutcome.Cancelled);

		Assert.Equal(ClipboardCopyMessages.ScreenshotCancelled, message);
		Assert.Equal(InfoBarSeverity.Informational, severity);
	}

	/// <summary>
	/// A press refused because an overlay is already on screen must not be announced as a
	/// cancellation. The two are opposite news for someone who cannot see the screen: cancelled
	/// says the overlay has gone, refused says it is still there waiting for a region (issue
	/// #363). Both sentences used to be the same one.
	/// </summary>
	[Fact]
	public void ARefusedPressSaysACaptureIsAlreadyWaitingRatherThanCancelled()
	{
		var (message, _) = ClipboardCopyMessages.ForScreenshot(ScreenshotToClipboardOutcome.Refused);

		Assert.Equal(ClipboardCopyMessages.ScreenshotAlreadyInProgress, message);
		Assert.NotEqual(ClipboardCopyMessages.ScreenshotCancelled, message);
	}

	/// <summary>
	/// And it interrupts. The user pressed a key and saw nothing change; a polite announcement
	/// queues behind whatever the overlay is saying and lands after the moment it was wanted.
	/// <c>StatusAnnouncement.GetProcessing</c> promotes Warning alongside Error.
	/// </summary>
	[Fact]
	public void ARefusedPressInterruptsTheScreenReader()
	{
		var (_, severity) = ClipboardCopyMessages.ForScreenshot(ScreenshotToClipboardOutcome.Refused);

		Assert.Equal(InfoBarSeverity.Warning, severity);
		Assert.Equal(
			AutomationNotificationProcessing.ImportantMostRecent,
			StatusAnnouncement.GetProcessing(severity));
	}

	/// <summary>
	/// The refusal has to tell the user the overlay is still there and how to get out of it.
	/// A sentence that only said "already in progress" would leave someone who cannot see the
	/// screen with no idea what is now in front of them.
	/// </summary>
	[Fact]
	public void TheRefusalSaysWhatIsOnScreenAndHowToLeaveIt()
	{
		Assert.Contains("already in progress", ClipboardCopyMessages.ScreenshotAlreadyInProgress);
		Assert.Contains("Escape", ClipboardCopyMessages.ScreenshotAlreadyInProgress);
	}

	/// <summary>
	/// The same sentence is said to someone who pressed the shortcut and someone who clicked the
	/// button, so it must not name either. Telling a screen-reader user to press a shortcut they
	/// did not use points them at the wrong control.
	/// </summary>
	[Fact]
	public void TheBusyClipboardAdviceNamesNeitherTheButtonNorTheShortcut()
	{
		Assert.DoesNotContain("shortcut", ClipboardCopyMessages.ScreenshotClipboardBusy);
		Assert.DoesNotContain("button", ClipboardCopyMessages.ScreenshotClipboardBusy);
	}

	/// <summary>
	/// An unknown outcome is announced as a cancellation — the only one of the four that is safe
	/// to say by accident, because it claims nothing.
	/// </summary>
	[Fact]
	public void AnUnknownOutcomeNeverClaimsACopy()
	{
		var (message, _) = ClipboardCopyMessages.ForScreenshot((ScreenshotToClipboardOutcome)99);

		Assert.Equal(ClipboardCopyMessages.ScreenshotCancelled, message);
	}

	/// <summary>
	/// Nothing is said when everything worked. The success line the caller shows afterwards is
	/// the whole of the story then.
	/// </summary>
	[Fact]
	public void ARunThatCopiedEverythingHasNothingToReport()
	{
		Assert.Null(ClipboardCopyMessages.ForOcrRun(success: true, textCopyFailed: false, pictureCopyFailed: false));
	}

	[Fact]
	public void TextThatDidNotReachTheClipboardIsReported()
	{
		Assert.Equal(
			ClipboardCopyMessages.OcrTextNotCopied,
			ClipboardCopyMessages.ForOcrRun(success: true, textCopyFailed: true, pictureCopyFailed: false));
	}

	[Fact]
	public void APictureThatDidNotReachTheClipboardIsReported()
	{
		Assert.Equal(
			ClipboardCopyMessages.OcrPictureNotCopied,
			ClipboardCopyMessages.ForOcrRun(success: true, textCopyFailed: false, pictureCopyFailed: true));
	}

	/// <summary>
	/// When both failed, the text is what gets said. It is the thing the user asked for and the
	/// one they can still lose; the picture they can take again.
	/// </summary>
	[Fact]
	public void AFailedTextCopyOutranksAFailedPictureCopy()
	{
		Assert.Equal(
			ClipboardCopyMessages.OcrTextNotCopied,
			ClipboardCopyMessages.ForOcrRun(success: true, textCopyFailed: true, pictureCopyFailed: true));
	}

	/// <summary>
	/// A reading that produced nothing has its own error to show, and that error is the news. A
	/// sentence about the clipboard in front of it would bury the reason there is no text — and a
	/// run that recognised nothing never had text to copy in the first place (issue #341).
	/// </summary>
	[Theory]
	[InlineData(false, false)]
	[InlineData(true, false)]
	[InlineData(false, true)]
	[InlineData(true, true)]
	public void AFailedReadingSaysNothingAboutTheClipboard(bool textCopyFailed, bool pictureCopyFailed)
	{
		Assert.Null(ClipboardCopyMessages.ForOcrRun(success: false, textCopyFailed, pictureCopyFailed));
	}
}
