using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CognitiveSupport;
using Microsoft.UI.Xaml.Controls;
using Mutation.Ui.Core;
using Mutation.Ui.Services;
using PdfSharp.Pdf;
using PdfSharp.Pdf.IO;
using Windows.Graphics.Imaging;

namespace Mutation.Tests;

public class OcrManagerTests
{
	private const OcrReadingOrder DefaultOrder = OcrReadingOrder.TopToBottomColumnAware;

	[Fact]
	public async Task ExtractTextFromFilesAsync_ThrowsArgumentNull_WhenPathsNull()
	{
		var manager = new TestableOcrManager(CreateValidSettings(), new StubOcrService(), new TestClipboard());

		await Assert.ThrowsAsync<ArgumentNullException>(() => manager.ExtractTextFromFilesAsync(null!, DefaultOrder, CancellationToken.None));
	}

	[Fact]
	public async Task ExtractTextFromFilesAsync_ReturnsFailure_WhenNoValidPaths()
	{
		var service = new StubOcrService();
		var clipboard = new TestClipboard();
		var manager = new TestableOcrManager(CreateValidSettings(), service, clipboard);

		var result = await manager.ExtractTextFromFilesAsync(new[] { string.Empty, "   	" }, DefaultOrder, CancellationToken.None);

		Assert.False(result.Success);
		Assert.Equal(string.Empty, result.Text);
		Assert.Equal(0, result.TotalCount);
		Assert.Equal(0, result.SuccessCount);
		Assert.Empty(result.Failures);
		Assert.Equal(0, clipboard.SetTextCalls);
		Assert.Equal(0, service.CallCount);

		WaitForBeep(manager, 1);
		Assert.Contains(BeepType.Failure, manager.Beeps);
	}

	[Fact]
	public async Task ExtractTextFromFilesAsync_ReturnsFailure_WhenOcrNotConfigured()
	{
		var settings = new Settings();
		var service = new StubOcrService();
		var clipboard = new TestClipboard();
		using var file = new TempFile(".png");
		var manager = new TestableOcrManager(settings, service, clipboard);

		var result = await manager.ExtractTextFromFilesAsync(new[] { file.Path }, DefaultOrder, CancellationToken.None);

		Assert.False(result.Success);
		Assert.Equal(1, result.TotalCount);
		Assert.Equal(0, result.SuccessCount);
		Assert.Single(result.Failures);
		Assert.Contains("Azure Computer Vision settings are missing", result.Failures[0], StringComparison.OrdinalIgnoreCase);
		Assert.Equal(0, clipboard.SetTextCalls);
		Assert.Equal(0, service.CallCount);
		Assert.Single(manager.Beeps);
		Assert.Equal(BeepType.Failure, manager.Beeps.Single());
	}

	[Fact]
	public async Task ExtractTextFromFilesAsync_ProcessesSingleImageFile_Successfully()
	{
		var settings = CreateValidSettings();
		var clipboard = new TestClipboard();
		var service = new StubOcrService("recognized text");
		using var file = new TempFile(".png");
		var manager = new TestableOcrManager(settings, service, clipboard);

		var result = await manager.ExtractTextFromFilesAsync(new[] { file.Path }, DefaultOrder, CancellationToken.None);

		string expectedText = $"[{Path.GetFileName(file.Path)}]{Environment.NewLine}recognized text{Environment.NewLine}";
		Assert.True(result.Success);
		Assert.Equal(1, result.TotalCount);
		Assert.Equal(1, result.SuccessCount);
		Assert.Equal(expectedText, result.Text);
		Assert.Equal(expectedText, clipboard.LastText);
		Assert.Equal(1, clipboard.SetTextCalls);
		WaitForBeep(manager, 1);
		Assert.Contains(BeepType.Success, manager.Beeps);
	}

	/// <summary>
	/// A clipboard held open by something else is retried, not reported. The window for it is
	/// widest right after a screenshot puts the image on the clipboard, which is exactly when a
	/// clipboard manager or a screen reader opens it to see what arrived — and the copy that
	/// followed had no retry at all, so a perfectly good read came back as a COM error where the
	/// text should have been (issue #341).
	/// </summary>
	[Fact]
	public async Task A_clipboard_that_is_briefly_busy_is_retried_and_the_text_still_lands()
	{
		using var image = new SoftwareBitmap(BitmapPixelFormat.Bgra8, 2, 2, BitmapAlphaMode.Premultiplied);
		var clipboard = new TestClipboard { Image = image, FailWrites = 2 };
		var manager = new TestableOcrManager(CreateValidSettings(), new StubOcrService("recognised text"), clipboard);

		var result = await manager.ExtractTextFromClipboardImageAsync(DefaultOrder);

		Assert.True(result.Success);
		Assert.False(result.ClipboardCopyFailed);
		Assert.Equal(3, clipboard.SetTextCalls);
		Assert.Equal("recognised text", clipboard.LastText);
	}

	/// <summary>
	/// And when it never lets go, the run says the copy failed rather than that the OCR did.
	/// The recognised text is the thing the user waited for and it is returned either way — the
	/// caller puts it in the OCR box, where the shortcut that runs next can read it.
	/// </summary>
	[Fact]
	public async Task A_clipboard_that_never_opens_is_a_failed_copy_and_not_a_failed_read()
	{
		using var image = new SoftwareBitmap(BitmapPixelFormat.Bgra8, 2, 2, BitmapAlphaMode.Premultiplied);
		var clipboard = new TestClipboard { Image = image, FailWrites = int.MaxValue };
		var manager = new TestableOcrManager(CreateValidSettings(), new StubOcrService("recognised text"), clipboard);

		var result = await manager.ExtractTextFromClipboardImageAsync(DefaultOrder);

		// The read succeeded, so the run did. The recognised text is still what comes back, and
		// the caller still puts it in the OCR box where the shortcut that runs next can read it
		// — the copy failing is a separate thing to say, not a reason to throw the text away and
		// show a COM error in its place.
		Assert.True(result.Success);
		Assert.True(result.ClipboardCopyFailed);
		Assert.Equal("recognised text", result.Message);
		Assert.Equal(ClipboardRetry.DefaultAttempts, clipboard.SetTextCalls);
	}

	/// <summary>
	/// A run that recognised nothing had nothing to copy, and must not report the copy that
	/// never happened as one that failed.
	/// </summary>
	[Fact]
	public async Task A_run_with_no_text_does_not_claim_the_copy_failed()
	{
		using var image = new SoftwareBitmap(BitmapPixelFormat.Bgra8, 2, 2, BitmapAlphaMode.Premultiplied);
		var clipboard = new TestClipboard { Image = image, FailWrites = int.MaxValue };
		var manager = new TestableOcrManager(CreateValidSettings(), new StubOcrService(string.Empty), clipboard);

		var result = await manager.ExtractTextFromClipboardImageAsync(DefaultOrder);

		Assert.False(result.ClipboardCopyFailed);
		Assert.Equal(0, clipboard.SetTextCalls);
	}

	/// <summary>
	/// A batch says so too. Forty pages announced as "Results copied to the clipboard" when
	/// they are not is the same lie, told to the user who waited longest for them.
	/// </summary>
	[Fact]
	public async Task A_batch_that_could_not_be_copied_says_so_as_well()
	{
		var clipboard = new TestClipboard { FailWrites = int.MaxValue };
		using var file = new TempFile(".png");
		var manager = new TestableOcrManager(CreateValidSettings(), new StubOcrService("recognised text"), clipboard);

		var result = await manager.ExtractTextFromFilesAsync(new[] { file.Path }, DefaultOrder, CancellationToken.None);

		Assert.True(result.Success);
		Assert.True(result.ClipboardCopyFailed);
		Assert.Contains("recognised text", result.Text, StringComparison.Ordinal);
	}

