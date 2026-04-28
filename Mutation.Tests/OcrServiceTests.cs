using System;
using System.Collections;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using CognitiveSupport;
using Mutation.Ui.Services;
using PdfSharp.Pdf;

namespace Mutation.Tests;

public class OcrServiceTests
{
	[Fact]
        public async Task RequestRateLimiter_EnforcesWindowBetweenRequests()
        {
                Type? limiterType = typeof(OcrService).GetNestedType("RequestRateLimiter", BindingFlags.NonPublic);
                Assert.NotNull(limiterType);
                object? limiter = Activator.CreateInstance(limiterType!, 1, TimeSpan.FromMilliseconds(100));
		Assert.NotNull(limiter);
		MethodInfo? waitAsync = limiterType!.GetMethod("WaitAsync", BindingFlags.Public | BindingFlags.Instance);
		Assert.NotNull(waitAsync);

		Task first = (Task)waitAsync!.Invoke(limiter, new object[] { CancellationToken.None })!;
		await first.ConfigureAwait(false);

		var stopwatch = Stopwatch.StartNew();
		Task second = (Task)waitAsync.Invoke(limiter, new object[] { CancellationToken.None })!;
		await second.ConfigureAwait(false);
		stopwatch.Stop();

                Assert.True(stopwatch.Elapsed >= TimeSpan.FromMilliseconds(85));
        }

	[Fact]
	public async Task RequestRateLimiter_RespectsLimitAcrossMultipleCalls()
	{
		Type? limiterType = typeof(OcrService).GetNestedType("RequestRateLimiter", BindingFlags.NonPublic);
		Assert.NotNull(limiterType);
		object? limiter = Activator.CreateInstance(limiterType!, 2, TimeSpan.FromMilliseconds(120));
		Assert.NotNull(limiter);
		MethodInfo? waitAsync = limiterType!.GetMethod("WaitAsync", BindingFlags.Public | BindingFlags.Instance);
		Assert.NotNull(waitAsync);

		await ((Task)waitAsync!.Invoke(limiter, new object[] { CancellationToken.None })!).ConfigureAwait(false);
		await ((Task)waitAsync.Invoke(limiter, new object[] { CancellationToken.None })!).ConfigureAwait(false);

		var stopwatch = Stopwatch.StartNew();
		await ((Task)waitAsync.Invoke(limiter, new object[] { CancellationToken.None })!).ConfigureAwait(false);
		stopwatch.Stop();

		Assert.True(stopwatch.Elapsed >= TimeSpan.FromMilliseconds(100));
	}

	[Fact]
	public async Task RequestRateLimiter_HonorsCancellation()
	{
		Type? limiterType = typeof(OcrService).GetNestedType("RequestRateLimiter", BindingFlags.NonPublic);
		Assert.NotNull(limiterType);
		object? limiter = Activator.CreateInstance(limiterType!, 1, TimeSpan.FromMilliseconds(250));
		Assert.NotNull(limiter);
		MethodInfo? waitAsync = limiterType!.GetMethod("WaitAsync", BindingFlags.Public | BindingFlags.Instance);
		Assert.NotNull(waitAsync);

		await ((Task)waitAsync!.Invoke(limiter, new object[] { CancellationToken.None })!).ConfigureAwait(false);

		using var cts = new CancellationTokenSource();
		cts.CancelAfter(TimeSpan.FromMilliseconds(50));

		await Assert.ThrowsAsync<TaskCanceledException>(async () =>
		{
			await ((Task)waitAsync.Invoke(limiter, new object[] { cts.Token })!).ConfigureAwait(false);
		}).ConfigureAwait(false);
	}

