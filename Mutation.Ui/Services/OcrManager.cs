using CognitiveSupport;
using Microsoft.UI.Xaml;
using Mutation.Ui.Core;
using Mutation.Ui.Views;
using PdfSharp.Pdf;
using PdfSharp.Pdf.IO;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Windows.Data.Pdf;
using Windows.Foundation;
using Windows.Graphics.Imaging;
using Windows.Storage;
using Windows.Storage.Streams;
using System.Drawing;
using System.Drawing.Imaging;
using System.Windows.Forms;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Runtime.CompilerServices;

namespace Mutation.Ui.Services;

/// <param name="Message">The recognised text when <paramref name="Success"/>, otherwise why not.</param>
/// <param name="Outcome">
/// Whether this run produced an answer at all. Read instead of comparing
/// <paramref name="Message"/> against a known string, which is how a refused run used to be
/// told apart from a failed one — a decision that broke the moment anybody reworded a message.
/// </param>
/// <param name="ClipboardCopyFailed">
/// True when there is recognised text here that did not reach the clipboard. It says something
/// only alongside <c>Success</c>: a run that recognised nothing had nothing to copy, and does
/// not report a copy that never happened as having failed (issue #341).
/// </param>
/// <param name="ScreenshotCopyFailed">
/// True when the screenshot this run read from did not itself reach the clipboard. Separate from
/// <paramref name="ClipboardCopyFailed"/> because the two are independent: the reading works from
/// the captured bitmap in hand, so a picture that never got to the clipboard says nothing about
/// whether the text that followed it did (issue #360). Always false for the paths that never put
/// a picture on the clipboard.
/// </param>
public record OcrResult(
	bool Success,
	string Message,
	OcrRunOutcome Outcome = OcrRunOutcome.Answered,
	bool ClipboardCopyFailed = false,
	bool ScreenshotCopyFailed = false);

/// <param name="ClipboardCopyFailed">
/// As on <see cref="OcrResult"/>: the combined text was recognised but could not be put on the
/// clipboard, so the run must not announce that the results were copied.
/// </param>
public record OcrBatchResult(
	bool Success,
	string Text,
	int TotalCount,
	int SuccessCount,
	IReadOnlyList<string> Failures,
	bool ClipboardCopyFailed = false);
public record OcrProcessingProgress(int ProcessedSegments, int TotalSegments, string FileName, int PageNumber, int TotalPagesForFile);

public class OcrManager
{
    private static readonly string[] SupportedFileExtensionArray = new[] { ".pdf", ".png", ".jpg", ".jpeg", ".bmp", ".tif", ".tiff" };
    private static readonly IReadOnlyList<string> SupportedFileExtensionReadOnly = Array.AsReadOnly(SupportedFileExtensionArray);
    private static readonly HashSet<string> SupportedFileExtensionSet = new(SupportedFileExtensionArray, StringComparer.OrdinalIgnoreCase);
    private const string SupportedFileExtensionsDisplay = ".pdf, .png, .jpg, .jpeg, .bmp, .tif, .tiff";

    private readonly Settings _settings;
    private readonly IOcrService _ocrService;
    private readonly ClipboardManager _clipboard;
    private Window? _window;
    private RegionSelectionWindow? _activeOverlay;
    private RegionSelectionWindow? _cachedOverlay;
    private int _captureInFlight; // 0 = idle, 1 = busy. Guards against re-entrant screenshot starts.

    public static IReadOnlyList<string> SupportedFileExtensions => SupportedFileExtensionReadOnly;

    public OcrManager(Settings settings, IOcrService ocrService, ClipboardManager clipboard)
    {
        _settings = settings;
        _ocrService = ocrService;
        _clipboard = clipboard;
    }

    public void InitializeWindow(Window window)
    {
        _window = window;
        // Pre-warm a reusable overlay instance to reduce first-use latency
        try { _cachedOverlay = new RegionSelectionWindow(); _cachedOverlay.PrepareWindowForReuse(); } catch { _cachedOverlay = null; }
    }

    /// <summary>
    /// Whether a region-selection overlay is on screen right now.
    /// <para>
    /// Exact rather than approximate, though only because of where it is read from. The field is
    /// written by the capture running on the UI thread, and both callers of the guard that reads
    /// it are UI-thread hotkey and click handlers, so reader and writer never run at once and
    /// the answer cannot be stale. Nothing enforces that beyond the callers themselves — take
    /// this guard off the UI thread and the read becomes a genuine race.
    /// </para>
    /// <para>
    /// Virtual because <see cref="CaptureScreenshotAsync"/> is the only thing that sets the
    /// field, and a test stands in for that wholesale — so without this seam the overlay-waiting
    /// case could not be reached in a test at all, which is how the wrong sentence went out
    /// covered by a green suite.
    /// </para>
    /// <para>
    /// The cost of the seam is that this line is the one thing here no test covers: every test
    /// overrides it, so inverting the condition breaks nothing. What the tests hold is that the
    /// guard asks this question and says the right thing about each answer.
    /// </para>
    /// </summary>
    protected virtual bool IsCaptureOverlayOnScreen => _activeOverlay is not null;