	/// <summary>
	/// Pressing an OCR shortcut while a capture is already on screen is refused rather than
	/// failed. The distinction is the whole fix: the caller reads it to decide whether to send
	/// the configured shortcut, and used to have nothing to read but the message text
	/// (issue #342).
	/// <para>
	/// Driven through the real method. It used to assert on a helper that returned the refusal,
	/// which proved the helper's contents and nothing about the guard that is supposed to use
	/// it — the branch could have been changed with the test still green (issue #365). Holding
	/// the first press inside the capture is what having an overlay on screen amounts to here,
	/// so the second press meets a genuinely busy manager.
	/// </para>
	/// </summary>
	// The timeout is the safety net for the regression this test exists to catch. Weakening the
	// guard fails the assertion below, but removing it altogether sends the second press into
	// the held capture, where it waits on a release that only the end of this test performs —
	// so without a bound the suite hangs instead of going red, which is far worse in a build
	// that nobody is watching.
	[Fact(Timeout = 20000)]
	public async Task A_second_capture_while_one_is_open_is_refused_rather_than_failed()
	{
		using var image = new SoftwareBitmap(BitmapPixelFormat.Bgra8, 2, 2, BitmapAlphaMode.Premultiplied);
		var ocrService = new StubOcrService("recognised text");
		var clipboard = new TestClipboard();
		var manager = new TestableOcrManager(CreateValidSettings(), ocrService, clipboard)
		{
			Screenshot = image,
			HoldCapture = true,
		};

		var firstPress = manager.TakeScreenshotAndExtractTextAsync(DefaultOrder);
		await manager.CaptureStarted.Task;

		var result = await manager.TakeScreenshotAndExtractTextAsync(DefaultOrder);

		Assert.False(result.Success);
		Assert.Equal(OcrRunOutcome.Refused, result.Outcome);
		Assert.False(PostOperationHotkey.ShouldSendAfterOcr(result.Outcome));

		// Refused means nothing ran, not that something ran and failed: no reading was asked
		// for, nothing was written to the clipboard, and no beep claimed an outcome. The
		// sentence in the OCR box is all the user gets, which is why it says what it says.
		Assert.Equal(ClipboardCopyMessages.OcrCaptureAlreadyInProgress, result.Message);
		Assert.Equal(0, ocrService.CallCount);
		Assert.Empty(clipboard.WriteThreadIds);
		Assert.Equal(0, manager.BeepCount);

		// And the run the user already had is unharmed by the press that was refused.
		manager.ReleaseCapture.SetResult();
		var firstResult = await firstPress;
		Assert.True(firstResult.Success);
		Assert.Equal(1, ocrService.CallCount);
	}

	/// <summary>
	/// Nothing was allowed in front of <c>Cancelled</c> when the refused value was added.
	/// A value nobody set has to mean "nothing happened"; if the zero value ever became
	/// <c>Copied</c>, an unassigned outcome would announce a screenshot that was never taken.
	/// </summary>
	[Fact]
	public void The_default_screenshot_outcome_still_claims_nothing()
	{
		Assert.Equal(ScreenshotToClipboardOutcome.Cancelled, default(ScreenshotToClipboardOutcome));
	}

	/// <summary>
	/// Every other unsuccessful run still sends it. An error in the OCR box wants reading as
	/// much as a result does, and narrowing that to "only successes" would be a worse bug than
	/// the one being fixed.
	/// </summary>
	[Fact]
	public async Task An_ordinary_OCR_failure_still_gets_the_shortcut()
	{
		var settings = CreateValidSettings();
		var manager = new TestableOcrManager(settings, new StubOcrService(), new TestClipboard());

		var result = await manager.ExtractTextFromClipboardImageAsync(DefaultOrder);

		Assert.False(result.Success);
		Assert.Equal(OcrRunOutcome.Answered, result.Outcome);
		Assert.True(PostOperationHotkey.ShouldSendAfterOcr(result.Outcome));
	}

	/// <summary>
	/// Batch OCR reads its files on the thread pool and finishes there, so the copy at the end
	/// of the run is made from the wrong thread unless something moves it. That something is now
	/// the clipboard itself rather than a second copy of the rule kept here, so what this checks
	/// is that the OCR path goes through it and the text still lands on the UI thread (issue
	/// #352).
	/// </summary>
	[Fact]
	public async Task ExtractTextFromFilesAsync_PutsTheTextOnTheClipboardFromTheUiThread()
	{
		var settings = CreateValidSettings();
		using var uiThread = new SingleThreadUiDispatcher();
		var clipboard = new TestClipboard(uiThread);
		var service = new StubOcrService("batched result");
		using var file = new TempFile(".png");
		var manager = new TestableOcrManager(settings, service, clipboard);

		var result = await Task.Run(() =>
			manager.ExtractTextFromFilesAsync(new[] { file.Path }, DefaultOrder, CancellationToken.None));

		string expectedText = $"[{Path.GetFileName(file.Path)}]{Environment.NewLine}batched result{Environment.NewLine}";
		Assert.True(result.Success);
		Assert.Equal(expectedText, clipboard.LastText);
		Assert.Equal(1, clipboard.SetTextCalls);
		Assert.Equal(new[] { uiThread.ThreadId }, clipboard.WriteThreadIds);
		WaitForBeep(manager, 1);
		Assert.Contains(BeepType.Success, manager.Beeps);
	}

	[Fact]
	public async Task ExtractTextFromFilesAsync_ProcessesMultipleFiles_WithMixedSuccess()
	{
		var settings = CreateValidSettings();
		var clipboard = new TestClipboard();
		var failure = new InvalidOperationException("OCR failed");
		var service = new StubOcrService("first text", failure);
		using var first = new TempFile(".png");
		using var second = new TempFile(".jpg");
		var manager = new TestableOcrManager(settings, service, clipboard);

		var result = await manager.ExtractTextFromFilesAsync(new[] { first.Path, second.Path }, DefaultOrder, CancellationToken.None);

		string expectedText = $"[{Path.GetFileName(first.Path)}]{Environment.NewLine}first text{Environment.NewLine}";
		Assert.False(result.Success);
		Assert.Equal(2, result.TotalCount);
		Assert.Equal(1, result.SuccessCount);
		Assert.Equal(expectedText, result.Text);
		Assert.Single(result.Failures);
		Assert.Contains(Path.GetFileName(second.Path), result.Failures[0]);
		Assert.Contains("OCR failed", result.Failures[0]);
		Assert.Equal(expectedText, clipboard.LastText);
		Assert.Equal(1, clipboard.SetTextCalls);
		WaitForBeep(manager, 1);
		Assert.Contains(BeepType.Failure, manager.Beeps);
		Assert.Equal(2, service.CallCount);
	}

	[Fact]
	public async Task ExtractTextFromFilesAsync_HandlesPdfFiles_WithMultiplePages()
	{
		var settings = CreateValidSettings();
		var clipboard = new TestClipboard();
		var service = new StubOcrService("page one", "page two");
		using var pdf = new TempPdf(2);
		var manager = new TestableOcrManager(settings, service, clipboard);

		var result = await manager.ExtractTextFromFilesAsync(new[] { pdf.Path }, DefaultOrder, CancellationToken.None);

		string expectedText = $"[{Path.GetFileName(pdf.Path)}]{Environment.NewLine}(Page 1){Environment.NewLine}page one{Environment.NewLine}{Environment.NewLine}(Page 2){Environment.NewLine}page two{Environment.NewLine}";
		Assert.True(result.Success);
		Assert.Equal(1, result.TotalCount);
		Assert.Equal(1, result.SuccessCount);
		Assert.Equal(expectedText, result.Text);
		Assert.Empty(result.Failures);
		Assert.Equal(expectedText, clipboard.LastText);
		Assert.Equal(1, clipboard.SetTextCalls);
		WaitForBeep(manager, 1);
		Assert.Contains(BeepType.Success, manager.Beeps);
		Assert.Equal(2, service.CallCount);
	}

	[Fact]
	public async Task ExtractTextFromFilesAsync_HandlesPdfWithNoPages()
	{
		var settings = CreateValidSettings();
		var clipboard = new TestClipboard();
		var service = new StubOcrService();
		using var pdf = new TempPdf(0);
		var manager = new TestableOcrManager(settings, service, clipboard);

		var result = await manager.ExtractTextFromFilesAsync(new[] { pdf.Path }, DefaultOrder, CancellationToken.None);

		Assert.False(result.Success);
		Assert.Equal(1, result.TotalCount);
		Assert.Equal(0, result.SuccessCount);
		Assert.Empty(result.Text);
		Assert.Single(result.Failures);
		Assert.Contains("PDF contains no pages", result.Failures[0], StringComparison.OrdinalIgnoreCase);
		Assert.Equal(0, clipboard.SetTextCalls);
		Assert.Equal(0, service.CallCount);
		WaitForBeep(manager, 1);
		Assert.Contains(BeepType.Failure, manager.Beeps);
	}

	[Fact]
        public async Task ExtractTextFromFilesAsync_HandlesInvalidPdf()
        {
                var settings = CreateValidSettings();
                var clipboard = new TestClipboard();
                var service = new StubOcrService();
                using var invalidPdf = new TempFile(".pdf", "not a real pdf");
                var manager = new TestableOcrManager(settings, service, clipboard);

                var result = await manager.ExtractTextFromFilesAsync(new[] { invalidPdf.Path }, DefaultOrder, CancellationToken.None);

                Assert.False(result.Success);
                Assert.Equal(1, result.TotalCount);
                Assert.Equal(0, result.SuccessCount);
                Assert.Empty(result.Text);
                Assert.Single(result.Failures);
                Assert.Contains(Path.GetFileName(invalidPdf.Path), result.Failures[0]);
                Assert.Equal(0, clipboard.SetTextCalls);
                Assert.Equal(0, service.CallCount);
                WaitForBeep(manager, 1);
                Assert.Contains(BeepType.Failure, manager.Beeps);
        }