	[Fact]
	public async Task RequestRateLimiter_AllowsRequestsAfterWindowExpires()
	{
		Type? limiterType = typeof(OcrService).GetNestedType("RequestRateLimiter", BindingFlags.NonPublic);
		Assert.NotNull(limiterType);
		object? limiter = Activator.CreateInstance(limiterType!, 2, TimeSpan.FromMilliseconds(80));
		Assert.NotNull(limiter);
		MethodInfo? waitAsync = limiterType!.GetMethod("WaitAsync", BindingFlags.Public | BindingFlags.Instance);
		Assert.NotNull(waitAsync);

		await ((Task)waitAsync!.Invoke(limiter, new object[] { CancellationToken.None })!).ConfigureAwait(false);
		await ((Task)waitAsync.Invoke(limiter, new object[] { CancellationToken.None })!).ConfigureAwait(false);

		await Task.Delay(TimeSpan.FromMilliseconds(120)).ConfigureAwait(false);

		var stopwatch = Stopwatch.StartNew();
		await ((Task)waitAsync.Invoke(limiter, new object[] { CancellationToken.None })!).ConfigureAwait(false);
		stopwatch.Stop();

		Assert.True(stopwatch.Elapsed < TimeSpan.FromMilliseconds(40));
	}

	[Fact]
	public async Task SharedRateLimiter_ResetClearsWindowState()
	{
		Type? limiterType = typeof(OcrService).GetNestedType("RequestRateLimiter", BindingFlags.NonPublic);
		Assert.NotNull(limiterType);
		FieldInfo? sharedField = typeof(OcrService).GetField("SharedRateLimiter", BindingFlags.NonPublic | BindingFlags.Static);
		Assert.NotNull(sharedField);
		object? limiter = sharedField!.GetValue(null);
		Assert.NotNull(limiter);
		MethodInfo? reset = limiterType!.GetMethod("Reset", BindingFlags.NonPublic | BindingFlags.Instance);
		Assert.NotNull(reset);
		reset!.Invoke(limiter, Array.Empty<object>());
		MethodInfo? waitAsync = limiterType.GetMethod("WaitAsync", BindingFlags.Public | BindingFlags.Instance);
		Assert.NotNull(waitAsync);

		await ((Task)waitAsync!.Invoke(limiter, new object[] { CancellationToken.None })!).ConfigureAwait(false);
		await ((Task)waitAsync.Invoke(limiter, new object[] { CancellationToken.None })!).ConfigureAwait(false);

		OcrService.OcrRequestWindowState populated = OcrService.GetSharedRequestWindowState();
		Assert.Equal(2, populated.RequestsInWindow);
		Assert.Equal(2, populated.TotalRequestsGranted);
		Assert.True(populated.LastRequestUtc.HasValue);

		reset.Invoke(limiter, Array.Empty<object>());

		OcrService.OcrRequestWindowState cleared = OcrService.GetSharedRequestWindowState();
		Assert.Equal(0, cleared.RequestsInWindow);
		Assert.Equal(0, cleared.TotalRequestsGranted);
		Assert.False(cleared.LastRequestUtc.HasValue);
	}

	[Fact]
	public async Task SharedRateLimiter_TracksUsageAcrossOperations()
	{
		Type? limiterType = typeof(OcrService).GetNestedType("RequestRateLimiter", BindingFlags.NonPublic);
		Assert.NotNull(limiterType);
		FieldInfo? sharedField = typeof(OcrService).GetField("SharedRateLimiter", BindingFlags.NonPublic | BindingFlags.Static);
		Assert.NotNull(sharedField);
		object? limiter = sharedField!.GetValue(null);
		Assert.NotNull(limiter);
		MethodInfo? reset = limiterType!.GetMethod("Reset", BindingFlags.NonPublic | BindingFlags.Instance);
		Assert.NotNull(reset);
		reset!.Invoke(limiter, Array.Empty<object>());
		MethodInfo? waitAsync = limiterType.GetMethod("WaitAsync", BindingFlags.Public | BindingFlags.Instance);
		Assert.NotNull(waitAsync);

		for (int i = 0; i < 3; i++)
		{
			await ((Task)waitAsync!.Invoke(limiter, new object[] { CancellationToken.None })!).ConfigureAwait(false);
		}

		OcrService.OcrRequestWindowState state = OcrService.GetSharedRequestWindowState();
		Assert.Equal(20, state.Limit);
		Assert.Equal(TimeSpan.FromMinutes(1), state.WindowLength);
		Assert.Equal(3, state.TotalRequestsGranted);
		Assert.Equal(3, state.RequestsInWindow);
		Assert.True(state.TimeUntilWindowReset >= TimeSpan.Zero);
		Assert.True(state.LastRequestUtc.HasValue);
	}


