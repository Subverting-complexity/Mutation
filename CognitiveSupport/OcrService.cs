using CognitiveSupport.Extensions;
using Azure;
using Azure.AI.Vision.ImageAnalysis;
using Polly;
using Polly.Timeout;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.Globalization;
using System.Linq;
using System.Net.Http.Headers;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;

namespace CognitiveSupport;

public class OcrService : IOcrService, IDisposable
{
        private const int MinimumImageWidth = 50;
        private const int MinimumImageHeight = 50;
        private const int MaxTimeoutSeconds = 60;

        private string SubscriptionKey { get; }
        private string Endpoint { get; }
        private ImageAnalysisClient ImageAnalysisClient { get; }
        private readonly int _timeoutSeconds;
	private static RequestRateLimiter SharedRateLimiter = new(20, TimeSpan.FromMinutes(1));

	public OcrService(string? subscriptionKey, string? endpoint, int timeoutSeconds = 30)
	{
		SubscriptionKey = subscriptionKey ?? throw new ArgumentNullException(nameof(subscriptionKey));
		Endpoint = endpoint ?? throw new ArgumentNullException(nameof(endpoint));
		ImageAnalysisClient = CreateImageAnalysisClient(Endpoint, SubscriptionKey);
		_timeoutSeconds = Math.Max(1, Math.Min(timeoutSeconds, MaxTimeoutSeconds));
	}

	public Task<string> ExtractText(
		OcrReadingOrder ocrReadingOrder,
		Stream imageStream,
		CancellationToken overallCancellationToken)
	{
		return Read(ocrReadingOrder, imageStream, overallCancellationToken);
	}

	private static ImageAnalysisClient CreateImageAnalysisClient(string endpoint, string key) =>
		 new(new Uri(endpoint), new AzureKeyCredential(key));

	private static RetryAttempts CreateRetry() =>
		new(retryCount: 3,
			new PredicateBuilder()
				.Handle<HttpRequestException>()
				.Handle<RequestFailedException>(ex => ex.Status == 429 || ex.Status >= 500)
				.Handle<TimeoutRejectedException>()
				.Handle<TaskCanceledException>());

	private TimeSpan GetPerRequestTimeout() => TimeSpan.FromSeconds(Math.Max(1, Math.Min(_timeoutSeconds, MaxTimeoutSeconds)));

	private CancellationTokenSource CreatePerRequestCancellationTokenSource(CancellationToken overallToken)
	{
		var cts = CancellationTokenSource.CreateLinkedTokenSource(overallToken);
		cts.CancelAfter(GetPerRequestTimeout());
		return cts;
	}


	private async Task<string> ExecuteReadInternal(
		OcrReadingOrder ocrReadingOrder,
		Stream imageStream,
		int attempt,
		CancellationToken overallCancellationToken)
	{
		// Beep swallows the first, non-retry attempt itself; retries beep once per attempt.
		this.Beep(attempt);

		imageStream.Seek(0, SeekOrigin.Begin);

		return await ReadInternal(ocrReadingOrder, imageStream, overallCancellationToken).ConfigureAwait(false);
	}


	private async Task<string> Read(
		OcrReadingOrder ocrReadingOrder,
		Stream imageStream,
		CancellationToken overallCancellationToken)
	{
		// Buffer the stream into a byte array so we can create a new stream for each retry
		byte[] imageBytes = BufferImage(imageStream);

		var retry = CreateRetry();

		return await retry.Pipeline.ExecuteAsync(
			async overallToken =>
			{
				var ms = new MemoryStream(imageBytes, writable: false);
				return await ExecuteReadInternal(ocrReadingOrder, ms, retry.Attempt, overallToken).ConfigureAwait(false);
			},
			overallCancellationToken).ConfigureAwait(false);
	}