        [Fact]
        public async Task ExtractTextFromFilesAsync_ReturnsFailure_ForUnsupportedExtension()
        {
                var settings = CreateValidSettings();
                var clipboard = new TestClipboard();
                var service = new StubOcrService();
                using var file = new TempFile(".txt");
                var manager = new TestableOcrManager(settings, service, clipboard);

                var result = await manager.ExtractTextFromFilesAsync(new[] { file.Path }, DefaultOrder, CancellationToken.None);

                Assert.False(result.Success);
                Assert.Equal(1, result.TotalCount);
                Assert.Equal(0, result.SuccessCount);
                Assert.Empty(result.Text);
                Assert.Single(result.Failures);
                Assert.Contains("unsupported file type", result.Failures[0], StringComparison.OrdinalIgnoreCase);
                Assert.Contains(Path.GetFileName(file.Path), result.Failures[0]);
                Assert.Equal(0, clipboard.SetTextCalls);
                Assert.Equal(0, service.CallCount);
                WaitForBeep(manager, 1);
                Assert.Contains(BeepType.Failure, manager.Beeps);
        }

        [Fact]
        public async Task ExtractTextFromFilesAsync_HandlesCancellation()
        {
                var settings = CreateValidSettings();
		var clipboard = new TestClipboard();
		using var first = new TempFile(".png");
		using var second = new TempFile(".png");
		var cts = new CancellationTokenSource();
		var service = new StubOcrService(new Func<Stream, CancellationToken, Task<string>>(async (_, token) =>
		{
			cts.Cancel();
			await Task.Yield();
			return "first";
		}));
		var manager = new TestableOcrManager(settings, service, clipboard);

		// ThrowsAny: a cancelled gate surfaces TaskCanceledException, a subclass of OperationCanceledException.
		await Assert.ThrowsAnyAsync<OperationCanceledException>(() => manager.ExtractTextFromFilesAsync(new[] { first.Path, second.Path }, DefaultOrder, cts.Token));

		Assert.Equal(0, clipboard.SetTextCalls);
		Assert.Equal(1, service.CallCount);
		Assert.Equal(0, manager.BeepCount);
	}

	// A cancel that lands after the last page finished but before the reduce used to fall
	// straight through to the success path and overwrite the clipboard — the one thing the
	// user is told cancelling never does.
	//
	// The cancel is raised from inside the OCR call, just before it hands back its text:
	// that guarantees the token is already cancelled when the last page completes, with no
	// scheduling assumption. The page itself succeeds, so nothing before the reduce has a
	// cancellation point left to trip on — only the final check can stop this run.
	[Fact]
	public async Task ExtractTextFromFilesAsync_DoesNotTouchTheClipboard_WhenCancelledAfterTheLastPage()
	{
		var settings = CreateValidSettings();
		var clipboard = new TestClipboard();
		using var only = new TempFile(".png");
		var cts = new CancellationTokenSource();
		var service = new StubOcrService(new Func<Stream, CancellationToken, Task<string>>((_, _) =>
		{
			cts.Cancel();
			return Task.FromResult("page text");
		}));
		var manager = new TestableOcrManager(settings, service, clipboard);

		await Assert.ThrowsAnyAsync<OperationCanceledException>(
			() => manager.ExtractTextFromFilesAsync(new[] { only.Path }, DefaultOrder, cts.Token));

		Assert.Equal(1, service.CallCount);
		Assert.Equal(0, clipboard.SetTextCalls);
	}

	[Fact]
	public async Task ExtractTextFromFilesAsync_ReportsProgress_Correctly()
	{
		var settings = CreateValidSettings();
		var clipboard = new TestClipboard();
		var service = new StubOcrService("page one", "page two");
		using var pdf = new TempPdf(2);
		var progress = new List<OcrProcessingProgress>();
		var manager = new TestableOcrManager(settings, service, clipboard);

		var result = await manager.ExtractTextFromFilesAsync(new[] { pdf.Path }, DefaultOrder, CancellationToken.None, new Progress<OcrProcessingProgress>(progress.Add));

		Assert.True(result.Success);
		Assert.Equal(2, progress.Count);
		Assert.Equal(1, progress[0].ProcessedSegments);
		Assert.Equal(2, progress[0].TotalSegments);
		Assert.Equal(Path.GetFileName(pdf.Path), progress[0].FileName);
		Assert.Equal(1, progress[0].PageNumber);
		Assert.Equal(2, progress[0].TotalPagesForFile);
		Assert.Equal(2, progress[1].ProcessedSegments);
		Assert.Equal(2, progress[1].TotalSegments);
		Assert.Equal(Path.GetFileName(pdf.Path), progress[1].FileName);
		Assert.Equal(2, progress[1].PageNumber);
		Assert.Equal(2, progress[1].TotalPagesForFile);
		WaitForBeep(manager, 1);
		Assert.Contains(BeepType.Success, manager.Beeps);
	}

	[Fact]
        public async Task ExtractTextFromFilesAsync_SkipsDuplicatePaths()
        {
                var settings = CreateValidSettings();
                var clipboard = new TestClipboard();
                var service = new StubOcrService("text");
                using var file = new TempFile(".png");
                var manager = new TestableOcrManager(settings, service, clipboard);

                var duplicatePaths = new[] { file.Path, file.Path.ToUpperInvariant() };
                var result = await manager.ExtractTextFromFilesAsync(duplicatePaths, DefaultOrder, CancellationToken.None);

                Assert.True(result.Success);
                Assert.Equal(1, result.TotalCount);
                Assert.Equal(1, result.SuccessCount);
                Assert.Equal(1, service.CallCount);
                WaitForBeep(manager, 1);
                Assert.Contains(BeepType.Success, manager.Beeps);
        }

        [Fact]
        public async Task ExtractTextFromFilesAsync_ProcessesAllSupportedFileTypes()
        {
                var settings = CreateValidSettings();
                var clipboard = new TestClipboard();
                using var png = new TempFile(".png");
                using var jpg = new TempFile(".jpg");
                using var bmp = new TempFile(".bmp");
                using var tif = new TempFile(".tiff");
                using var pdf = new TempPdf(1);
                var service = new StubOcrService("png text", "jpg text", "bmp text", "tiff text", "pdf text");
                var manager = new TestableOcrManager(settings, service, clipboard);

                var paths = new[] { png.Path, jpg.Path, bmp.Path, tif.Path, pdf.Path };
                var result = await manager.ExtractTextFromFilesAsync(paths, DefaultOrder, CancellationToken.None);

                Assert.True(result.Success);
                Assert.Equal(paths.Length, result.TotalCount);
                Assert.Equal(paths.Length, result.SuccessCount);
                Assert.Equal(paths.Length, service.CallCount);
                Assert.Contains("png text", result.Text, StringComparison.OrdinalIgnoreCase);
                Assert.Contains("jpg text", result.Text, StringComparison.OrdinalIgnoreCase);
                Assert.Contains("bmp text", result.Text, StringComparison.OrdinalIgnoreCase);
                Assert.Contains("tiff text", result.Text, StringComparison.OrdinalIgnoreCase);
                Assert.Contains("pdf text", result.Text, StringComparison.OrdinalIgnoreCase);
                WaitForBeep(manager, 1);
                Assert.Contains(BeepType.Success, manager.Beeps);
        }

	[Fact]
	public void SupportedFileExtensions_MatchExpectedSet()
	{
		string[] expected = { ".pdf", ".png", ".jpg", ".jpeg", ".bmp", ".tif", ".tiff" };
		Assert.Equal(expected, OcrManager.SupportedFileExtensions);
	}

	[Fact]
	public async Task ExtractTextFromFilesAsync_SplitsPdfOnly()
	{
		var settings = CreateValidSettings();
		var clipboard = new TestClipboard();
		using var pdf = new TempPdf(3);
		using var png = new TempFile(".png");
		using var jpeg = new TempFile(".jpeg");
		var service = new StubOcrService("pdf page one", "pdf page two", "pdf page three", "png text", "jpeg text");
		var manager = new TestableOcrManager(settings, service, clipboard);

		var result = await manager.ExtractTextFromFilesAsync(new[] { pdf.Path, png.Path, jpeg.Path }, DefaultOrder, CancellationToken.None);

		Assert.True(result.Success);
		Assert.Equal(3 + 2, service.CallCount);
		Assert.Contains("(Page 1)", result.Text, StringComparison.Ordinal);
		string pngHeader = $"[{Path.GetFileName(png.Path)}]";
		string jpegHeader = $"[{Path.GetFileName(jpeg.Path)}]";
		string pngSection = ExtractSection(result.Text, pngHeader);
		string jpegSection = ExtractSection(result.Text, jpegHeader);
		Assert.DoesNotContain("(Page", pngSection, StringComparison.Ordinal);
		Assert.DoesNotContain("(Page", jpegSection, StringComparison.Ordinal);
		Assert.Equal(result.Text, clipboard.LastText);
		Assert.Equal(1, clipboard.SetTextCalls);
		WaitForBeep(manager, 1);
		Assert.Contains(BeepType.Success, manager.Beeps);
	}