    /// <summary>
    /// Captures a region and puts it on the clipboard, saying which of the five things
    /// happened. Callers must not announce success unconditionally — for a blind user,
    /// "Screenshot copied to the clipboard" after a cancelled capture is worse than silence,
    /// and so is it after a capture the clipboard would not take. Nor may they treat a refused
    /// press as a cancellation, which tells the user a capture has gone when it has not
    /// (issue #363), or run the two refusals together, which offers a control that is only
    /// there for one of them (issue #367).
    /// </summary>
    public async Task<ScreenshotToClipboardOutcome> TakeScreenshotToClipboardAsync()
    {
        if (Interlocked.CompareExchange(ref _captureInFlight, 1, 0) != 0)
        {
            // Refused, not cancelled. The two used to be the same value, so the user was told
            // "Screenshot cancelled. Nothing was copied to the clipboard." about an overlay
            // that was still on screen waiting for them (issue #363) — the opposite of the
            // truth for someone who cannot see it.
            //
            // Which refusal depends on whether there is an overlay, because the guard outlives
            // it: it is held through the crop, the clipboard write and its retries, and — when
            // the capture holding it is a screenshot-and-OCR run — the entire reading. Telling
            // someone with nothing on screen to select a region is an instruction they cannot
            // follow, and the Escape it offers would go to whatever application actually has
            // the keyboard (issue #367).
            //
            // The guard also runs ahead of the overlay, between taking the flag and putting the
            // overlay up, and that stretch must stay unreachable — a press answered there would
            // say "wait" moments before an overlay appeared asking for a rectangle, which is
            // this same bug inverted and lands on the commonest mistiming of all, the quick
            // double press. It is unreachable today only because that stretch never yields:
            // CaptureScreenshotAsync grabs the screen and prepares the window synchronously,
            // and RegionSelectionWindow.InitializeAsync returns an already-completed task, so
            // the message pump cannot deliver a second press until the overlay is up. Give that
            // method a real await and this opens with nothing going red.
            bool overlayWaiting = IsCaptureOverlayOnScreen;
            try { _activeOverlay?.BringToFront(); } catch { }
            return overlayWaiting
                ? ScreenshotToClipboardOutcome.RefusedOverlayWaiting
                : ScreenshotToClipboardOutcome.RefusedCaptureRunning;
        }
        try
        {
            // Disposed here: a virtual-screen capture is tens of megabytes of unmanaged
            // imaging memory, and waiting for a finalizer pass lets repeated hotkey
            // presses grow the working set until an encode fails outright (issue #229).
            using var bitmap = await CaptureScreenshotAsync();
            if (bitmap == null)
            {
                await PlayBeepSafeAsync(BeepType.Failure);
                return ScreenshotToClipboardOutcome.Cancelled;
            }

            bool copied = await _clipboard.TrySetImageAsync(bitmap);
            await PlayBeepSafeAsync(copied ? BeepType.Success : BeepType.Failure);
            return copied
                ? ScreenshotToClipboardOutcome.Copied
                : ScreenshotToClipboardOutcome.ClipboardUnavailable;
        }
        finally
        {
            Interlocked.Exchange(ref _captureInFlight, 0);
        }
    }

    public async Task<OcrResult> TakeScreenshotAndExtractTextAsync(OcrReadingOrder order)
    {
        if (Interlocked.CompareExchange(ref _captureInFlight, 1, 0) != 0)
        {
            // Refused, not failed. Nothing happened, the OCR box still holds the last run's
            // answer, and the thing in front of the user is the capture overlay — so the
            // shortcut configured to run after an OCR must not be sent, or it is typed into
            // that overlay (issue #342). The outcome carries that; the message used to be the
            // only way to tell, which meant rewording it would have quietly broken the
            // decision.
            try { _activeOverlay?.BringToFront(); } catch { }
            return new(false, ClipboardCopyMessages.OcrCaptureAlreadyInProgress, OcrRunOutcome.Refused);
        }
        try
        {
            using var bitmap = await CaptureScreenshotAsync();
            if (bitmap == null)
            {
                await PlayBeepSafeAsync(BeepType.Failure);
                return new(false, "Screenshot cancelled.");
            }

            // The reading goes ahead whatever the clipboard says, because it works from the
            // bitmap in hand and never needed the clipboard copy. This used to throw out of
            // here when the clipboard was busy, losing a capture the user cannot repeat —
            // whatever was on screen has moved on (issue #360).
            bool imageCopied = await _clipboard.TrySetImageAsync(bitmap);
            var result = await ExtractTextViaOcrAsync(order, bitmap);

            // The beep follows the reading, not the picture — deliberately, and unlike the plain
            // screenshot above, which has nothing but the picture to report. Here the text is
            // what the user asked for. Beeping failure over a picture that did not copy would
            // tell them the reading failed, and send them off to take the whole capture again
            // when the text they wanted is already on their clipboard. The picture gets a spoken
            // warning instead, which is the right weight for it.
            await PlayBeepSafeAsync(result.Success ? BeepType.Success : BeepType.Failure);
            return imageCopied ? result : result with { ScreenshotCopyFailed = true };
        }
        finally
        {
            Interlocked.Exchange(ref _captureInFlight, 0);
        }
    }