	/// <summary>
	/// Copies the image into a byte array that holds exactly the image and nothing else.
	/// A <see cref="MemoryStream"/>'s backing array is its <em>capacity</em> buffer, so the
	/// segment's Offset and Count have to be honoured: uploading the raw array would POST
	/// the unused tail as image payload, and would send the wrong region entirely for a
	/// stream built over a slice of a larger array (issue #239).
	/// </summary>
	private static byte[] BufferImage(Stream imageStream)
	{
		if (imageStream is MemoryStream ms && ms.TryGetBuffer(out ArraySegment<byte> buffer) && buffer.Array != null)
			return buffer.ToArray();

		using var tempMs = new MemoryStream();
		imageStream.Seek(0, SeekOrigin.Begin);
		imageStream.CopyTo(tempMs);
		return tempMs.ToArray();
	}

        private Stream EnsureMinimumImageSize(Stream imageStream)
        {
                if (!imageStream.CanSeek)
                        return imageStream;

                imageStream.Seek(0, SeekOrigin.Begin);

                try
                {
                        using var image = Image.FromStream(imageStream);

                        if (image.Width >= MinimumImageWidth && image.Height >= MinimumImageHeight)
                        {
                                imageStream.Seek(0, SeekOrigin.Begin);
                                return imageStream;
                        }

                        int newWidth = Math.Max(MinimumImageWidth, image.Width);
                        int newHeight = Math.Max(MinimumImageHeight, image.Height);

                        using var paddedImage = new Bitmap(newWidth, newHeight);
                        using (var graphics = Graphics.FromImage(paddedImage))
                        {
                                graphics.Clear(Color.White);
                                int offsetX = (newWidth - image.Width) / 2;
                                int offsetY = (newHeight - image.Height) / 2;
                                graphics.DrawImage(image, offsetX, offsetY);
                        }

                        var paddedStream = new MemoryStream();
                        paddedImage.Save(paddedStream, ImageFormat.Png);
                        paddedStream.Seek(0, SeekOrigin.Begin);

                        return paddedStream;
                }
                catch (Exception ex) when (ex is ArgumentException or ExternalException or OutOfMemoryException or InvalidOperationException)
                {
                        imageStream.Seek(0, SeekOrigin.Begin);
                        return imageStream;
                }
        }

	public static OcrRequestWindowState GetSharedRequestWindowState()
	{
		return SharedRateLimiter.GetSnapshot();
	}

	private async Task<string> ReadInternal(
		OcrReadingOrder ocrReadingOrder,
		Stream imageStream,
		CancellationToken overallCancellationToken)
	{
		imageStream = EnsureMinimumImageSize(imageStream);

		await SharedRateLimiter.WaitAsync(overallCancellationToken).ConfigureAwait(false);

		using var requestCts = CreatePerRequestCancellationTokenSource(overallCancellationToken);
		
		var binaryData = BinaryData.FromStream(imageStream);

		ImageAnalysisResult result = await ImageAnalysisClient.AnalyzeAsync(
			binaryData,
			VisualFeatures.Read,
			new ImageAnalysisOptions { },
			cancellationToken: requestCts.Token)
			.ConfigureAwait(false);

		return ExtractTextFromResults(result, ocrReadingOrder);
	}

        // Azure Image Analysis 4.0 exposes no reading-order request option, so the order is
        // applied here to the recognised lines. See OcrReadingOrderSorter (issue #217).
        private static string ExtractTextFromResults(ImageAnalysisResult result, OcrReadingOrder ocrReadingOrder)
        {
                var lines = FlattenLines(result);
                return OcrReadingOrderSorter.ToText(OcrReadingOrderSorter.Sort(lines, ocrReadingOrder));
        }

        private static IReadOnlyList<OcrTextLine> FlattenLines(ImageAnalysisResult result)
        {
                var lines = new List<OcrTextLine>();

                if (result.Read?.Blocks != null)
                {
                        foreach (var block in result.Read.Blocks)
                        {
                                foreach (var line in block.Lines)
                                {
                                        lines.Add(ToTextLine(line));
                                }
                        }
                }

                return lines;
        }