	[Fact]
	public async Task ExtractTextFromFilesAsync_ProcessesMixedSupportedAndUnsupportedFiles()
	{
		var settings = CreateValidSettings();
		var clipboard = new TestClipboard();
		using var png = new TempFile(".png");
		using var textFile = new TempFile(".txt");
		var service = new StubOcrService("png text");
		var manager = new TestableOcrManager(settings, service, clipboard);

		var result = await manager.ExtractTextFromFilesAsync(new[] { png.Path, textFile.Path }, DefaultOrder, CancellationToken.None);

		Assert.False(result.Success);
		Assert.Equal(2, result.TotalCount);
		Assert.Equal(1, result.SuccessCount);
		Assert.Single(result.Failures);
		Assert.Contains(Path.GetFileName(textFile.Path), result.Failures[0], StringComparison.OrdinalIgnoreCase);
		Assert.Contains("unsupported", result.Failures[0], StringComparison.OrdinalIgnoreCase);
		Assert.Contains("png text", result.Text, StringComparison.OrdinalIgnoreCase);
		Assert.Equal(1, clipboard.SetTextCalls);
		Assert.Equal(1, service.CallCount);
		WaitForBeep(manager, 1);
		Assert.Contains(BeepType.Failure, manager.Beeps);
	}


	[Fact]
	public async Task ExtractTextFromFilesAsync_ProcessesEverySupportedImageTypeWithoutSplitting()
	{
		var settings = CreateValidSettings();
		var clipboard = new TestClipboard();
		using var png = new TempFile(".png");
		using var jpg = new TempFile(".jpg");
		using var jpeg = new TempFile(".jpeg");
		using var bmp = new TempFile(".bmp");
		using var tif = new TempFile(".tif");
		using var tiff = new TempFile(".tiff");
		var service = new StubOcrService("png text", "jpg text", "jpeg text", "bmp text", "tif text", "tiff text");
		var manager = new TestableOcrManager(settings, service, clipboard);

		string[] paths = { png.Path, jpg.Path, jpeg.Path, bmp.Path, tif.Path, tiff.Path };
		var result = await manager.ExtractTextFromFilesAsync(paths, DefaultOrder, CancellationToken.None);

		Assert.True(result.Success);
		Assert.Equal(paths.Length, result.TotalCount);
		Assert.Equal(paths.Length, result.SuccessCount);
		Assert.Equal(paths.Length, service.CallCount);

		foreach (string path in paths)
		{
			string header = $"[{Path.GetFileName(path)}]";
			string section = ExtractSection(result.Text, header);
			Assert.Contains(header, result.Text, StringComparison.Ordinal);
			Assert.DoesNotContain("(Page", section, StringComparison.Ordinal);
		}

		Assert.Equal(result.Text, clipboard.LastText);
		Assert.Equal(1, clipboard.SetTextCalls);
		WaitForBeep(manager, 1);
		Assert.Contains(BeepType.Success, manager.Beeps);
	}

	[Fact]
	public async Task ExtractTextFromFilesAsync_ProcessesMixedFilesInSingleSelection()
	{
		var settings = CreateValidSettings();
		var clipboard = new TestClipboard();
		using var pdf = new TempPdf(2);
		using var png = new TempFile(".png");
		using var jpeg = new TempFile(".jpeg");
		var service = new StubOcrService("pdf page one", "pdf page two", "png text", "jpeg text");
		var manager = new TestableOcrManager(settings, service, clipboard);

		var result = await manager.ExtractTextFromFilesAsync(new[] { pdf.Path, png.Path, jpeg.Path }, DefaultOrder, CancellationToken.None);

		Assert.True(result.Success);
		Assert.Equal(3, result.TotalCount);
		Assert.Equal(3, result.SuccessCount);
		Assert.Equal(4, service.CallCount);

		string pdfHeader = $"[{Path.GetFileName(pdf.Path)}]";
		string pngHeader = $"[{Path.GetFileName(png.Path)}]";
		string jpegHeader = $"[{Path.GetFileName(jpeg.Path)}]";
		string pdfSection = ExtractSection(result.Text, pdfHeader);
		string pngSection = ExtractSection(result.Text, pngHeader);
		string jpegSection = ExtractSection(result.Text, jpegHeader);

		Assert.Contains("(Page 1)", pdfSection, StringComparison.Ordinal);
		Assert.Contains("(Page 2)", pdfSection, StringComparison.Ordinal);
		Assert.DoesNotContain("(Page", pngSection, StringComparison.Ordinal);
		Assert.DoesNotContain("(Page", jpegSection, StringComparison.Ordinal);

		Assert.Equal(result.Text, clipboard.LastText);
		Assert.Equal(1, clipboard.SetTextCalls);
		WaitForBeep(manager, 1);
		Assert.Contains(BeepType.Success, manager.Beeps);
	}

	[Fact]
	public async Task ExtractTextFromFilesAsync_HandlesOddPagePdfSuccessfully()
	{
		var settings = CreateValidSettings();
		var clipboard = new TestClipboard();
		using var pdf = new TempPdf(3);
		var service = new StubOcrService("odd page one", "odd page two", "odd page three");
		var manager = new TestableOcrManager(settings, service, clipboard);

		var result = await manager.ExtractTextFromFilesAsync(new[] { pdf.Path }, DefaultOrder, CancellationToken.None);

		Assert.True(result.Success);
		Assert.Equal(1, result.TotalCount);
		Assert.Equal(1, result.SuccessCount);
		Assert.Equal(3, service.CallCount);

		string header = $"[{Path.GetFileName(pdf.Path)}]";
		string section = ExtractSection(result.Text, header);
		Assert.Contains("(Page 1)", section, StringComparison.Ordinal);
		Assert.Contains("(Page 2)", section, StringComparison.Ordinal);
		Assert.Contains("(Page 3)", section, StringComparison.Ordinal);
		Assert.Contains("odd page one", section, StringComparison.OrdinalIgnoreCase);
		Assert.Contains("odd page two", section, StringComparison.OrdinalIgnoreCase);
		Assert.Contains("odd page three", section, StringComparison.OrdinalIgnoreCase);

		Assert.Equal(result.Text, clipboard.LastText);
		Assert.Equal(1, clipboard.SetTextCalls);
		WaitForBeep(manager, 1);
		Assert.Contains(BeepType.Success, manager.Beeps);
	}

	[Fact]
	public async Task ExtractTextFromFilesAsync_SkipsFile_ExceedingMaxDocumentBytes()
	{
		var settings = CreateValidSettings();
		settings.AzureComputerVisionSettings!.MaxDocumentBytes = 10; // 10-byte cap
		var clipboard = new TestClipboard();
		var service = new StubOcrService("should not run");
		using var file = new TempFile(".png", new string('x', 50)); // 50 bytes > cap
		var manager = new TestableOcrManager(settings, service, clipboard);

		var result = await manager.ExtractTextFromFilesAsync(new[] { file.Path }, DefaultOrder, CancellationToken.None);

		Assert.False(result.Success);
		Assert.Equal(1, result.TotalCount);
		Assert.Equal(0, result.SuccessCount);
		Assert.Single(result.Failures);
		Assert.Contains("maximum document size", result.Failures[0], StringComparison.OrdinalIgnoreCase);
		Assert.Contains(Path.GetFileName(file.Path), result.Failures[0]);
		Assert.Equal(0, service.CallCount); // OCR never invoked for the oversized file
		Assert.Equal(0, clipboard.SetTextCalls);
		WaitForBeep(manager, 1);
		Assert.Contains(BeepType.Failure, manager.Beeps);
	}

	[Fact]
	public async Task ExtractTextFromFilesAsync_ProcessesLargeFile_WhenMaxDocumentBytesIsZero()
	{
		var settings = CreateValidSettings();
		settings.AzureComputerVisionSettings!.MaxDocumentBytes = 0; // 0 = no limit
		var clipboard = new TestClipboard();
		var service = new StubOcrService("recognized text");
		using var file = new TempFile(".png", new string('x', 50));
		var manager = new TestableOcrManager(settings, service, clipboard);

		var result = await manager.ExtractTextFromFilesAsync(new[] { file.Path }, DefaultOrder, CancellationToken.None);

		Assert.True(result.Success);
		Assert.Equal(1, result.SuccessCount);
		Assert.Equal(1, service.CallCount);
		WaitForBeep(manager, 1);
		Assert.Contains(BeepType.Success, manager.Beeps);
	}

	[Fact]
	public async Task ExtractTextFromFilesAsync_HonoursMaxParallelRequests()
	{
		var settings = CreateValidSettings();
		// Document gate wide open so the request gate is the only binding constraint.
		settings.AzureComputerVisionSettings!.MaxParallelDocuments = 100;
		settings.AzureComputerVisionSettings!.MaxParallelRequests = 2;
		var clipboard = new TestClipboard();
		// Calls are released in pairs, so the overlap is enforced rather than hoped for
		// inside a delay window.
		var service = new ConcurrencyTrackingOcrService(rendezvousSize: 2);
		var files = Enumerable.Range(0, 8).Select(_ => new TempFile(".png")).ToList();
		try
		{
			var manager = new TestableOcrManager(settings, service, clipboard);

			var result = await manager.ExtractTextFromFilesAsync(files.Select(f => f.Path), DefaultOrder, CancellationToken.None);

			Assert.True(result.Success);
			Assert.Equal(8, service.CallCount);
			Assert.False(service.RendezvousTimedOut, "Two OCR calls never ran at once; the throttle admitted fewer than 2 concurrently.");
			Assert.Equal(2, service.MaxConcurrency);
		}
		finally
		{
			foreach (var file in files)
				file.Dispose();
		}
	}

