using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CognitiveSupport;
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
		var manager = new TestableOcrManager(settings, service, clipboard, () => true, action =>
		{
			action();
			return Task.CompletedTask;
		});

		var result = await manager.ExtractTextFromFilesAsync(new[] { file.Path }, DefaultOrder, CancellationToken.None);

		string expectedText = $"[{Path.GetFileName(file.Path)}]{Environment.NewLine}recognized text{Environment.NewLine}";
		Assert.True(result.Success);
		Assert.Equal(1, result.TotalCount);
		Assert.Equal(1, result.SuccessCount);
		Assert.Equal(expectedText, result.Text);
		Assert.Equal(expectedText, clipboard.LastText);
		Assert.Equal(1, clipboard.SetTextCalls);
		Assert.Equal(0, manager.RunOnDispatcherCalls);
		WaitForBeep(manager, 1);
		Assert.Contains(BeepType.Success, manager.Beeps);
	}

	[Fact]
	public async Task ExtractTextFromFilesAsync_DispatchesClipboardUpdate_WhenOffUiThread()
	{
		var settings = CreateValidSettings();
		var clipboard = new TestClipboard();
		var service = new StubOcrService("batched result");
		using var file = new TempFile(".png");
		var dispatched = false;
		var manager = new TestableOcrManager(settings, service, clipboard, () => false, action =>
		{
			dispatched = true;
			action();
			return Task.CompletedTask;
		});

		var result = await manager.ExtractTextFromFilesAsync(new[] { file.Path }, DefaultOrder, CancellationToken.None);

		string expectedText = $"[{Path.GetFileName(file.Path)}]{Environment.NewLine}batched result{Environment.NewLine}";
		Assert.True(result.Success);
		Assert.True(dispatched);
		Assert.Equal(1, manager.RunOnDispatcherCalls);
		Assert.Equal(expectedText, clipboard.LastText);
		Assert.Equal(1, clipboard.SetTextCalls);
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
		var service = new ConcurrencyTrackingOcrService(delayMs: 50);
		var files = Enumerable.Range(0, 8).Select(_ => new TempFile(".png")).ToList();
		try
		{
			var manager = new TestableOcrManager(settings, service, clipboard);

			var result = await manager.ExtractTextFromFilesAsync(files.Select(f => f.Path), DefaultOrder, CancellationToken.None);

			Assert.True(result.Success);
			Assert.Equal(8, service.CallCount);
			Assert.True(service.MaxConcurrency <= 2, $"Observed {service.MaxConcurrency} concurrent OCR calls; expected at most 2.");
			Assert.True(service.MaxConcurrency >= 2, $"Expected the throttle to allow up to 2 concurrent calls; observed {service.MaxConcurrency}.");
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
		var service = new ConcurrencyTrackingOcrService(delayMs: 50);
		var files = Enumerable.Range(0, 8).Select(_ => new TempFile(".png")).ToList();
		try
		{
			var manager = new TestableOcrManager(settings, service, clipboard);

			var result = await manager.ExtractTextFromFilesAsync(files.Select(f => f.Path), DefaultOrder, CancellationToken.None);

			Assert.True(result.Success);
			Assert.Equal(8, service.CallCount);
			// Each single-page file makes exactly one OCR call while holding the document gate,
			// so concurrent OCR calls equal concurrent documents here.
			Assert.True(service.MaxConcurrency <= 2, $"Observed {service.MaxConcurrency} concurrent documents; expected at most 2.");
			Assert.True(service.MaxConcurrency >= 2, $"Expected up to 2 concurrent documents; observed {service.MaxConcurrency}.");
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

	private static void WaitForBeep(TestableOcrManager manager, int expectedCount)
	{
		var reached = SpinWait.SpinUntil(() => manager.BeepCount >= expectedCount, TimeSpan.FromSeconds(1));
		Assert.True(reached, $"Expected at least {expectedCount} beep(s).");
	}

	private sealed class TestableOcrManager : OcrManager
	{
		private readonly Func<bool> _hasThreadAccess;
		private readonly Func<Action, Task> _dispatcher;
		private readonly ConcurrentQueue<BeepType> _beeps = new();

		public TestableOcrManager(Settings settings, IOcrService ocrService, TestClipboard clipboard, Func<bool>? hasThreadAccess = null, Func<Action, Task>? dispatcher = null)
			: base(settings, ocrService, clipboard)
		{
			Clipboard = clipboard;
			_hasThreadAccess = hasThreadAccess ?? (() => true);
			_dispatcher = dispatcher ?? (action =>
			{
				action();
				return Task.CompletedTask;
			});
		}

		public TestClipboard Clipboard { get; }
		public int RunOnDispatcherCalls { get; private set; }
		public int BeepCount => _beeps.Count;
		public IReadOnlyCollection<BeepType> Beeps => _beeps.ToArray();

		protected override bool HasDispatcherThreadAccess() => _hasThreadAccess();

		protected override Task RunOnDispatcherAsync(Action action)
		{
			RunOnDispatcherCalls++;
			return _dispatcher(action);
		}

		protected override void PlayBeep(BeepType type)
		{
			_beeps.Enqueue(type);
		}
	}

	private sealed class TestClipboard : ClipboardManager
	{
		public string? LastText { get; private set; }
		public int SetTextCalls { get; private set; }

		// The image ExtractTextFromClipboardImageAsync will find. Null means "no image
		// on the clipboard", which is the path that beeps failure.
		public SoftwareBitmap? Image { get; set; }

		public override void SetText(string text)
		{
			SetTextCalls++;
			LastText = text;
		}

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
			Interlocked.Increment(ref _callCount);

			Func<Stream, CancellationToken, Task<string>> behavior;
			lock (_gate)
			{
				if (_behaviors.Count == 0)
					throw new InvalidOperationException("No behavior configured for this OCR call.");

				behavior = _behaviors.Dequeue();
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
	// observable. The delay keeps each call in flight long enough for overlap to occur.
	private sealed class ConcurrencyTrackingOcrService : IOcrService
	{
		private readonly int _delayMs;
		private int _current;
		private int _maxConcurrency;
		private int _callCount;

		public ConcurrencyTrackingOcrService(int delayMs = 50)
		{
			_delayMs = delayMs;
		}

		public int CallCount => Volatile.Read(ref _callCount);
		public int MaxConcurrency => Volatile.Read(ref _maxConcurrency);

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
				await Task.Delay(_delayMs, overallCancellationToken);
				return "ocr text";
			}
			finally
			{
				Interlocked.Decrement(ref _current);
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
