using System;
using System.Globalization;
using System.Threading;

namespace Mutation.Ui.Services;

/// <summary>
/// Decides what a batch OCR run says out loud as it goes.
///
/// The run reports progress once per page, which is the right cadence for the progress
/// bar and far too fast for a screen reader — a forty-page batch would talk over itself
/// for minutes. So the visible label still updates on every report, while announcements
/// are held back to one per finished document (issue #228).
///
/// Per document alone is not enough, though. A batch of one long PDF finishes exactly
/// once, and that once is the end of the run, which is silent — so the user would sit
/// through minutes of nothing, which is the failure the issue describes. Long documents
/// therefore get a heartbeat every <see cref="PageAnnouncementInterval"/> pages: often
/// enough to prove the run is alive, rare enough not to talk over the user.
///
/// The last segment of the run is deliberately silent: the run's own completion summary
/// announces the outcome, and it should be the only thing the user hears at the end so
/// "finished" never sounds like just another progress tick.
///
/// Not thread-safe by contract, though it is cheap about it: every call arrives on the UI
/// thread, because the run's <c>Progress&lt;T&gt;</c> is built there and posts back to it.
/// </summary>
public sealed class OcrBatchProgressNarrator
{
	/// <summary>
	/// How many pages of one document pass between heartbeats. Ten pages is on the order
	/// of half a minute of OCR at the shipped parallelism — long enough not to nag, short
	/// enough that silence never starts to sound like a hang.
	/// </summary>
	public const int PageAnnouncementInterval = 10;

	/// <summary>
	/// Its own UIA activity, separate from the status bar's. Progress announcements
	/// supersede each other, and sharing the status activity would let a progress tick
	/// swallow the pending "Processing N document(s)" or the closing summary.
	/// </summary>
	public const string AnnouncementActivityId = "Mutation.OcrDocumentsProgress";

	private readonly int _totalDocuments;
	private int _documentsCompleted;

	public OcrBatchProgressNarrator(int totalDocuments)
	{
		_totalDocuments = Math.Max(0, totalDocuments);
	}

	/// <summary>Number of documents that have finished so far.</summary>
	public int DocumentsCompleted => Volatile.Read(ref _documentsCompleted);

	/// <summary>
	/// The line shown next to the progress bar. Updated on every report, page by page,
	/// because reading it is cheap and a sighted user wants the detail.
	/// </summary>
	public static string ComposeLabel(OcrProcessingProgress progress)
	{
		ArgumentNullException.ThrowIfNull(progress);

		return progress.TotalPagesForFile > 1
			? string.Format(CultureInfo.CurrentCulture, "{0} (Page {1} of {2})", progress.FileName, progress.PageNumber, progress.TotalPagesForFile)
			: progress.FileName;
	}

	/// <summary>
	/// The announcement for this report, or <c>null</c> when it should pass in silence.
	/// </summary>
	public string? TryComposeAnnouncement(OcrProcessingProgress progress)
	{
		ArgumentNullException.ThrowIfNull(progress);

		// Mid-document pages are silent apart from the heartbeat: the document is what the
		// user thinks in, and a page each would be unbearable on a long PDF.
		if (progress.PageNumber < progress.TotalPagesForFile)
		{
			if (progress.PageNumber % PageAnnouncementInterval != 0)
				return null;

			return string.Format(
				CultureInfo.CurrentCulture,
				"{0}, page {1} of {2}.",
				progress.FileName,
				progress.PageNumber,
				progress.TotalPagesForFile);
		}

		int completed = Interlocked.Increment(ref _documentsCompleted);

		// The run summary owns the ending, so say nothing on the final segment.
		if (progress.ProcessedSegments >= progress.TotalSegments)
			return null;

		// "Finished", not "done": a document that failed outright still reports its last
		// page, and telling the user "corrupt.pdf done" would claim a success the closing
		// summary is about to contradict. This says where the run is, not how it went.
		int total = Math.Max(_totalDocuments, completed);
		return string.Format(
			CultureInfo.CurrentCulture,
			"Finished {0}. {1} of {2} documents.",
			progress.FileName,
			completed,
			total);
	}
}