	[Fact]
	public async Task ExtractTextFromFilesAsync_HonoursMaxParallelDocuments()
	{
		var settings = CreateValidSettings();
		settings.AzureComputerVisionSettings!.MaxParallelDocuments = 2;
		// Request gate wide open so the document gate is the only binding constraint.
		settings.AzureComputerVisionSettings!.MaxParallelRequests = 100;
		var clipboard = new TestClipboard();
		var service = new ConcurrencyTrackingOcrService(rendezvousSize: 2);
		var files = Enumerable.Range(0, 8).Select(_ => new TempFile(".png")).ToList();
		try
		{
			var manager = new TestableOcrManager(settings, service, clipboard);

			var result = await manager.ExtractTextFromFilesAsync(files.Select(f => f.Path), DefaultOrder, CancellationToken.None);

			Assert.True(result.Success);
			Assert.Equal(8, service.CallCount);
			// Each single-page file makes exactly one OCR call while holding the document gate,
			// so concurrent OCR calls equal concurrent documents here.
			Assert.False(service.RendezvousTimedOut, "Two documents never ran at once; the throttle admitted fewer than 2 concurrently.");
			Assert.Equal(2, service.MaxConcurrency);
		}
		finally
		{
			foreach (var file in files)
				file.Dispose();
		}
	}

	[Fact]
	public async Task ExtractTextFromFilesAsync_FreeTier_ProcessesEveryPageInOrder()
	{
		var settings = CreateValidSettings();
		settings.AzureComputerVisionSettings!.UseFreeTier = true;
		settings.AzureComputerVisionSettings!.FreeTierPageLimit = 2;
		var clipboard = new TestClipboard();
		var service = new StubOcrService(Enumerable.Range(1, 9).Select(i => (object)$"page {i}").ToArray());
		using var pdf = new TempPdf(9);
		var manager = new TestableOcrManager(settings, service, clipboard);

		var result = await manager.ExtractTextFromFilesAsync(new[] { pdf.Path }, DefaultOrder, CancellationToken.None);

		Assert.True(result.Success);
		// Recommended per-page model: 9 pages produce exactly 9 OCR requests, nothing truncated.
		Assert.Equal(9, service.CallCount);

		string section = ExtractSection(result.Text, $"[{Path.GetFileName(pdf.Path)}]");
		int previousIndex = -1;
		for (int page = 1; page <= 9; page++)
		{
			string label = $"(Page {page})";
			int index = section.IndexOf(label, StringComparison.Ordinal);
			Assert.True(index >= 0, $"Page {page} is missing from the output.");
			Assert.True(index > previousIndex, $"Page {page} is out of order.");
			previousIndex = index;
		}

		// (No "nothing was truncated" string assertion here: the output text comes from
		// the stub, and OcrManager has no truncation notice to look for. The 9 requests
		// and the 9 ordered page labels above are what prove nothing was dropped.)
	}

	[Fact]
	public async Task ExtractTextFromFilesAsync_FreeTierDisabled_ProcessesEveryPage()
	{
		var settings = CreateValidSettings();
		settings.AzureComputerVisionSettings!.UseFreeTier = false;
		var clipboard = new TestClipboard();
		var service = new StubOcrService(Enumerable.Range(1, 9).Select(i => (object)$"page {i}").ToArray());
		using var pdf = new TempPdf(9);
		var manager = new TestableOcrManager(settings, service, clipboard);

		var result = await manager.ExtractTextFromFilesAsync(new[] { pdf.Path }, DefaultOrder, CancellationToken.None);

		Assert.True(result.Success);
		Assert.Equal(9, service.CallCount);

		string section = ExtractSection(result.Text, $"[{Path.GetFileName(pdf.Path)}]");
		for (int page = 1; page <= 9; page++)
			Assert.Contains($"(Page {page})", section, StringComparison.Ordinal);
	}

	[Fact]
	public async Task ExtractTextFromFilesAsync_RequestGate_HoldsAcrossManyWorkItems()
	{
		var settings = CreateValidSettings();
		settings.AzureComputerVisionSettings!.MaxParallelDocuments = 8;
		settings.AzureComputerVisionSettings!.MaxParallelRequests = 2;
		var clipboard = new TestClipboard();
		var service = new ConcurrencyTrackingOcrService(delayMs: 30);
		var pdfs = new[] { new TempPdf(4), new TempPdf(4), new TempPdf(4) };
		try
		{
			var manager = new TestableOcrManager(settings, service, clipboard);

			var result = await manager.ExtractTextFromFilesAsync(pdfs.Select(p => p.Path), DefaultOrder, CancellationToken.None);

			Assert.True(result.Success);
			Assert.Equal(12, service.CallCount);
			// The global request gate must hold even when many pages across documents are queued.
			Assert.True(service.MaxConcurrency <= 2, $"Observed {service.MaxConcurrency} concurrent OCR calls; expected at most 2.");
		}
		finally
		{
			foreach (var pdf in pdfs)
				pdf.Dispose();
		}
	}

	[Fact]
	public async Task ExtractTextFromFilesAsync_ParallelRun_ProducesDeterministicOrderedOutput()
	{
		var settings = CreateValidSettings();
		settings.AzureComputerVisionSettings!.MaxParallelDocuments = 4;
		settings.AzureComputerVisionSettings!.MaxParallelRequests = 4;

		using var pdf = new TempPdf(3);
		using var png = new TempFile(".png");
		using var jpeg = new TempFile(".jpeg");
		using var bmp = new TempFile(".bmp");
		var paths = new[] { pdf.Path, png.Path, jpeg.Path, bmp.Path };

		string? expected = null;
		for (int run = 0; run < 5; run++)
		{
			var clipboard = new TestClipboard();
			var service = new ConcurrencyTrackingOcrService(delayMs: 10);
			var manager = new TestableOcrManager(settings, service, clipboard);

			var result = await manager.ExtractTextFromFilesAsync(paths, DefaultOrder, CancellationToken.None);

			Assert.True(result.Success);
			expected ??= result.Text;
			Assert.Equal(expected, result.Text);

			int pdfIndex = result.Text.IndexOf($"[{Path.GetFileName(pdf.Path)}]", StringComparison.Ordinal);
			int pngIndex = result.Text.IndexOf($"[{Path.GetFileName(png.Path)}]", StringComparison.Ordinal);
			int jpegIndex = result.Text.IndexOf($"[{Path.GetFileName(jpeg.Path)}]", StringComparison.Ordinal);
			int bmpIndex = result.Text.IndexOf($"[{Path.GetFileName(bmp.Path)}]", StringComparison.Ordinal);
			Assert.True(pdfIndex < pngIndex && pngIndex < jpegIndex && jpegIndex < bmpIndex, "Files are not in original selection order.");
		}
	}

	private static string ExtractSection(string text, string header)
	{
		int start = text.IndexOf(header, StringComparison.Ordinal);
		Assert.True(start >= 0, $"Header '{header}' not found.");
		string segment = text[start..];
		// Files are separated by 3 newlines (1 from AppendLine(text), 2 from AppendLine().AppendLine() at start of next loop)
		// Pages are separated by 2 newlines.
		string separator = $"{Environment.NewLine}{Environment.NewLine}{Environment.NewLine}";
		int separatorIndex = segment.IndexOf(separator, StringComparison.Ordinal);
		if (separatorIndex >= 0)
		{
			segment = segment[..separatorIndex];
		}
		return segment;
	}

	// The batch gates default to 2 documents / 4 requests in production. Most tests here
	// map the Nth stub response to the Nth file positionally, which only holds under
	// sequential dispatch, so the shared fixture pins both to one. The throttles
	// themselves have dedicated tests that set the knobs explicitly.
	private static Settings CreateValidSettings() => new()
	{
		AzureComputerVisionSettings = new AzureComputerVisionSettings
		{
			ApiKey = "key",
			Endpoint = "https://example.com",
			MaxParallelDocuments = 1,
			MaxParallelRequests = 1
		}
	};

