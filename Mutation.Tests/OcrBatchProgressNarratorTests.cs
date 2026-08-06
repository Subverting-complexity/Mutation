using System;
using System.Collections.Generic;
using Mutation.Ui.Services;

namespace Mutation.Tests;

/// <summary>
/// Batch OCR reports progress once per page and nothing announced it, so a forty-page
/// run was several minutes of silence with the button greyed out (issue #228). The fix
/// has to say something without saying it forty times, which is what these pin.
/// </summary>
public class OcrBatchProgressNarratorTests
{
	private static OcrProcessingProgress Progress(
		int processedSegments,
		int totalSegments,
		string fileName,
		int pageNumber,
		int totalPagesForFile)
		=> new(processedSegments, totalSegments, fileName, pageNumber, totalPagesForFile);

	// ---------------------------------------------------------------------
	// ComposeLabel — the visible line, updated on every report
	// ---------------------------------------------------------------------

	[Fact]
	public void ComposeLabel_ShowsThePageForAMultiPageDocument()
	{
		string label = OcrBatchProgressNarrator.ComposeLabel(Progress(2, 9, "report.pdf", 2, 5));

		Assert.Equal("report.pdf (Page 2 of 5)", label);
	}

	// A single image has no pages to count, and "(Page 1 of 1)" is noise on every line
	// of a forty-image batch.
	[Fact]
	public void ComposeLabel_OmitsThePageCountForASinglePageDocument()
	{
		string label = OcrBatchProgressNarrator.ComposeLabel(Progress(3, 40, "scan.png", 1, 1));

		Assert.Equal("scan.png", label);
	}

	// ---------------------------------------------------------------------
	// TryComposeAnnouncement — the throttled speech
	// ---------------------------------------------------------------------

	[Fact]
	public void MidDocumentPagesAreSilent()
	{
		var narrator = new OcrBatchProgressNarrator(totalDocuments: 2);

		Assert.Null(narrator.TryComposeAnnouncement(Progress(1, 10, "report.pdf", 1, 5)));
		Assert.Null(narrator.TryComposeAnnouncement(Progress(2, 10, "report.pdf", 2, 5)));
		Assert.Null(narrator.TryComposeAnnouncement(Progress(3, 10, "report.pdf", 3, 5)));
		Assert.Null(narrator.TryComposeAnnouncement(Progress(4, 10, "report.pdf", 4, 5)));
		Assert.Equal(0, narrator.DocumentsCompleted);
	}

	[Fact]
	public void AFinishedDocumentIsAnnouncedWithItsPlaceInTheRun()
	{
		var narrator = new OcrBatchProgressNarrator(totalDocuments: 3);

		string? announcement = narrator.TryComposeAnnouncement(Progress(5, 11, "report.pdf", 5, 5));

		Assert.Equal("Finished report.pdf. 1 of 3 documents.", announcement);
		Assert.Equal(1, narrator.DocumentsCompleted);
	}

	// The cadence the issue asks for: nowhere near one per page, and the page reports that
	// do get through are the sparse heartbeat, not the running commentary.
	[Fact]
	public void AFortyPageDocumentIsAnnouncedFourTimes()
	{
		var narrator = new OcrBatchProgressNarrator(totalDocuments: 2);
		var announcements = new List<string>();

		for (int page = 1; page <= 40; page++)
		{
			string? announcement = narrator.TryComposeAnnouncement(Progress(page, 80, "big.pdf", page, 40));
			if (announcement is not null)
				announcements.Add(announcement);
		}

		Assert.Equal(
			new[]
			{
				"big.pdf, page 10 of 40.",
				"big.pdf, page 20 of 40.",
				"big.pdf, page 30 of 40.",
				"Finished big.pdf. 1 of 2 documents.",
			},
			announcements);
	}

	// The exact scenario issue #228 describes: one long PDF, several minutes, and the user
	// hearing nothing. Per-document announcements alone cannot cover it — the document
	// finishes exactly once, and that once is the end of the run, which is silent by
	// design. Without the heartbeat this run says nothing at all.
	[Fact]
	public void ABatchOfOneLongDocumentIsNotSilent()
	{
		var narrator = new OcrBatchProgressNarrator(totalDocuments: 1);
		var announcements = new List<string>();

		for (int page = 1; page <= 40; page++)
		{
			string? announcement = narrator.TryComposeAnnouncement(Progress(page, 40, "big.pdf", page, 40));
			if (announcement is not null)
				announcements.Add(announcement);
		}

		Assert.NotEmpty(announcements);
		Assert.Equal(
			new[]
			{
				"big.pdf, page 10 of 40.",
				"big.pdf, page 20 of 40.",
				"big.pdf, page 30 of 40.",
			},
			announcements);
	}