    public async Task<OcrResult> ExtractTextFromClipboardImageAsync(OcrReadingOrder order)
    {
        using var bitmap = await _clipboard.TryGetImageAsync();
        if (bitmap == null)
        {
            PlayBeepSafe(BeepType.Failure);
            return new(false, "No image on clipboard.");
        }

        var result = await ExtractTextViaOcrAsync(order, bitmap);
        await PlayBeepSafeAsync(result.Success ? BeepType.Success : BeepType.Failure);
        return result;
    }

    public async Task<OcrBatchResult> ExtractTextFromFilesAsync(IEnumerable<string> filePaths, OcrReadingOrder order, CancellationToken cancellationToken, IProgress<OcrProcessingProgress>? progress = null)
    {
        if (filePaths is null)
            throw new ArgumentNullException(nameof(filePaths));

        List<string> paths = filePaths
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (paths.Count == 0)
        {
            PlayBeepSafe(BeepType.Failure);
            return new(false, string.Empty, 0, 0, Array.Empty<string>());
        }

        if (!IsOcrConfigured(out string configurationError))
        {
            PlayBeepSafe(BeepType.Failure);
            return new(false, string.Empty, paths.Count, 0, new[] { configurationError });
        }

        var batches = ExpandFileBatches(paths);
        int totalSegments = batches.Sum(batch => batch.Items.Count);
        if (totalSegments == 0)
        {
            PlayBeepSafe(BeepType.Failure);
            return new(false, string.Empty, paths.Count, 0, Array.Empty<string>());
        }

        int maxParallelDocuments = GetMaxParallelDocuments();
        int maxParallelRequests = GetMaxParallelRequests();
        (bool useFreeTier, int freeTierPageLimit) = GetFreeTierGrouping();

        // Two shared throttles for the whole run: documentGate caps files in flight,
        // requestGate is the global ceiling on concurrent OCR service calls across all files.
        using var documentGate = new SemaphoreSlim(maxParallelDocuments, maxParallelDocuments);
        using var requestGate = new SemaphoreSlim(maxParallelRequests, maxParallelRequests);

        var processedSegments = new StrongBox<int>(0);

        // Map: fan out one self-contained task per file. Each returns a FileOcrOutcome
        // and never mutates shared run-level state.
        var tasks = new List<Task<FileOcrOutcome>>(batches.Count);
        for (int index = 0; index < batches.Count; index++)
        {
            tasks.Add(ProcessBatchAsync(
                batches[index],
                index,
                order,
                documentGate,
                requestGate,
                useFreeTier,
                freeTierPageLimit,
                totalSegments,
                processedSegments,
                progress,
                cancellationToken));
        }

        // Throttle is enforced inside the tasks via the two gates. Task.WhenAll propagates
        // OperationCanceledException so fail-fast cancellation behaviour is preserved.
        FileOcrOutcome[] outcomes = await Task.WhenAll(tasks);

        // A cancel that lands after the last page finished but before the reduce would
        // otherwise fall through to the success path and overwrite the clipboard — the
        // one thing the user is told cancelling never does. Checked here so cancelling
        // means the same thing however late it arrives (issue #227).
        cancellationToken.ThrowIfCancellationRequested();

        // Reduce: combine results on a single thread, in original selection order, so today's
        // output format and ordering are protected against the concurrent execution above.
        var combinedText = new StringBuilder();
        var failures = new List<string>();
        int successCount = 0;

        foreach (var outcome in outcomes.OrderBy(outcome => outcome.Order))
        {
            if (!string.IsNullOrEmpty(outcome.Section))
            {
                if (combinedText.Length > 0)
                    combinedText.AppendLine().AppendLine();

                combinedText.Append(outcome.Section);
            }

            if (outcome.Failures.Count > 0)
                failures.AddRange(outcome.Failures);

            if (outcome.Succeeded)
                ++successCount;
        }

        string resultText = combinedText.ToString();
        bool copied = true;
        if (successCount > 0 && !string.IsNullOrWhiteSpace(resultText))
            copied = await TrySetClipboardTextAsync(resultText);

        bool success = successCount > 0 && failures.Count == 0;
        await PlayBeepSafeAsync(success ? BeepType.Success : BeepType.Failure);

        return new(success, resultText, paths.Count, successCount, failures.AsReadOnly(), ClipboardCopyFailed: !copied);
    }