	// Issue #268: the end beep used to sound here as a side effect of OcrService's retry
	// signal firing on attempt 1. #216 silenced that first attempt, which left OCR with
	// no end beep at all, and #269 restored it only for dictation.
	//
	// This is the clipboard path: no region overlay, so no start beep — the user hears
	// end (image sent) then success. The screenshot path adds a start beep in front,
	// from the overlay.
	[Fact]
	public async Task ExtractTextFromClipboardImageAsync_BeepsEndWhenTheRequestGoesOut_ThenSuccess()
	{
		using var image = new SoftwareBitmap(BitmapPixelFormat.Bgra8, 2, 2, BitmapAlphaMode.Premultiplied);
		var clipboard = new TestClipboard { Image = image };
		var manager = new TestableOcrManager(CreateValidSettings(), new StubOcrService("recognised text"), clipboard);

		var result = await manager.ExtractTextFromClipboardImageAsync(DefaultOrder);

		Assert.True(result.Success);
		WaitForBeep(manager, 2);
		Assert.Equal(new[] { BeepType.End, BeepType.Success }, manager.Beeps.ToArray());
	}

	// The end beep says "the request went out", so a request that never went out must
	// not claim one — the user would be told the image was sent when it was not.
	[Fact]
	public async Task ExtractTextFromClipboardImageAsync_NoEndBeep_WhenOcrIsNotConfigured()
	{
		using var image = new SoftwareBitmap(BitmapPixelFormat.Bgra8, 2, 2, BitmapAlphaMode.Premultiplied);
		var clipboard = new TestClipboard { Image = image };
		var manager = new TestableOcrManager(new Settings(), new StubOcrService(), clipboard);

		var result = await manager.ExtractTextFromClipboardImageAsync(DefaultOrder);

		Assert.False(result.Success);
		WaitForBeep(manager, 1);
		Assert.Equal(new[] { BeepType.Failure }, manager.Beeps.ToArray());
	}

	[Fact]
	public async Task ExtractTextFromClipboardImageAsync_NoEndBeep_WhenThereIsNoImage()
	{
		var manager = new TestableOcrManager(CreateValidSettings(), new StubOcrService(), new TestClipboard());

		var result = await manager.ExtractTextFromClipboardImageAsync(DefaultOrder);

		Assert.False(result.Success);
		WaitForBeep(manager, 1);
		Assert.Equal(new[] { BeepType.Failure }, manager.Beeps.ToArray());
	}

	// A failing request still ended in the image being sent, so the end beep stands and
	// the failure beep follows it.
	[Fact]
	public async Task ExtractTextFromClipboardImageAsync_BeepsEndThenFailure_WhenTheRequestFails()
	{
		using var image = new SoftwareBitmap(BitmapPixelFormat.Bgra8, 2, 2, BitmapAlphaMode.Premultiplied);
		var clipboard = new TestClipboard { Image = image };
		var service = new StubOcrService(new InvalidOperationException("service unavailable"));
		var manager = new TestableOcrManager(CreateValidSettings(), service, clipboard);

		var result = await manager.ExtractTextFromClipboardImageAsync(DefaultOrder);

		Assert.False(result.Success);
		WaitForBeep(manager, 2);
		Assert.Equal(new[] { BeepType.End, BeepType.Failure }, manager.Beeps.ToArray());
	}

	// The batch path issues one request per segment; a beep each would be a burst, and
	// it reports progress of its own.
	[Fact]
	public async Task ExtractTextFromFilesAsync_DoesNotBeepEndPerRequest()
	{
		using var first = new TempFile(".png");
		using var second = new TempFile(".jpg");
		var manager = new TestableOcrManager(
			CreateValidSettings(), new StubOcrService("first", "second"), new TestClipboard());

		var result = await manager.ExtractTextFromFilesAsync(
			new[] { first.Path, second.Path }, DefaultOrder, CancellationToken.None);

		Assert.True(result.Success);
		WaitForBeep(manager, 1);
		Assert.DoesNotContain(BeepType.End, manager.Beeps);
	}

	// ---------------------------------------------------------------------
	// The screenshot write to the clipboard (issue #360)
	//
	// This is the widest clipboard race in the app: the picture arriving is the very thing
	// that makes a clipboard manager or a screen reader open the clipboard to look. The write
	// used to get one attempt and report failure by throwing, which reached the hotkey handler
	// as an error dialog. It now retries like every other clipboard write here, and says which
	// of the three things happened.
	// ---------------------------------------------------------------------

	[Fact]
	public async Task TakeScreenshotToClipboardAsync_CopiesTheImage_WhenTheClipboardIsBrieflyBusy()
	{
		using var image = new SoftwareBitmap(BitmapPixelFormat.Bgra8, 2, 2, BitmapAlphaMode.Premultiplied);
		var clipboard = new TestClipboard { FailWrites = 2 };
		var manager = new TestableOcrManager(CreateValidSettings(), new StubOcrService(), clipboard)
		{
			Screenshot = image,
		};

		var outcome = await manager.TakeScreenshotToClipboardAsync();

		Assert.Equal(ScreenshotToClipboardOutcome.Copied, outcome);
		Assert.Same(image, clipboard.LastImage);
		Assert.Equal(3, clipboard.WriteThreadIds.Count);
		WaitForBeep(manager, 1);
		Assert.Equal(new[] { BeepType.Success }, manager.Beeps.ToArray());
	}

	/// <summary>
	/// The clipboard never letting go is now an answer rather than an exception, and the answer
	/// says which of the three it was. Reporting it as a cancellation would tell the user they
	/// cancelled a capture they did not, and throwing is what put an error dialog in front of
	/// them.
	/// </summary>
	[Fact]
	public async Task TakeScreenshotToClipboardAsync_ReportsClipboardUnavailable_WhenTheClipboardNeverLetsGo()
	{
		using var image = new SoftwareBitmap(BitmapPixelFormat.Bgra8, 2, 2, BitmapAlphaMode.Premultiplied);
		var clipboard = new TestClipboard { FailWrites = int.MaxValue };
		var manager = new TestableOcrManager(CreateValidSettings(), new StubOcrService(), clipboard)
		{
			Screenshot = image,
		};

		var outcome = await manager.TakeScreenshotToClipboardAsync();

		Assert.Equal(ScreenshotToClipboardOutcome.ClipboardUnavailable, outcome);
		Assert.Null(clipboard.LastImage);
		Assert.Equal(ClipboardRetry.DefaultAttempts, clipboard.WriteThreadIds.Count);
		WaitForBeep(manager, 1);
		Assert.Equal(new[] { BeepType.Failure }, manager.Beeps.ToArray());
	}

	/// <summary>
	/// A press that arrives while a capture is already running is refused, not cancelled. The
	/// two used to be the same value, so the user was told "Screenshot cancelled. Nothing was
	/// copied to the clipboard." about a capture that was still going (issue #363) — the
	/// opposite of the truth for someone who cannot see it.
	/// <para>
	/// Which refusal depends on whether an overlay is on screen, and both are exercised here
	/// because getting that wrong is what issue #367 was: the one sentence fitted to both told
	/// a user with no overlay to select a region that did not exist. The guard is held for far
	/// longer than the overlay lives, so the no-overlay case is the common one, not the corner.
	/// </para>
	/// <para>
	/// Driven through the real method rather than a seam: the first press is held inside the
	/// capture, so the second meets a genuinely busy manager. That also pins what the refused
	/// press does *not* do — it writes nothing to the clipboard and plays no beep, which is why
	/// the announcement is the only thing telling the user anything.
	/// </para>
	/// </summary>
	// Bounded for the same reason as the OCR-side refusal test above: remove the guard and this
	// one waits forever on a release it can no longer reach.
	[Theory(Timeout = 20000)]
	[InlineData(true, ScreenshotToClipboardOutcome.RefusedOverlayWaiting)]
	[InlineData(false, ScreenshotToClipboardOutcome.RefusedCaptureRunning)]
	public async Task TakeScreenshotToClipboardAsync_ReportsRefused_WhenACaptureIsAlreadyRunning(
		bool overlayOnScreen,
		ScreenshotToClipboardOutcome expected)
	{
		using var image = new SoftwareBitmap(BitmapPixelFormat.Bgra8, 2, 2, BitmapAlphaMode.Premultiplied);
		var clipboard = new TestClipboard();
		var manager = new TestableOcrManager(CreateValidSettings(), new StubOcrService(), clipboard)
		{
			Screenshot = image,
			HoldCapture = true,
			OverlayOnScreen = overlayOnScreen,
		};

		var firstPress = manager.TakeScreenshotToClipboardAsync();
		await manager.CaptureStarted.Task;

		var secondPress = await manager.TakeScreenshotToClipboardAsync();

		Assert.Equal(expected, secondPress);
		Assert.NotEqual(ScreenshotToClipboardOutcome.Cancelled, secondPress);
		Assert.Empty(clipboard.WriteThreadIds);
		Assert.Equal(0, manager.BeepCount);

		// And the capture the user already had is unharmed by the press that was refused.
		manager.ReleaseCapture.SetResult();
		Assert.Equal(ScreenshotToClipboardOutcome.Copied, await firstPress);
	}