	// The heartbeat must not turn a short document into a running commentary.
	[Fact]
	public void AShortDocumentGetsNoHeartbeat()
	{
		var narrator = new OcrBatchProgressNarrator(totalDocuments: 2);
		var announcements = new List<string>();

		for (int page = 1; page <= 9; page++)
		{
			string? announcement = narrator.TryComposeAnnouncement(Progress(page, 20, "short.pdf", page, 9));
			if (announcement is not null)
				announcements.Add(announcement);
		}

		Assert.Equal(new[] { "Finished short.pdf. 1 of 2 documents." }, announcements);
	}

	// A heartbeat is a position report, not an outcome, so it must not be counted as a
	// finished document — the cancellation message reports off that count.
	[Fact]
	public void AHeartbeatDoesNotCountAsAFinishedDocument()
	{
		var narrator = new OcrBatchProgressNarrator(totalDocuments: 1);

		Assert.NotNull(narrator.TryComposeAnnouncement(Progress(10, 40, "big.pdf", 10, 40)));

		Assert.Equal(0, narrator.DocumentsCompleted);
	}

	[Fact]
	public void EachSingleImageInABatchIsAnnouncedOnce()
	{
		var narrator = new OcrBatchProgressNarrator(totalDocuments: 4);
		var announcements = new List<string>();

		for (int index = 1; index <= 3; index++)
		{
			string? announcement = narrator.TryComposeAnnouncement(Progress(index, 4, $"scan{index}.png", 1, 1));
			if (announcement is not null)
				announcements.Add(announcement);
		}

		Assert.Equal(
			new[]
			{
				"Finished scan1.png. 1 of 4 documents.",
				"Finished scan2.png. 2 of 4 documents.",
				"Finished scan3.png. 3 of 4 documents.",
			},
			announcements);
	}

	// The run's own summary announces the outcome. If the narrator also spoke on the last
	// segment, "finished" would arrive as an indistinguishable progress tick immediately
	// before it — the opposite of a distinct completion state.
	[Fact]
	public void TheFinalSegmentIsSilentSoTheRunSummaryOwnsTheEnding()
	{
		var narrator = new OcrBatchProgressNarrator(totalDocuments: 2);
		narrator.TryComposeAnnouncement(Progress(1, 2, "first.png", 1, 1));

		string? announcement = narrator.TryComposeAnnouncement(Progress(2, 2, "second.png", 1, 1));

		Assert.Null(announcement);
	}

	// Silent, but still counted: the cancellation message reports how many finished, and
	// the count would be short by one if the last segment did not register.
	[Fact]
	public void TheFinalSegmentStillCountsAsAFinishedDocument()
	{
		var narrator = new OcrBatchProgressNarrator(totalDocuments: 2);
		narrator.TryComposeAnnouncement(Progress(1, 2, "first.png", 1, 1));
		narrator.TryComposeAnnouncement(Progress(2, 2, "second.png", 1, 1));

		Assert.Equal(2, narrator.DocumentsCompleted);
	}

	// Documents that fail to expand never report a final page, so the completed count can
	// outrun the path count the narrator was built with. Report the honest larger number
	// rather than "3 of 2".
	[Fact]
	public void TheTotalNeverReadsLowerThanTheNumberCompleted()
	{
		var narrator = new OcrBatchProgressNarrator(totalDocuments: 1);
		narrator.TryComposeAnnouncement(Progress(1, 9, "first.png", 1, 1));

		string? announcement = narrator.TryComposeAnnouncement(Progress(2, 9, "second.png", 1, 1));

		Assert.Equal("Finished second.png. 2 of 2 documents.", announcement);
	}

	[Fact]
	public void ANegativeDocumentCountIsTreatedAsNone()
	{
		var narrator = new OcrBatchProgressNarrator(totalDocuments: -5);

		string? announcement = narrator.TryComposeAnnouncement(Progress(1, 9, "scan.png", 1, 1));

		Assert.Equal("Finished scan.png. 1 of 1 documents.", announcement);
	}

	[Fact]
	public void NullProgressIsRejected()
	{
		var narrator = new OcrBatchProgressNarrator(totalDocuments: 1);

		Assert.Throws<ArgumentNullException>(() => narrator.TryComposeAnnouncement(null!));
		Assert.Throws<ArgumentNullException>(() => OcrBatchProgressNarrator.ComposeLabel(null!));
	}

	// A document that failed outright still reports its last page, so the announcement
	// must not claim it succeeded — the closing summary is about to say otherwise.
	[Fact]
	public void AFinishedDocumentIsNotDescribedAsSucceeding()
	{
		var narrator = new OcrBatchProgressNarrator(totalDocuments: 2);

		string? announcement = narrator.TryComposeAnnouncement(Progress(1, 9, "corrupt.pdf", 1, 1));

		Assert.NotNull(announcement);
		Assert.DoesNotContain("done", announcement, StringComparison.OrdinalIgnoreCase);
		Assert.DoesNotContain("success", announcement, StringComparison.OrdinalIgnoreCase);
	}
}