	[Fact]
	public async Task SharedRateLimiter_PreventsExceedingLimitPerWindowAcrossSequentialRuns()
	{
		Type? limiterType = typeof(OcrService).GetNestedType("RequestRateLimiter", BindingFlags.NonPublic);
		Assert.NotNull(limiterType);
		FieldInfo? sharedField = typeof(OcrService).GetField("SharedRateLimiter", BindingFlags.NonPublic | BindingFlags.Static);
		Assert.NotNull(sharedField);
		object? originalLimiter = sharedField!.GetValue(null);
		Assert.NotNull(originalLimiter);

		MethodInfo? reset = limiterType!.GetMethod("Reset", BindingFlags.NonPublic | BindingFlags.Instance);
		Assert.NotNull(reset);
		reset!.Invoke(originalLimiter, Array.Empty<object>());

		object? testLimiter = Activator.CreateInstance(limiterType!, 3, TimeSpan.FromMilliseconds(80));
		Assert.NotNull(testLimiter);
		sharedField.SetValue(null, testLimiter);

		try
		{
			MethodInfo? waitAsync = limiterType.GetMethod("WaitAsync", BindingFlags.Public | BindingFlags.Instance);
			Assert.NotNull(waitAsync);

			var stopwatch = Stopwatch.StartNew();
			for (int i = 0; i < 6; i++)
			{
				await ((Task)waitAsync!.Invoke(testLimiter, new object[] { CancellationToken.None })!).ConfigureAwait(false);
				OcrService.OcrRequestWindowState snapshot = OcrService.GetSharedRequestWindowState();
				Assert.InRange(snapshot.RequestsInWindow, 1, 3);
				Assert.True(snapshot.TotalRequestsGranted >= i + 1);
			}
			stopwatch.Stop();

			Assert.True(stopwatch.Elapsed >= TimeSpan.FromMilliseconds(80));
		}
		finally
		{
			sharedField.SetValue(null, originalLimiter);
			reset.Invoke(originalLimiter, Array.Empty<object>());
		}
	}

	[Fact]
	public async Task SharedRateLimiter_AllowsFreshBatchAfterWindowAcrossSequentialRuns()
	{
		Type? limiterType = typeof(OcrService).GetNestedType("RequestRateLimiter", BindingFlags.NonPublic);
		Assert.NotNull(limiterType);
		FieldInfo? sharedField = typeof(OcrService).GetField("SharedRateLimiter", BindingFlags.NonPublic | BindingFlags.Static);
		Assert.NotNull(sharedField);
		object? originalLimiter = sharedField!.GetValue(null);
		Assert.NotNull(originalLimiter);

		MethodInfo? reset = limiterType!.GetMethod("Reset", BindingFlags.NonPublic | BindingFlags.Instance);
		Assert.NotNull(reset);
		reset!.Invoke(originalLimiter, Array.Empty<object>());

		object? testLimiter = Activator.CreateInstance(limiterType!, 3, TimeSpan.FromMilliseconds(90));
		Assert.NotNull(testLimiter);
		sharedField.SetValue(null, testLimiter);

		try
		{
			MethodInfo? waitAsync = limiterType.GetMethod("WaitAsync", BindingFlags.Public | BindingFlags.Instance);
			Assert.NotNull(waitAsync);

			for (int i = 0; i < 3; i++)
			{
				await ((Task)waitAsync!.Invoke(testLimiter, new object[] { CancellationToken.None })!).ConfigureAwait(false);
			}

			OcrService.OcrRequestWindowState saturated = OcrService.GetSharedRequestWindowState();
			Assert.Equal(3, saturated.RequestsInWindow);

			await Task.Delay(TimeSpan.FromMilliseconds(120)).ConfigureAwait(false);

			OcrService.OcrRequestWindowState afterWait = OcrService.GetSharedRequestWindowState();
			Assert.Equal(0, afterWait.RequestsInWindow);

			var stopwatch = Stopwatch.StartNew();
			for (int i = 0; i < 3; i++)
			{
				await ((Task)waitAsync!.Invoke(testLimiter, new object[] { CancellationToken.None })!).ConfigureAwait(false);
			}
			stopwatch.Stop();

			Assert.True(stopwatch.Elapsed < TimeSpan.FromMilliseconds(90));
		}
		finally
		{
			sharedField.SetValue(null, originalLimiter);
			reset.Invoke(originalLimiter, Array.Empty<object>());
		}
	}