        private static OcrTextLine ToTextLine(DetectedTextLine line)
        {
                var polygon = line.BoundingPolygon;
                if (polygon is null || polygon.Count == 0)
                        return new OcrTextLine(line.Text, 0, 0, 0, 0);

                double left = polygon.Min(p => (double)p.X);
                double top = polygon.Min(p => (double)p.Y);
                double right = polygon.Max(p => (double)p.X);
                double bottom = polygon.Max(p => (double)p.Y);
                return new OcrTextLine(line.Text, left, top, right, bottom);
        }

        public void Dispose()
        {
                // ImageAnalysisClient does not implement IDisposable
        }

		public readonly record struct OcrRequestWindowState(
		int Limit,
		TimeSpan WindowLength,
		int RequestsInWindow,
		long TotalRequestsGranted,
		DateTimeOffset? LastRequestUtc,
		TimeSpan TimeUntilWindowReset);

		private sealed class RequestRateLimiter
		{
			private readonly int _limit;
			private readonly TimeSpan _window;
			private readonly Queue<DateTime> _timestamps = new();
			private readonly object _sync = new();
			private long _totalGranted;
			private DateTime? _lastGrantedUtc;

			public RequestRateLimiter(int limit, TimeSpan window)
			{
				_limit = limit;
				_window = window;
			}

			/// <summary>
		/// Waits asynchronously until a request slot is available within the rate limit window.
		/// Note: This uses a lock-based pattern which is safe here because the lock is only held
		/// for brief synchronous operations (queue manipulation), and all async work (Task.Delay)
		/// occurs outside the lock.
		/// </summary>
		public async Task WaitAsync(CancellationToken token)
			{
				while (true)
				{
					token.ThrowIfCancellationRequested();
					TimeSpan delay = TimeSpan.Zero;

					lock (_sync)
					{
						var now = DateTime.UtcNow;
						RemoveExpired(now);

						if (_timestamps.Count < _limit)
						{
							_timestamps.Enqueue(now);
							_totalGranted++;
							_lastGrantedUtc = now;
							return;
						}

						var oldest = _timestamps.Peek();
						delay = _window - (now - oldest);
						if (delay < TimeSpan.Zero)
						{
							delay = TimeSpan.Zero;
						}
					}

					if (delay > TimeSpan.Zero)
					{
						await Task.Delay(delay, token).ConfigureAwait(false);
					}
				}
			}

			public OcrRequestWindowState GetSnapshot()
			{
				lock (_sync)
				{
					var now = DateTime.UtcNow;
					RemoveExpired(now);

					TimeSpan timeUntilReset = TimeSpan.Zero;
					if (_timestamps.Count > 0)
					{
						var oldest = _timestamps.Peek();
						timeUntilReset = _window - (now - oldest);
						if (timeUntilReset < TimeSpan.Zero)
						{
							timeUntilReset = TimeSpan.Zero;
						}
					}

					DateTimeOffset? lastRequest = _lastGrantedUtc.HasValue
					? new DateTimeOffset(DateTime.SpecifyKind(_lastGrantedUtc.Value, DateTimeKind.Utc))
					: null;

					return new OcrRequestWindowState(
					_limit,
					_window,
					_timestamps.Count,
					_totalGranted,
					lastRequest,
					timeUntilReset);
				}
			}

			private void RemoveExpired(DateTime now)
			{
				while (_timestamps.Count > 0 && now - _timestamps.Peek() >= _window)
				{
					_timestamps.Dequeue();
				}
			}

			// Not dead code: the test suite invokes this reflectively to clear the shared
			// static limiter between runs, which is the only way one test's requests can
			// be kept out of the next one's window. Flagged as unused by issue #239's
			// notes; it is reachable, so it stays.
			private void Reset()
			{
				lock (_sync)
				{
					_timestamps.Clear();
					_totalGranted = 0;
					_lastGrantedUtc = null;
				}
			}
		}
}
