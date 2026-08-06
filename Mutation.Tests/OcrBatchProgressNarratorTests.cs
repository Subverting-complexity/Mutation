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

		Assert.Equal("report.pdf done. 1 of 3 documents processed.", announcement);
		Assert.Equal(1, narrator.DocumentsCompleted);
	}

	// The cadence the issue asks for: one announcement per document, not per page.
	[Fact]
	public void AFortyPageDocumentIsAnnouncedOnce()
	{
		var narrator = new OcrBatchProgressNarrator(totalDocuments: 2);
		var announcements = new List<string>();

		for (int page = 1; page <= 40; page++)
		{
			string? announcement = narrator.TryComposeAnnouncement(Progress(page, 80, "big.pdf", page, 40));
			if (announcement is not null)
				announcements.Add(announcement);
		}

		Assert.Single(announcements);
		Assert.Equal("big.pdf done. 1 of 2 documents processed.", announcements[0]);
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
				"scan1.png done. 1 of 4 documents processed.",
				"scan2.png done. 2 of 4 documents processed.",
				"scan3.png done. 3 of 4 documents processed.",
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

		Assert.Equal("second.png done. 2 of 2 documents processed.", announcement);
	}

	[Fact]
	public void ANegativeDocumentCountIsTreatedAsNone()
	{
		var narrator = new OcrBatchProgressNarrator(totalDocuments: -5);

		string? announcement = narrator.TryComposeAnnouncement(Progress(1, 9, "scan.png", 1, 1));

		Assert.Equal("scan.png done. 1 of 1 documents processed.", announcement);
	}

	[Fact]
	public void NullProgressIsRejected()
	{
		var narrator = new OcrBatchProgressNarrator(totalDocuments: 1);

		Assert.Throws<ArgumentNullException>(() => narrator.TryComposeAnnouncement(null!));
		Assert.Throws<ArgumentNullException>(() => OcrBatchProgressNarrator.ComposeLabel(null!));
	}

	// Progress and status share a screen-reader queue keyed by activity id. If they shared
	// one, a progress tick would supersede the pending "Processing N document(s)".
	[Fact]
	public void ProgressAnnouncementsUseTheirOwnActivity()
	{
		Assert.NotEqual(Mutation.Ui.Core.StatusAnnouncement.ActivityId, OcrBatchProgressNarrator.AnnouncementActivityId);
	}
}