    // Self-contained processing for a single file. Returns a FileOcrOutcome and never mutates
    // shared run-level state, so many of these can run concurrently under the shared gates.
    private async Task<FileOcrOutcome> ProcessBatchAsync(
        FileOcrBatch batch,
        int order,
        OcrReadingOrder readingOrder,
        SemaphoreSlim documentGate,
        SemaphoreSlim requestGate,
        bool useFreeTier,
        int freeTierPageLimit,
        int totalSegments,
        StrongBox<int> processedSegments,
        IProgress<OcrProcessingProgress>? progress,
        CancellationToken cancellationToken)
    {
        await documentGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            cancellationToken.ThrowIfCancellationRequested();

            bool fileHasSuccess = false;
            bool fileHasFailure = false;
            int totalPagesForFile = Math.Max(1, batch.Items.Select(item => Math.Max(item.TotalPages, item.PageNumber)).DefaultIfEmpty(1).Max());
            var pageResults = new List<(int PageNumber, string Text)>();
            var failures = new List<string>();

            // Free-tier grouping only defines the logical submission chunk size. Each page is
            // still processed as its own OCR request, so every page is processed and nothing is
            // truncated regardless of FreeTierPageLimit.
            foreach (var submission in GroupIntoSubmissions(batch.Items, useFreeTier, freeTierPageLimit))
            {
                foreach (var item in submission)
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    try
                    {
                        if (item.InitializationError is not null)
                        {
                            fileHasFailure = true;
                            failures.Add($"{batch.FileName}: {item.InitializationError.Message}");
                            continue;
                        }

                        using var stream = await item.OpenStreamAsync().ConfigureAwait(false);

                        long maxDocumentBytes = GetMaxDocumentBytes();
                        if (maxDocumentBytes > 0 && stream.CanSeek && stream.Length > maxDocumentBytes)
                        {
                            fileHasFailure = true;
                            failures.Add($"{batch.FileName} (Page {item.PageNumber}): {FormatMegabytes(stream.Length)} exceeds the {FormatMegabytes(maxDocumentBytes)} maximum document size.");
                            continue;
                        }

                        string text = await ExtractTextThrottledAsync(readingOrder, stream, requestGate, cancellationToken).ConfigureAwait(false);
                        string sanitizedText = string.IsNullOrWhiteSpace(text) ? string.Empty : text.TrimEnd();
                        pageResults.Add((item.PageNumber, sanitizedText));
                        fileHasSuccess = true;
                    }
                    catch (OperationCanceledException)
                    {
                        throw;
                    }
                    catch (Exception ex)
                    {
                        fileHasFailure = true;
                        failures.Add($"{batch.FileName} (Page {item.PageNumber}): {ex.Message}");
                    }
                    finally
                    {
                        int processed = Interlocked.Increment(ref processedSegments.Value);
                        progress?.Report(new OcrProcessingProgress(processed, totalSegments, batch.FileName, item.PageNumber, totalPagesForFile));
                    }
                }
            }