	[Fact]
	public async Task SharedRateLimiter_ScalesTwentyPerMinuteThrottleAcrossRuns()
	{
		Type? limiterType = typeof(OcrService).GetNestedType("RequestRateLimiter", BindingFlags.NonPublic);
		Assert.NotNull(limiterType);
		FieldInfo? sharedField = typeof(OcrService).GetField("SharedRateLimiter", BindingFlags.NonPublic | BindingFlags.Static);
		Assert.NotNull(sharedField);
		object? originalLimiter = sharedField!.GetValue(null);
		Assert.NotNull(originalLimiter);

		MethodInfo? reset = limiterType!.GetMethod("Reset", BindingFlags.NonPublic | BindingFlags.Instance);
		Assert.NotNull(reset);
		reset!.Invoke(originalLimiter, Array.Empty<object>());

		// Use a scaled window mirroring Azure's 20 requests per minute limit (4 per 120 ms in tests)
		object? testLimiter = Activator.CreateInstance(limiterType!, 4, TimeSpan.FromMilliseconds(120));
		Assert.NotNull(testLimiter);
		sharedField.SetValue(null, testLimiter);

		try
		{
			MethodInfo? waitAsync = limiterType.GetMethod("WaitAsync", BindingFlags.Public | BindingFlags.Instance);
			Assert.NotNull(waitAsync);

			var stopwatch = Stopwatch.StartNew();
			for (int i = 0; i < 10; i++)
			{
				await ((Task)waitAsync!.Invoke(testLimiter, new object[] { CancellationToken.None })!).ConfigureAwait(false);
				OcrService.OcrRequestWindowState snapshot = OcrService.GetSharedRequestWindowState();
				Assert.InRange(snapshot.RequestsInWindow, 1, 4);
				Assert.True(snapshot.TotalRequestsGranted >= i + 1);
			}
			stopwatch.Stop();

			Assert.True(stopwatch.Elapsed >= TimeSpan.FromMilliseconds(200));
		}
		finally
		{
			sharedField.SetValue(null, originalLimiter);
			reset.Invoke(originalLimiter, Array.Empty<object>());
		}
	}