	/// <summary>
	/// The refusal a user meets while a capture is running past its overlay must not offer them
	/// a control that is not there. This is the whole of issue #367: the sentence written for
	/// the overlay case asks for a region that does not exist, and the Escape it offers goes to
	/// whichever application actually holds the keyboard.
	/// </summary>
	[Fact(Timeout = 20000)]
	public async Task ARefusalWithNoOverlayIsAnnouncedWithoutAskingForARegion()
	{
		using var image = new SoftwareBitmap(BitmapPixelFormat.Bgra8, 2, 2, BitmapAlphaMode.Premultiplied);
		var manager = new TestableOcrManager(CreateValidSettings(), new StubOcrService(), new TestClipboard())
		{
			Screenshot = image,
			HoldCapture = true,
			OverlayOnScreen = false,
		};

		var firstPress = manager.TakeScreenshotToClipboardAsync();
		await manager.CaptureStarted.Task;

		var (message, severity) = ClipboardCopyMessages.ForScreenshot(await manager.TakeScreenshotToClipboardAsync());

		Assert.Equal(ClipboardCopyMessages.ScreenshotCaptureRunning, message);
		Assert.DoesNotContain("Escape", message);
		Assert.DoesNotContain("Select a region", message);
		Assert.Equal(InfoBarSeverity.Warning, severity);

		manager.ReleaseCapture.SetResult();
		await firstPress;
	}

	[Fact]
	public async Task TakeScreenshotToClipboardAsync_ReportsCancelled_WhenNothingWasCaptured()
	{
		var clipboard = new TestClipboard();
		var manager = new TestableOcrManager(CreateValidSettings(), new StubOcrService(), clipboard);

		var outcome = await manager.TakeScreenshotToClipboardAsync();

		Assert.Equal(ScreenshotToClipboardOutcome.Cancelled, outcome);
		Assert.Empty(clipboard.WriteThreadIds);
		WaitForBeep(manager, 1);
		Assert.Equal(new[] { BeepType.Failure }, manager.Beeps.ToArray());
	}

	/// <summary>
	/// The reading goes ahead when the picture cannot be copied. It works from the bitmap in
	/// hand and never needed the clipboard, so throwing here used to lose a capture the user
	/// cannot take again — whatever was on screen has moved on by then.
	/// <para>
	/// The double fails exactly as many writes as one ladder has rungs, so the picture's write
	/// fails outright and the text's write, which comes after it, succeeds. That is what
	/// separates the two flags: the text reached the clipboard and the picture did not.
	/// </para>
	/// </summary>
	[Fact]
	public async Task TakeScreenshotAndExtractTextAsync_StillReadsTheText_WhenTheClipboardWillNotTakeThePicture()
	{
		using var image = new SoftwareBitmap(BitmapPixelFormat.Bgra8, 2, 2, BitmapAlphaMode.Premultiplied);
		var clipboard = new TestClipboard { FailWrites = ClipboardRetry.DefaultAttempts };
		var manager = new TestableOcrManager(
			CreateValidSettings(), new StubOcrService("recognised text"), clipboard)
		{
			Screenshot = image,
		};

		var result = await manager.TakeScreenshotAndExtractTextAsync(DefaultOrder);

		Assert.True(result.Success);
		Assert.Equal("recognised text", result.Message);
		Assert.True(result.ScreenshotCopyFailed);
		Assert.False(result.ClipboardCopyFailed);
		Assert.Equal("recognised text", clipboard.LastText);
		Assert.Null(clipboard.LastImage);
	}

	[Fact]
	public async Task TakeScreenshotAndExtractTextAsync_ReportsNoScreenshotFailure_WhenTheClipboardTakesThePicture()
	{
		using var image = new SoftwareBitmap(BitmapPixelFormat.Bgra8, 2, 2, BitmapAlphaMode.Premultiplied);
		var clipboard = new TestClipboard { FailWrites = 2 };
		var manager = new TestableOcrManager(
			CreateValidSettings(), new StubOcrService("recognised text"), clipboard)
		{
			Screenshot = image,
		};

		var result = await manager.TakeScreenshotAndExtractTextAsync(DefaultOrder);

		Assert.True(result.Success);
		Assert.False(result.ScreenshotCopyFailed);
		Assert.False(result.ClipboardCopyFailed);
		Assert.Same(image, clipboard.LastImage);
	}

	// ---------------------------------------------------------------------
	// Bitmap lifetime (issue #229)
	//
	// A clipboard or screenshot bitmap is unmanaged imaging memory — roughly 30 MB on a
	// 4K multi-monitor desktop. Leaving it to a finalizer let repeated OCR hotkey presses
	// grow the working set until an encode failed outright, surfacing to the user as a
	// generic "OCR failed".
	// ---------------------------------------------------------------------

	[Fact]
	public async Task ExtractTextFromClipboardImageAsync_DisposesTheClipboardBitmap()
	{
		var image = new SoftwareBitmap(BitmapPixelFormat.Bgra8, 2, 2, BitmapAlphaMode.Premultiplied);
		var clipboard = new TestClipboard { Image = image };
		var manager = new TestableOcrManager(CreateValidSettings(), new StubOcrService("recognised text"), clipboard);

		var result = await manager.ExtractTextFromClipboardImageAsync(DefaultOrder);

		Assert.True(result.Success);
		AssertDisposed(image);
	}

	// The unconfigured path returns before any OCR call, and used to leak the same way.
	[Fact]
	public async Task ExtractTextFromClipboardImageAsync_DisposesTheClipboardBitmap_WhenOcrIsNotConfigured()
	{
		var image = new SoftwareBitmap(BitmapPixelFormat.Bgra8, 2, 2, BitmapAlphaMode.Premultiplied);
		var clipboard = new TestClipboard { Image = image };
		var manager = new TestableOcrManager(new Settings(), new StubOcrService(), clipboard);

		var result = await manager.ExtractTextFromClipboardImageAsync(DefaultOrder);

		Assert.False(result.Success);
		AssertDisposed(image);
	}

	[Fact]
	public async Task ExtractTextFromClipboardImageAsync_DisposesTheClipboardBitmap_WhenTheRequestFails()
	{
		var image = new SoftwareBitmap(BitmapPixelFormat.Bgra8, 2, 2, BitmapAlphaMode.Premultiplied);
		var clipboard = new TestClipboard { Image = image };
		var service = new StubOcrService(new InvalidOperationException("service unavailable"));
		var manager = new TestableOcrManager(CreateValidSettings(), service, clipboard);

		var result = await manager.ExtractTextFromClipboardImageAsync(DefaultOrder);

		Assert.False(result.Success);
		AssertDisposed(image);
	}

	// LockBuffer, not PixelWidth: a closed SoftwareBitmap still answers its metadata
	// properties, so those cannot tell a disposed bitmap from a live one. Reaching for the
	// pixels is what fails. The probe itself is pinned by the test below, so a projection
	// change that stops it throwing cannot quietly turn the leak tests green.
	private static void AssertDisposed(SoftwareBitmap bitmap)
	{
		Assert.Throws<ObjectDisposedException>(() => bitmap.LockBuffer(BitmapBufferAccessMode.Read).Dispose());
	}

	[Fact]
	public void TheDisposalProbeDistinguishesADisposedBitmapFromALiveOne()
	{
		using var live = new SoftwareBitmap(BitmapPixelFormat.Bgra8, 2, 2, BitmapAlphaMode.Premultiplied);
		live.LockBuffer(BitmapBufferAccessMode.Read).Dispose();

		var closed = new SoftwareBitmap(BitmapPixelFormat.Bgra8, 2, 2, BitmapAlphaMode.Premultiplied);
		closed.Dispose();

		AssertDisposed(closed);
	}

	private static void WaitForBeep(TestableOcrManager manager, int expectedCount)
	{
		var reached = SpinWait.SpinUntil(() => manager.BeepCount >= expectedCount, TimeSpan.FromSeconds(1));
		Assert.True(reached, $"Expected at least {expectedCount} beep(s).");
	}

	private sealed class TestableOcrManager : OcrManager
	{
		private readonly ConcurrentQueue<BeepType> _beeps = new();

		public TestableOcrManager(Settings settings, IOcrService ocrService, TestClipboard clipboard)
			: base(settings, ocrService, clipboard)
		{
			Clipboard = clipboard;
		}

		public TestClipboard Clipboard { get; }
		public int BeepCount => _beeps.Count;
		public IReadOnlyCollection<BeepType> Beeps => _beeps.ToArray();

		/// <summary>
		/// What the region overlay will hand back. Null means the user dismissed it without
		/// selecting anything, which is the cancelled path.
		/// </summary>
		public SoftwareBitmap? Screenshot { get; set; }

		/// <summary>
		/// Set to make the capture sit and wait, which is what an overlay on screen amounts to
		/// here: <c>CaptureStarted</c> completes once the capture is in flight, and the capture
		/// returns only when <c>ReleaseCapture</c> is set. Without it a capture finishes before
		/// a second press could ever meet it, so the refused branch would be unreachable.
		/// </summary>
		public bool HoldCapture { get; set; }

		public TaskCompletionSource CaptureStarted { get; } = new();

		public TaskCompletionSource ReleaseCapture { get; } = new();