            string section = BuildFileSection(batch.FileName, fileHasSuccess, totalPagesForFile, pageResults);
            bool succeeded = fileHasSuccess && !fileHasFailure;
            return new FileOcrOutcome(order, section, failures, succeeded);
        }
        finally
        {
            documentGate.Release();
        }
    }

    // Wraps a single OCR service call with the global request gate so MaxParallelRequests is a
    // true ceiling across all files and all generated work items.
    private async Task<string> ExtractTextThrottledAsync(OcrReadingOrder order, Stream stream, SemaphoreSlim requestGate, CancellationToken cancellationToken)
    {
        await requestGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            return await _ocrService.ExtractText(order, stream, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            requestGate.Release();
        }
    }

    // Builds a file's combined-text section exactly as the sequential implementation did, so the
    // ordered reduce can simply append sections separated by a blank line.
    private static string BuildFileSection(string fileName, bool fileHasSuccess, int totalPagesForFile, List<(int PageNumber, string Text)> pageResults)
    {
        if (!fileHasSuccess)
            return string.Empty;

        var builder = new StringBuilder();
        builder.AppendLine($"[{fileName}]");

        pageResults.Sort((left, right) => left.PageNumber.CompareTo(right.PageNumber));
        var fileTextBuilder = new StringBuilder();

        for (int index = 0; index < pageResults.Count; index++)
        {
            var segment = pageResults[index];

            if (totalPagesForFile > 1)
                fileTextBuilder.AppendLine($"(Page {segment.PageNumber})");

            if (!string.IsNullOrWhiteSpace(segment.Text))
                fileTextBuilder.AppendLine(segment.Text);

            bool hasAdditionalSegments = index < pageResults.Count - 1;
            if (hasAdditionalSegments && totalPagesForFile > 1)
                fileTextBuilder.AppendLine();
        }

        string fileText = fileTextBuilder.ToString().TrimEnd();
        if (!string.IsNullOrWhiteSpace(fileText))
            builder.AppendLine(fileText);

        return builder.ToString();
    }

    // Groups a file's per-page work items into logical free-tier submissions. We keep one page
    // per OCR request, so this only documents the intended chunk size; the same FreeTierPageLimit
    // can drive multi-page submissions later without changing the call sites.
    private static IReadOnlyList<IReadOnlyList<OcrWorkItem>> GroupIntoSubmissions(IReadOnlyList<OcrWorkItem> items, bool useFreeTier, int freeTierPageLimit)
    {
        if (!useFreeTier || freeTierPageLimit <= 1 || items.Count <= freeTierPageLimit)
            return new[] { items };

        var groups = new List<IReadOnlyList<OcrWorkItem>>();
        for (int start = 0; start < items.Count; start += freeTierPageLimit)
        {
            int count = Math.Min(freeTierPageLimit, items.Count - start);
            var chunk = new List<OcrWorkItem>(count);
            for (int offset = 0; offset < count; offset++)
                chunk.Add(items[start + offset]);
            groups.Add(chunk);
        }

        return groups;
    }

    private async Task<OcrResult> ExtractTextViaOcrAsync(OcrReadingOrder order, SoftwareBitmap bitmap)
    {
        if (!IsOcrConfigured(out string configurationError))
            return new(false, configurationError);

        using var stream = new InMemoryRandomAccessStream();
        var encoder = await BitmapEncoder.CreateAsync(BitmapEncoder.PngEncoderId, stream);
        encoder.SetSoftwareBitmap(bitmap);
        await encoder.FlushAsync();
        stream.Seek(0);

        // The image is encoded and the request is going out — the same moment the
        // dictation end beep marks (issue #268). It used to sound here as a side effect
        // of OcrService's retry signal beeping on attempt 1; #216 silenced that first
        // attempt, which was correct for retries and left OCR with no end beep at all.
        // #269 restored it for dictation and missed this path.
        //
        // After the encode, not before: the encode is not inside the catch below, so a
        // failure there ends the call with no success or failure beep to follow. An end
        // beep in front of that silence would have announced a request that never went.
        //
        // Raised here rather than in the batch path: that one issues a request per
        // segment, so a beep each would be a burst, and it reports progress of its own.
        await PlayBeepSafeAsync(BeepType.End);

        using Stream netStream = stream.AsStream();
        try
        {
            var text = await _ocrService.ExtractText(order, netStream, default);

            // Outside the catch on purpose. The read has already succeeded by this point, and a
            // clipboard that will not open is not a reason to call the read a failure and put a
            // COM error where the recognised text belongs (issue #341).
            bool copied = await TrySetClipboardTextAsync(text);
            return new(true, text, ClipboardCopyFailed: !copied);
        }
        catch (Exception ex)
        {
            return new(false, ex.Message);
        }
    }

    // Configured maximum bytes for a single OCR upload, or 0 when no limit applies
    // (null or a non-positive stored value).
    private long GetMaxDocumentBytes()
    {
        long? configured = _settings.AzureComputerVisionSettings?.MaxDocumentBytes;
        return configured is > 0 ? configured.Value : 0;
    }

    // Concurrency knobs are clamped to at least 1, mirroring the GetMaxDocumentBytes pattern.
    private int GetMaxParallelDocuments() =>
        Math.Max(1, _settings.AzureComputerVisionSettings?.MaxParallelDocuments ?? 1);

    private int GetMaxParallelRequests() =>
        Math.Max(1, _settings.AzureComputerVisionSettings?.MaxParallelRequests ?? 1);

    private (bool UseFreeTier, int FreeTierPageLimit) GetFreeTierGrouping()
    {
        var settings = _settings.AzureComputerVisionSettings;
        bool useFreeTier = settings?.UseFreeTier ?? false;
        int pageLimit = Math.Max(1, settings?.FreeTierPageLimit ?? 1);
        return (useFreeTier, pageLimit);
    }

    private static string FormatMegabytes(long bytes) => $"{bytes / (1024.0 * 1024.0):0.#} MB";

    // True when Azure Computer Vision has a real key and endpoint (neither is the
    // placeholder default). Exposed so the UI can warn and steer the user to the OCR
    // settings tab before attempting an operation that would otherwise just fail.
    public bool IsOcrConfigured(out string message)
    {
        var settings = _settings.AzureComputerVisionSettings;
        if (settings is null)
        {
            message = "Azure Computer Vision settings are missing. Update AzureComputerVisionSettings in the settings file.";
            return false;
        }

        bool apiKeyMissing = IsPlaceholderValue(settings.ApiKey);
        bool endpointMissing = IsPlaceholderEndpoint(settings.Endpoint);

        if (!apiKeyMissing && !endpointMissing)
        {
            message = string.Empty;
            return true;
        }

        if (apiKeyMissing && endpointMissing)
        {
            message = "Azure Computer Vision endpoint and API key are not configured. Update AzureComputerVisionSettings in the settings file.";
        }
        else if (apiKeyMissing)
        {
            message = "Azure Computer Vision API key is not configured. Update AzureComputerVisionSettings.ApiKey in the settings file.";
        }
        else
        {
            message = "Azure Computer Vision endpoint is not configured. Update AzureComputerVisionSettings.Endpoint in the settings file.";
        }

        return false;
    }

    private static bool IsPlaceholderValue(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return true;

        string trimmed = value.Trim();
        return string.Equals(trimmed, "<placeholder>", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsPlaceholderEndpoint(string? endpoint)
    {
        if (IsPlaceholderValue(endpoint))
            return true;

        if (!Uri.TryCreate(endpoint, UriKind.Absolute, out var uri))
            return true;

        return string.Equals(uri.Host, "placeholder.com", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Puts <paramref name="text"/> on the clipboard, retrying while something else has it
    /// open. True when it landed, or when there was nothing worth copying.
    /// <para>
    /// It used to be a bare <c>SetContent</c> with no retry, which the speech-to-text path had
    /// already stopped doing. The window for losing the race is at its widest on the screenshot
    /// path, which puts the <em>image</em> on the clipboard a moment earlier — exactly when a
    /// clipboard manager or a screen reader opens the clipboard to look at what arrived. The
    /// <c>CLIPBRD_E_CANT_OPEN</c> that came back was caught as an OCR failure, so a perfectly
    /// good read was reported as a failed one and the shortcut that ran afterwards acted on the
    /// screenshot still sitting there (issue #341).
    /// </para>
    /// <para>
    /// The retry and the UI-thread hop it used to own both live in <see cref="ClipboardManager"/>
    /// now. Every clipboard caller in the app had the same problem, not just this one, and two
    /// copies of the rule was one too many (issue #352). What stays here is the difference that
    /// is genuinely this path's own: blank text is a success, because there was nothing worth
    /// copying and nothing failed.
    /// </para>
    /// </summary>
    private Task<bool> TrySetClipboardTextAsync(string text) =>
        string.IsNullOrWhiteSpace(text)
            ? Task.FromResult(true)
            : _clipboard.TrySetTextAsync(text);

    private static IReadOnlyList<FileOcrBatch> ExpandFileBatches(IReadOnlyList<string> paths)
    {
        var batches = new List<FileOcrBatch>(paths.Count);

        foreach (string path in paths)
        {
            var items = ExpandFile(path);
            batches.Add(new FileOcrBatch(path, items));
        }

        return batches;
    }

    private static List<OcrWorkItem> ExpandFile(string path)
    {
        var items = new List<OcrWorkItem>();

        if (!IsSupportedFileType(path))
        {
            string extension = Path.GetExtension(path);
            string message = string.IsNullOrWhiteSpace(extension)
                ? $"Unsupported file type. Supported file types: {SupportedFileExtensionsDisplay}."
                : $"Unsupported file type '{extension}'. Supported file types: {SupportedFileExtensionsDisplay}.";
            items.Add(OcrWorkItem.CreateError(path, new NotSupportedException(message)));
            return items;
        }

        if (IsPdf(path))
        {
            try
            {
                using var document = PdfReader.Open(path, PdfDocumentOpenMode.Import);

                if (document.PageCount == 0)
                {
                    items.Add(OcrWorkItem.CreateError(path, new InvalidOperationException("PDF contains no pages.")));
                }
                else
                {
                    int totalPages = document.PageCount;

                    for (int i = 0; i < totalPages; i++)
                    {
                        int pageNumber = i + 1;
                        items.Add(OcrWorkItem.CreatePdf(path, pageNumber, totalPages));
                    }
                }
            }
            catch (Exception ex)
            {
                items.Add(OcrWorkItem.CreateError(path, ex));
            }
        }
        else
        {
            items.Add(OcrWorkItem.CreateFile(path));
        }

        return items;
    }

    private static bool IsPdf(string path)
    {
        string extension = Path.GetExtension(path);
        return string.Equals(extension, ".pdf", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsSupportedFileType(string path)
    {
        string extension = Path.GetExtension(path);
        if (string.IsNullOrWhiteSpace(extension))
            return false;

        return SupportedFileExtensionSet.Contains(extension);
    }

    private static async Task<Stream> CreatePdfPageImageStreamAsync(string path, int pageIndex)
    {
        StorageFile file = await StorageFile.GetFileFromPathAsync(path);
        Windows.Data.Pdf.PdfDocument pdfDocument = await Windows.Data.Pdf.PdfDocument.LoadFromFileAsync(file);

        if (pageIndex < 0 || pageIndex >= pdfDocument.PageCount)
            throw new ArgumentOutOfRangeException(nameof(pageIndex));

        using Windows.Data.Pdf.PdfPage page = pdfDocument.GetPage((uint)pageIndex);
        var stream = new InMemoryRandomAccessStream();
        
        // Render options can be customized if needed, e.g. scaling for better OCR
        await page.RenderToStreamAsync(stream);
        return stream.AsStreamForRead();
    }

    // Self-contained result for a single file, carried back from ProcessBatchAsync to the
    // ordered reduce. Order preserves the file's original position in the selection.
    private sealed record FileOcrOutcome(int Order, string Section, IReadOnlyList<string> Failures, bool Succeeded);

    private sealed class FileOcrBatch
    {
        public FileOcrBatch(string path, List<OcrWorkItem> items)
        {
            OriginalPath = path;
            FileName = Path.GetFileName(path);
            if (string.IsNullOrWhiteSpace(FileName))
                FileName = path;
            Items = items ?? new List<OcrWorkItem>();
        }

        public string OriginalPath { get; }
        public string FileName { get; }
        public List<OcrWorkItem> Items { get; }
    }

    private sealed class OcrWorkItem
    {
        private readonly Func<Task<Stream>>? _streamFactory;

        private OcrWorkItem(string originalPath, Func<Task<Stream>>? streamFactory, int pageNumber, int totalPages, Exception? initializationError)
        {
            OriginalPath = originalPath;
            _streamFactory = streamFactory;
            PageNumber = pageNumber;
            TotalPages = totalPages;
            InitializationError = initializationError;
        }

        public string OriginalPath { get; }
        public int PageNumber { get; }
        public int TotalPages { get; }
        public Exception? InitializationError { get; }

        public static OcrWorkItem CreateFile(string path) =>
            new(path, () => Task.FromResult<Stream>(File.OpenRead(path)), 1, 1, null);

        public static OcrWorkItem CreatePdf(string path, int pageNumber, int totalPages) =>
            new(path, () => CreatePdfPageImageStreamAsync(path, pageNumber - 1), pageNumber, totalPages, null);

        public static OcrWorkItem CreateError(string path, Exception error) =>
            new(path, null, 1, 1, error);

        public async Task<Stream> OpenStreamAsync()
        {
            if (_streamFactory is null)
                throw new InvalidOperationException("No stream factory available.");

            return await _streamFactory();
        }
    }

    protected virtual void PlayBeep(BeepType type)
    {
        BeepPlayer.Play(type);
    }

    private void PlayBeepSafe(BeepType type)
    {
        try { PlayBeep(type); }
        catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"PlayBeep({type}) failed: {ex.Message}"); }
    }

    private Task PlayBeepSafeAsync(BeepType type) => Task.Run(() => PlayBeepSafe(type));

        [DllImport("user32.dll")]
        private static extern int GetSystemMetrics(int nIndex);

        private const int SM_XVIRTUALSCREEN = 76;
        private const int SM_YVIRTUALSCREEN = 77;
        private const int SM_CXVIRTUALSCREEN = 78;
        private const int SM_CYVIRTUALSCREEN = 79;

        /// <summary>
        /// Puts a region-selection overlay on screen and returns what the user selected, or
        /// null when they dismissed it.
        /// <para>
        /// Virtual so a test can stand in for it. Everything below this line reads the real
        /// desktop and shows a real window, so without a seam here nothing about the two
        /// screenshot methods could be tested at all — including what they now do when the
        /// clipboard will not take the picture (issue #360). Issue #304 wants the same seam
        /// for the same reason.
        /// </para>
        /// </summary>
        protected virtual async Task<SoftwareBitmap?> CaptureScreenshotAsync()
        {
            // Use GetSystemMetrics to retrieve the physical pixel bounds of the virtual screen.
            // This avoids the scaling issues associated with System.Windows.Forms.SystemInformation.VirtualScreen on high-DPI displays.
            int left = GetSystemMetrics(SM_XVIRTUALSCREEN);
            int top = GetSystemMetrics(SM_YVIRTUALSCREEN);
            int width = GetSystemMetrics(SM_CXVIRTUALSCREEN);
            int height = GetSystemMetrics(SM_CYVIRTUALSCREEN);

            var bounds = new Rectangle(left, top, width, height);

            IntPtr? hwnd = null;
            try
            {
                if (_window is not null)
                {
                    hwnd = WinRT.Interop.WindowNative.GetWindowHandle(_window);
                }
            }
            catch { }

        using Bitmap gdiBmp = new(bounds.Width, bounds.Height, PixelFormat.Format32bppPArgb);
        using (Graphics g = Graphics.FromImage(gdiBmp))
        {
            g.CopyFromScreen(bounds.Left, bounds.Top, 0, 0, bounds.Size);
        }

        // Fast path: copy GDI pixels directly into a SoftwareBitmap without PNG encode/decode
        SoftwareBitmap bmp;
        var gdiRect = new Rectangle(0, 0, gdiBmp.Width, gdiBmp.Height);
        var data = gdiBmp.LockBits(gdiRect, ImageLockMode.ReadOnly, PixelFormat.Format32bppPArgb);
        try
        {
            int srcStride = data.Stride;
            int pixelHeight = data.Height;
            int pixelWidth = data.Width;
            int length = Math.Abs(srcStride) * pixelHeight;
            byte[] pixels = new byte[length];
            System.Runtime.InteropServices.Marshal.Copy(data.Scan0, pixels, 0, length);

            if (_settings.AzureComputerVisionSettings?.InvertScreenshot == true)
            {
                InvertPixels(pixels);
            }

            var ibuffer = pixels.AsBuffer();
            bmp = new SoftwareBitmap(BitmapPixelFormat.Bgra8, pixelWidth, pixelHeight, BitmapAlphaMode.Premultiplied);
            bmp.CopyFromBuffer(ibuffer);
        }
        finally
        {
            gdiBmp.UnlockBits(data);
        }

        try
        {
            var overlay = _cachedOverlay ?? new RegionSelectionWindow();
            // Read per capture, not once at startup: the overlay is cached and reused, so a
            // change made in the Settings dialog has to reach the next capture (issue #373).
            overlay.PointerNudge = ReadPointerNudgeOptions();
            // InitializeAsync already calls UpdateBitmap; calling it again converted and
            // copied the whole virtual screen a second time for nothing (issue #229).
            await overlay.InitializeAsync(bmp);
            _activeOverlay = overlay;
            try
            {
                // Activate and show overlay (inside SelectRegionAsync), then play start beep asynchronously to avoid UI delay
                var selectTask = overlay.SelectRegionAsync();
                var beepTask = PlayBeepSafeAsync(BeepType.Start);
                Rect? selectionRect = await selectTask;
                await beepTask;
                if (selectionRect == null || selectionRect.Value.Width < 1 || selectionRect.Value.Height < 1)
                {
                    // Nothing was captured, so what follows is an error message and the shortcut
                    // the user configured to read it — both aimed at the window the overlay took
                    // the keyboard from. Waiting for the overlay to hand it back keeps that
                    // shortcut out of the overlay, which the 50 ms failure delay could otherwise
                    // beat (issue #342). Only on this branch: a capture that produced an image
                    // goes on to spend hundreds of milliseconds reading it.
                    //
                    // Released before the wait rather than in the finally below. The overlay is
                    // already hidden by now, and a hotkey press arriving during the wait is
                    // answered by bringing the active overlay to the front — which would put
                    // this one back on screen after it had finished standing down.
                    _activeOverlay = null;
                    await overlay.ForegroundHandedBack;
                    return null;
                }
                return await CropBitmapAsync(bmp, selectionRect.Value);
            }
            finally
            {
                _activeOverlay = null;
            }
        }
        finally
        {
            bmp.Dispose();
        }
    }

    /// <summary>
    /// The pointer-nudge settings, as the overlay wants them. Off whenever the OCR settings are
    /// missing altogether, which is the same answer a fresh settings file gives.
    /// </summary>
    private PointerNudgeOptions ReadPointerNudgeOptions()
    {
        var ocr = _settings.AzureComputerVisionSettings;
        if (ocr is null || !ocr.NudgePointerDuringCapture)
            return PointerNudgeOptions.Off;

        return new PointerNudgeOptions(
            true,
            ocr.PointerNudgeIntervalMilliseconds,
            ocr.PointerNudgeDurationMilliseconds,
            ocr.PointerNudgeDistancePixels);
    }

    private static async Task<SoftwareBitmap> CropBitmapAsync(SoftwareBitmap src, Rect rect)
    {
        using InMemoryRandomAccessStream stream = new();
        BitmapEncoder encoder = await BitmapEncoder.CreateAsync(BitmapEncoder.PngEncoderId, stream);
        encoder.SetSoftwareBitmap(src);
        await encoder.FlushAsync();
        stream.Seek(0);

        BitmapDecoder decoder = await BitmapDecoder.CreateAsync(stream);
        BitmapBounds bounds = new()
        {
            X = (uint)rect.X,
            Y = (uint)rect.Y,
            Width = (uint)rect.Width,
            Height = (uint)rect.Height
        };
        BitmapTransform transform = new() { Bounds = bounds };
        return await decoder.GetSoftwareBitmapAsync(decoder.BitmapPixelFormat, decoder.BitmapAlphaMode, transform, ExifOrientationMode.IgnoreExifOrientation, ColorManagementMode.DoNotColorManage);
    }

    private static void InvertPixels(byte[] pixels)
    {
        // Assuming 32bpp (4 bytes per pixel: B, G, R, A)
        // Parallelize for performance on large screenshots
        Parallel.For(0, pixels.Length / 4, i =>
        {
            int offset = i * 4;
            pixels[offset] = (byte)(255 - pixels[offset]);         // B
            pixels[offset + 1] = (byte)(255 - pixels[offset + 1]); // G
            pixels[offset + 2] = (byte)(255 - pixels[offset + 2]); // R
            // Alpha at offset+3 is left alone
        });
    }
}