	[Fact]
	public void ExpandFile_CreatesPdfWorkItemPerPage()
	{
		string path = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".pdf");
		try
		{
			using (var document = new PdfDocument())
			{
				document.Pages.Add();
				document.Pages.Add();
				document.Pages.Add();
				document.Save(path);
			}

			MethodInfo? expandFile = typeof(OcrManager).GetMethod("ExpandFile", BindingFlags.NonPublic | BindingFlags.Static);
			Assert.NotNull(expandFile);

			var raw = expandFile!.Invoke(null, new object?[] { path });
			Assert.NotNull(raw);
			var items = ((IEnumerable)raw!).Cast<object>().ToList();
			Assert.Equal(3, items.Count);

			Type itemType = items[0].GetType();
			PropertyInfo? pageNumber = itemType.GetProperty("PageNumber", BindingFlags.Public | BindingFlags.Instance);
			PropertyInfo? totalPages = itemType.GetProperty("TotalPages", BindingFlags.Public | BindingFlags.Instance);
			PropertyInfo? error = itemType.GetProperty("InitializationError", BindingFlags.Public | BindingFlags.Instance);
			Assert.NotNull(pageNumber);
			Assert.NotNull(totalPages);
			Assert.NotNull(error);

			for (int i = 0; i < items.Count; i++)
			{
				object item = items[i];
				Assert.Equal(i + 1, (int)pageNumber!.GetValue(item)!);
				Assert.Equal(3, (int)totalPages!.GetValue(item)!);
				Assert.Null(error!.GetValue(item));
			}
		}
		finally
		{
			if (File.Exists(path))
			{
				File.Delete(path);
			}
		}
	}

	// ---------------------------------------------------------------------
	// GetPerRequestTimeout / CreatePerRequestCancellationTokenSource
	// ---------------------------------------------------------------------

	[Theory]
	[InlineData(-5, 1)]   // floor at 1 second (Math.Max(1, …))
	[InlineData(0, 1)]
	[InlineData(1, 1)]
	[InlineData(30, 30)]
	[InlineData(60, 60)]
	[InlineData(120, 60)] // ceiling at MaxTimeoutSeconds (60)
	[InlineData(3600, 60)]
	public void GetPerRequestTimeout_ClampsToValidRange(int configuredSeconds, int expectedSeconds)
	{
		var service = new OcrService("dummy-key", "https://example.com/", configuredSeconds);

		MethodInfo? method = typeof(OcrService).GetMethod("GetPerRequestTimeout", BindingFlags.NonPublic | BindingFlags.Instance);
		Assert.NotNull(method);
		var timeout = (TimeSpan)method!.Invoke(service, null)!;

		Assert.Equal(TimeSpan.FromSeconds(expectedSeconds), timeout);
	}

	[Fact]
	public void Constructor_FloorsTimeoutAtOneSecond()
	{
		// _timeoutSeconds is clamped via Math.Max(1, Math.Min(timeoutSeconds, 60)).
		var service = new OcrService("dummy-key", "https://example.com/", -100);

		FieldInfo? field = typeof(OcrService).GetField("_timeoutSeconds", BindingFlags.NonPublic | BindingFlags.Instance);
		Assert.NotNull(field);
		Assert.Equal(1, (int)field!.GetValue(service)!);
	}

	[Fact]
	public void Constructor_CeilsTimeoutAtMaximum()
	{
		var service = new OcrService("dummy-key", "https://example.com/", 9999);

		FieldInfo? field = typeof(OcrService).GetField("_timeoutSeconds", BindingFlags.NonPublic | BindingFlags.Instance);
		Assert.NotNull(field);
		Assert.Equal(60, (int)field!.GetValue(service)!);
	}

	[Fact]
	public void Constructor_NullSubscriptionKey_Throws()
	{
		Assert.Throws<ArgumentNullException>(() => new OcrService(null, "https://example.com/", 10));
	}

	[Fact]
	public void Constructor_NullEndpoint_Throws()
	{
		Assert.Throws<ArgumentNullException>(() => new OcrService("key", null, 10));
	}

	[Fact]
	public void CreatePerRequestCancellationTokenSource_LinkedAndScheduledToCancel()
	{
		var service = new OcrService("dummy-key", "https://example.com/", 1);

		MethodInfo? method = typeof(OcrService).GetMethod("CreatePerRequestCancellationTokenSource", BindingFlags.NonPublic | BindingFlags.Instance);
		Assert.NotNull(method);

		using var overall = new CancellationTokenSource();
		using var cts = (CancellationTokenSource)method!.Invoke(service, new object[] { overall.Token })!;

		Assert.False(cts.IsCancellationRequested);

		overall.Cancel();
		Assert.True(cts.Token.IsCancellationRequested);
	}

	// ---------------------------------------------------------------------
	// EnsureMinimumImageSize
	// ---------------------------------------------------------------------

	[Fact]
	public void EnsureMinimumImageSize_ImageAlreadyMeetsMinimum_ReturnsSameStream()
	{
		var service = new OcrService("dummy-key", "https://example.com/", 10);
		using var stream = CreatePngStream(width: 100, height: 100);

		MethodInfo? method = typeof(OcrService).GetMethod("EnsureMinimumImageSize", BindingFlags.NonPublic | BindingFlags.Instance);
		Assert.NotNull(method);

		var resultStream = (Stream)method!.Invoke(service, new object[] { stream })!;

		Assert.Same(stream, resultStream);
		Assert.Equal(0, resultStream.Position);
	}

	[Fact]
	public void EnsureMinimumImageSize_TooSmallWidth_PadsToMinimum()
	{
		var service = new OcrService("dummy-key", "https://example.com/", 10);
		using var stream = CreatePngStream(width: 10, height: 80);

		MethodInfo? method = typeof(OcrService).GetMethod("EnsureMinimumImageSize", BindingFlags.NonPublic | BindingFlags.Instance);
		Assert.NotNull(method);

		var resultStream = (Stream)method!.Invoke(service, new object[] { stream })!;

		// A new MemoryStream is returned with the padded image.
		Assert.NotSame(stream, resultStream);
		using var paddedImage = System.Drawing.Image.FromStream(resultStream);
		Assert.True(paddedImage.Width >= 50);
		Assert.True(paddedImage.Height >= 80);
	}

	[Fact]
	public void EnsureMinimumImageSize_TooSmallHeight_PadsToMinimum()
	{
		var service = new OcrService("dummy-key", "https://example.com/", 10);
		using var stream = CreatePngStream(width: 80, height: 10);

		MethodInfo? method = typeof(OcrService).GetMethod("EnsureMinimumImageSize", BindingFlags.NonPublic | BindingFlags.Instance);
		Assert.NotNull(method);

		var resultStream = (Stream)method!.Invoke(service, new object[] { stream })!;

		Assert.NotSame(stream, resultStream);
		using var paddedImage = System.Drawing.Image.FromStream(resultStream);
		Assert.True(paddedImage.Width >= 80);
		Assert.True(paddedImage.Height >= 50);
	}

	[Fact]
	public void EnsureMinimumImageSize_InvalidImageData_ReturnsOriginalStream()
	{
		var service = new OcrService("dummy-key", "https://example.com/", 10);
		using var stream = new MemoryStream(new byte[] { 0x01, 0x02, 0x03 });

		MethodInfo? method = typeof(OcrService).GetMethod("EnsureMinimumImageSize", BindingFlags.NonPublic | BindingFlags.Instance);
		Assert.NotNull(method);

		var resultStream = (Stream)method!.Invoke(service, new object[] { stream })!;

		Assert.Same(stream, resultStream);
		Assert.Equal(0, resultStream.Position);
	}

	[Fact]
	public void EnsureMinimumImageSize_NonSeekableStream_ReturnsAsIs()
	{
		var service = new OcrService("dummy-key", "https://example.com/", 10);
		using var inner = CreatePngStream(width: 10, height: 10);
		using var nonSeekable = new NonSeekableStreamWrapper(inner);

		MethodInfo? method = typeof(OcrService).GetMethod("EnsureMinimumImageSize", BindingFlags.NonPublic | BindingFlags.Instance);
		Assert.NotNull(method);

		var resultStream = (Stream)method!.Invoke(service, new object[] { nonSeekable })!;

		Assert.Same(nonSeekable, resultStream);
	}

	private static MemoryStream CreatePngStream(int width, int height)
	{
		using var bitmap = new System.Drawing.Bitmap(width, height);
		using (var graphics = System.Drawing.Graphics.FromImage(bitmap))
			graphics.Clear(System.Drawing.Color.White);

		var ms = new MemoryStream();
		bitmap.Save(ms, System.Drawing.Imaging.ImageFormat.Png);
		ms.Seek(0, SeekOrigin.Begin);
		return ms;
	}

	private sealed class NonSeekableStreamWrapper : Stream
	{
		private readonly Stream _inner;
		public NonSeekableStreamWrapper(Stream inner) { _inner = inner; }
		public override bool CanRead => _inner.CanRead;
		public override bool CanSeek => false;
		public override bool CanWrite => false;
		public override long Length => throw new NotSupportedException();
		public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
		public override void Flush() => _inner.Flush();
		public override int Read(byte[] buffer, int offset, int count) => _inner.Read(buffer, offset, count);
		public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
		public override void SetLength(long value) => throw new NotSupportedException();
		public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
	}
}