		/// <summary>
		/// What the held capture claims is on screen. The real overlay is created and cleared
		/// inside <c>CaptureScreenshotAsync</c>, which this class replaces wholesale, so without
		/// standing in for the answer too a held capture always looks like one past the overlay
		/// — and the overlay-waiting refusal could not be reached in a test at all.
		/// </summary>
		public bool OverlayOnScreen { get; set; }

		protected override bool IsCaptureOverlayOnScreen => OverlayOnScreen;

		protected override void PlayBeep(BeepType type)
		{
			_beeps.Enqueue(type);
		}

		// The real one shows a full-screen overlay and reads the desktop, so nothing about the
		// two screenshot methods could be tested without standing in for it.
		protected override async Task<SoftwareBitmap?> CaptureScreenshotAsync()
		{
			if (!HoldCapture)
				return Screenshot;

			CaptureStarted.TrySetResult();
			await ReleaseCapture.Task;
			return Screenshot;
		}
	}

	private sealed class TestClipboard : RecordingClipboardManager
	{
		/// <param name="uiThread">
		/// Null for the tests that only care what reached the clipboard, so the write happens
		/// where the test stands. Supply one to check which thread the write was made on.
		/// </param>
		public TestClipboard(IUiThreadDispatcher? uiThread = null)
			: base(uiThread)
		{
		}

		// The image ExtractTextFromClipboardImageAsync will find. Null means "no image
		// on the clipboard", which is the path that beeps failure.
		public SoftwareBitmap? Image { get; set; }

		public override Task<SoftwareBitmap?> TryGetImageAsync(int attempts = 5, int delayMs = 150)
			=> Task.FromResult(Image);
	}

	// OcrManager can fan work out across threads, so the queue and the counter are
	// guarded even though CreateValidSettings pins the batch gates to one at a time.
	// An unguarded Queue<T> here would corrupt or throw, and a lost CallCount
	// increment would show up as a mystery off-by-one rather than a real failure.
	private sealed class StubOcrService : IOcrService
	{
		private readonly object _gate = new();
		private readonly Queue<Func<Stream, CancellationToken, Task<string>>> _behaviors;
		private int _callCount;

		public StubOcrService(params object[] behaviors)
		{
			_behaviors = new Queue<Func<Stream, CancellationToken, Task<string>>>(behaviors.Select(ConvertBehavior));
		}

		public int CallCount => Volatile.Read(ref _callCount);

		public Task<string> ExtractText(OcrReadingOrder ocrReadingOrder, Stream imageStream, CancellationToken overallCancellationToken)
		{
			Func<Stream, CancellationToken, Task<string>> behavior;
			lock (_gate)
			{
				// Counted only once a behaviour is actually handed out, so an
				// under-configured test fails on the missing behaviour rather than on a
				// CallCount that includes a call this stub refused to serve.
				if (_behaviors.Count == 0)
					throw new InvalidOperationException("No behavior configured for this OCR call.");

				behavior = _behaviors.Dequeue();
				Interlocked.Increment(ref _callCount);
			}

			return behavior(imageStream, overallCancellationToken);
		}

		private static Func<Stream, CancellationToken, Task<string>> ConvertBehavior(object behavior) => behavior switch
		{
			Func<Stream, CancellationToken, Task<string>> typed => typed,
			Func<Stream, Task<string>> streamFunc => (stream, _) => streamFunc(stream),
			Func<Task<string>> taskFactory => (_, _) => taskFactory(),
			string text => (_, _) => Task.FromResult(text),
			Exception ex => (_, _) => Task.FromException<string>(ex),
			_ => throw new ArgumentException("Unsupported behavior type.", nameof(behavior))
		};
	}

	// Records the peak number of overlapping ExtractText calls so concurrency throttles are
	// observable. Two ways to keep a call in flight long enough to overlap: a plain delay,
	// which only makes overlap likely, or a rendezvous, which makes it required.
	private sealed class ConcurrencyTrackingOcrService : IOcrService
	{
		private static readonly TimeSpan RendezvousTimeout = TimeSpan.FromSeconds(10);

		private readonly int _delayMs;
		private readonly int _rendezvousSize;
		private readonly object _sync = new();
		private TaskCompletionSource _gate = new(TaskCreationOptions.RunContinuationsAsynchronously);
		private int _waiting;
		private int _current;
		private int _maxConcurrency;
		private int _callCount;
		private int _rendezvousTimedOut;

		/// <param name="delayMs">How long each call occupies its slot. Ignored when
		/// <paramref name="rendezvousSize"/> is set.</param>
		/// <param name="rendezvousSize">When greater than zero, each call blocks until
		/// this many callers have arrived, then all of them are released together. That
		/// makes the overlap a fact the throttle has to produce rather than something the
		/// test hopes will happen inside a delay window, so the observed concurrency is
		/// exact instead of load-dependent. The expected call count must be a whole
		/// multiple of this, or the final part-filled group waits out the timeout and
		/// sets <see cref="RendezvousTimedOut"/>.</param>
		public ConcurrencyTrackingOcrService(int delayMs = 50, int rendezvousSize = 0)
		{
			_delayMs = delayMs;
			_rendezvousSize = rendezvousSize;
		}

		public int CallCount => Volatile.Read(ref _callCount);
		public int MaxConcurrency => Volatile.Read(ref _maxConcurrency);

		// True when a caller waited out the timeout without enough peers arriving —
		// i.e. the throttle admitted fewer concurrent calls than the test expected.
		public bool RendezvousTimedOut => Volatile.Read(ref _rendezvousTimedOut) != 0;

		public async Task<string> ExtractText(OcrReadingOrder ocrReadingOrder, Stream imageStream, CancellationToken overallCancellationToken)
		{
			Interlocked.Increment(ref _callCount);
			int now = Interlocked.Increment(ref _current);
			int observed;
			do
			{
				observed = Volatile.Read(ref _maxConcurrency);
				if (now <= observed)
					break;
			}
			while (Interlocked.CompareExchange(ref _maxConcurrency, now, observed) != observed);

			try
			{
				if (_rendezvousSize > 0)
					await ArriveAtRendezvousAsync(overallCancellationToken);
				else
					await Task.Delay(_delayMs, overallCancellationToken);

				return "ocr text";
			}
			finally
			{
				Interlocked.Decrement(ref _current);
			}
		}

		// A barrier that recycles: every group of _rendezvousSize arrivals is released
		// together, so a run of N files passes through in groups rather than the first
		// group holding the rest up forever.
		private async Task ArriveAtRendezvousAsync(CancellationToken token)
		{
			// Once one group has timed out the throttle is already proven wrong, so let
			// the rest through immediately rather than paying the timeout N more times
			// and turning a failing test into a multi-minute one.
			if (RendezvousTimedOut)
				return;

			TaskCompletionSource group;
			lock (_sync)
			{
				group = _gate;
				if (++_waiting == _rendezvousSize)
				{
					_waiting = 0;
					// Swap in a fresh gate for the next group before releasing this one.
					_gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
					group.TrySetResult();
				}
			}

			try
			{
				await group.Task.WaitAsync(RendezvousTimeout, token);
			}
			catch (TimeoutException)
			{
				// Never hang the suite on a broken throttle: record it, release anyone
				// else stuck on this gate, and let the test report a plain assertion
				// failure instead of a deadlock.
				Interlocked.Exchange(ref _rendezvousTimedOut, 1);
				group.TrySetResult();
			}
		}
	}

		private sealed class TempFile : IDisposable
	{
		public string Path { get; }

		public TempFile(string extension, string? contents = null)
		{
			var basePath = System.IO.Path.Combine(System.IO.Path.GetTempPath(), System.IO.Path.GetRandomFileName());
			Path = System.IO.Path.ChangeExtension(basePath, extension);
			File.WriteAllText(Path, contents ?? "test");
		}

		public void Dispose()
		{
			if (File.Exists(Path))
				File.Delete(Path);
		}
	}

	private static readonly byte[] ZeroPagePdfTemplate = System.Text.Encoding.ASCII.GetBytes("%PDF-1.4\n1 0 obj\n<< /Type /Catalog /Pages 2 0 R >>\nendobj\n2 0 obj\n<< /Type /Pages /Count 0 /Kids [] >>\nendobj\nxref\n0 3\n0000000000 65535 f \n0000000009 00000 n \n0000000058 00000 n \ntrailer\n<< /Root 1 0 R >>\nstartxref\n110\n%%EOF\n");

	private sealed class TempPdf : IDisposable
	{
		public string Path { get; }

		public TempPdf(int pageCount)
		{
			Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), System.IO.Path.GetRandomFileName() + ".pdf");
			if (pageCount == 0)
			{
				File.WriteAllBytes(Path, ZeroPagePdfTemplate);
				return;
			}

			using var document = new PdfDocument();
			for (var i = 0; i < pageCount; i++)
			{
				document.Pages.Add();
			}
			document.Save(Path);
		}

		public void Dispose()
		{
			if (File.Exists(Path))
				File.Delete(Path);
		}
	}
}
