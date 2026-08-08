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

// Several tests here reflectively swap OcrService's private static SharedRateLimiter.
// The collection keeps that swap from being visible to another class (issue #250).
[Collection(OcrServiceStaticsCollection.Name)]
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
		await first;

		var stopwatch = Stopwatch.StartNew();
		Task second = (Task)waitAsync.Invoke(limiter, new object[] { CancellationToken.None })!;
		await second;
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

		await ((Task)waitAsync!.Invoke(limiter, new object[] { CancellationToken.None })!);
		await ((Task)waitAsync.Invoke(limiter, new object[] { CancellationToken.None })!);

		var stopwatch = Stopwatch.StartNew();
		await ((Task)waitAsync.Invoke(limiter, new object[] { CancellationToken.None })!);
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

		await ((Task)waitAsync!.Invoke(limiter, new object[] { CancellationToken.None })!);

		using var cts = new CancellationTokenSource();
		cts.CancelAfter(TimeSpan.FromMilliseconds(50));

		// ThrowsAny, not ThrowsAsync<TaskCanceledException>: cancellation usually lands
		// inside Task.Delay, but if it fires while the limiter is between the lock and
		// the delay, ThrowIfCancellationRequested throws a plain OperationCanceledException.
		await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
		{
			await ((Task)waitAsync.Invoke(limiter, new object[] { cts.Token })!);
		});
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
		MethodInfo? getSnapshot = limiterType.GetMethod("GetSnapshot", BindingFlags.Public | BindingFlags.Instance);
		Assert.NotNull(getSnapshot);

		await ((Task)waitAsync!.Invoke(limiter, new object[] { CancellationToken.None })!);
		await ((Task)waitAsync.Invoke(limiter, new object[] { CancellationToken.None })!);

		var saturated = (OcrService.OcrRequestWindowState)getSnapshot!.Invoke(limiter, Array.Empty<object>())!;
		Assert.Equal(2, saturated.RequestsInWindow);

		await Task.Delay(TimeSpan.FromMilliseconds(120));

		// The window rolling over is the observable fact; asserting it directly beats
		// timing the next grant, which a GC pause on a loaded agent can blow past.
		// This one only gets more true under load — the wait was already past the
		// window, so a slow machine cannot make it fail.
		var expired = (OcrService.OcrRequestWindowState)getSnapshot.Invoke(limiter, Array.Empty<object>())!;
		Assert.Equal(0, expired.RequestsInWindow);

		await ((Task)waitAsync.Invoke(limiter, new object[] { CancellationToken.None })!);

		// A grant against an empty window cannot block, so the count of grants is the
		// whole proof. Deliberately not asserting RequestsInWindow here: that would go
		// stale the moment the window rolled again, putting the clock back in the test.
		var afterGrant = (OcrService.OcrRequestWindowState)getSnapshot.Invoke(limiter, Array.Empty<object>())!;
		Assert.Equal(3, afterGrant.TotalRequestsGranted);
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

		await ((Task)waitAsync!.Invoke(limiter, new object[] { CancellationToken.None })!);
		await ((Task)waitAsync.Invoke(limiter, new object[] { CancellationToken.None })!);

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
			await ((Task)waitAsync!.Invoke(limiter, new object[] { CancellationToken.None })!);
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
				await ((Task)waitAsync!.Invoke(testLimiter, new object[] { CancellationToken.None })!);
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
				await ((Task)waitAsync!.Invoke(testLimiter, new object[] { CancellationToken.None })!);
			}

			OcrService.OcrRequestWindowState saturated = OcrService.GetSharedRequestWindowState();
			Assert.Equal(3, saturated.RequestsInWindow);

			await Task.Delay(TimeSpan.FromMilliseconds(120));

			OcrService.OcrRequestWindowState afterWait = OcrService.GetSharedRequestWindowState();
			Assert.Equal(0, afterWait.RequestsInWindow);

			for (int i = 0; i < 3; i++)
			{
				await ((Task)waitAsync!.Invoke(testLimiter, new object[] { CancellationToken.None })!);
			}

			// The window was asserted empty just above and the limit is 3, so those
			// three grants could not have blocked. Six cumulative grants is therefore
			// the whole proof, and unlike an elapsed-time budget it cannot go stale.
			OcrService.OcrRequestWindowState freshBatch = OcrService.GetSharedRequestWindowState();
			Assert.Equal(6, freshBatch.TotalRequestsGranted);
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
				await ((Task)waitAsync!.Invoke(testLimiter, new object[] { CancellationToken.None })!);
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
	[InlineData(-5, 1, 1)]   // floor at 1 second (Math.Max(1, …))
	[InlineData(0, 1, 1)]
	[InlineData(1, 1, 1)]
	[InlineData(30, 1, 30)]
	[InlineData(60, 1, 60)]
	[InlineData(120, 1, 60)] // ceiling at MaxTimeoutSeconds (60)
	[InlineData(3600, 1, 60)]
	// The attempt number stretches the deadline, and the same ceiling caps every rung of
	// the ladder rather than only the first (issue #315).
	[InlineData(5, 2, 10)]
	[InlineData(5, 4, 20)]
	[InlineData(30, 2, 60)]
	[InlineData(30, 4, 60)]
	[InlineData(60, 3, 60)]
	public void GetPerRequestTimeout_StretchesByAttemptAndClampsToValidRange(
		int configuredSeconds, int attempt, int expectedSeconds)
	{
		var service = new OcrService("dummy-key", "https://example.com/", configuredSeconds);

		MethodInfo? method = typeof(OcrService).GetMethod("GetPerRequestTimeout", BindingFlags.NonPublic | BindingFlags.Instance);
		Assert.NotNull(method);
		var timeout = (TimeSpan)method!.Invoke(service, [attempt])!;

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

	private static CancellationTokenSource CreatePerRequestDeadline(OcrService service, int attempt = 1)
	{
		MethodInfo? method = typeof(OcrService).GetMethod("CreatePerRequestDeadline", BindingFlags.NonPublic | BindingFlags.Instance);
		Assert.NotNull(method);
		return (CancellationTokenSource)method!.Invoke(service, [attempt])!;
	}

	[Fact]
	public void PerRequestDeadline_LinksWithTheOverallTokenWithoutCancellingItself()
	{
		// ReadInternal links the two, so an outside cancel has to reach the request even
		// though the deadline is now its own source.
		var service = new OcrService("dummy-key", "https://example.com/", 30);

		using var overall = new CancellationTokenSource();
		using var deadline = CreatePerRequestDeadline(service);
		using var linked = CancellationTokenSource.CreateLinkedTokenSource(overall.Token, deadline.Token);

		Assert.False(linked.IsCancellationRequested);

		overall.Cancel();
		Assert.True(linked.Token.IsCancellationRequested);
		Assert.False(deadline.IsCancellationRequested);
	}

	[Fact]
	public void PerRequestDeadline_SelfCancelsAfterThePerRequestTimeout()
	{
		// The timeout is what stops a wedged Azure call hanging the batch, and linking
		// alone does not provide it. A one-second service timeout with a ten-second wait
		// leaves ample slack on a loaded agent while still failing if the deadline is lost.
		var service = new OcrService("dummy-key", "https://example.com/", 1);

		using var deadline = CreatePerRequestDeadline(service);

		Assert.True(
			deadline.Token.WaitHandle.WaitOne(TimeSpan.FromSeconds(10)),
			"The per-request source never cancelled itself, so no per-request timeout is scheduled.");
	}

	// ---------------------------------------------------------------------
	// BufferImage — what actually gets uploaded (issue #239)
	//
	// A MemoryStream's backing array is its *capacity* buffer. Reading it whole used to
	// POST the unused tail as image payload, and for a stream built over a slice it sent
	// a region of somebody else's array entirely.
	// ---------------------------------------------------------------------

	private static byte[] BufferImage(Stream stream)
	{
		MethodInfo? method = typeof(OcrService).GetMethod("BufferImage", BindingFlags.NonPublic | BindingFlags.Static);
		Assert.NotNull(method);
		return (byte[])method!.Invoke(null, new object[] { stream })!;
	}

	[Fact]
	public void BufferImage_MemoryStreamWithSpareCapacity_UploadsOnlyTheImage()
	{
		byte[] image = Enumerable.Range(1, 100).Select(value => (byte)value).ToArray();

		// Capacity deliberately larger than the content, as a grown MemoryStream's is.
		using var stream = new MemoryStream(capacity: 512);
		stream.Write(image, 0, image.Length);
		stream.Position = 0;

		Assert.True(stream.Capacity > image.Length, "The test needs spare capacity to be meaningful.");

		byte[] uploaded = BufferImage(stream);

		Assert.Equal(image, uploaded);
	}

	[Fact]
	public void BufferImage_SlicedMemoryStream_UploadsTheSliceNotTheWholeArray()
	{
		byte[] backing = Enumerable.Range(0, 200).Select(value => (byte)value).ToArray();
		byte[] expected = backing.Skip(40).Take(60).ToArray();

		using var stream = new MemoryStream(backing, index: 40, count: 60, writable: false, publiclyVisible: true);

		byte[] uploaded = BufferImage(stream);

		Assert.Equal(expected, uploaded);
	}

	// TryGetBuffer refuses a stream that is not publicly visible, so this one has to take
	// the copy path — and must still come back as exactly the slice.
	[Fact]
	public void BufferImage_SlicedMemoryStreamWithHiddenBuffer_UploadsTheSlice()
	{
		byte[] backing = Enumerable.Range(0, 200).Select(value => (byte)value).ToArray();
		byte[] expected = backing.Skip(40).Take(60).ToArray();

		using var stream = new MemoryStream(backing, index: 40, count: 60, writable: false);

		byte[] uploaded = BufferImage(stream);

		Assert.Equal(expected, uploaded);
	}

	// The buffered copy is what every retry re-reads, so it must not alias the caller's
	// array — a caller reusing its buffer would otherwise change the retried payload.
	[Fact]
	public void BufferImage_DoesNotAliasTheCallersArray()
	{
		byte[] backing = { 1, 2, 3, 4 };
		using var stream = new MemoryStream(backing, index: 0, count: 4, writable: true, publiclyVisible: true);

		byte[] uploaded = BufferImage(stream);
		backing[0] = 99;

		Assert.Equal(new byte[] { 1, 2, 3, 4 }, uploaded);
	}

	// A mid-stream position must not truncate the upload: Read seeks to the start before
	// each attempt, so the buffer has to hold the whole image regardless.
	[Fact]
	public void BufferImage_IgnoresTheStreamPosition()
	{
		byte[] image = { 10, 20, 30, 40, 50 };
		using var stream = new MemoryStream(capacity: 64);
		stream.Write(image, 0, image.Length);
		stream.Position = 3;

		byte[] uploaded = BufferImage(stream);

		Assert.Equal(image, uploaded);
	}

	[Fact]
	public void BufferImage_NonMemoryStream_UploadsEveryByte()
	{
		byte[] image = Enumerable.Range(0, 300).Select(value => (byte)value).ToArray();
		string path = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".bin");
		File.WriteAllBytes(path, image);

		try
		{
			using var stream = File.OpenRead(path);

			byte[] uploaded = BufferImage(stream);

			Assert.Equal(image, uploaded);
		}
		finally
		{
			if (File.Exists(path))
				File.Delete(path);
		}
	}

	[Fact]
	public void BufferImage_EmptyStream_ReturnsEmpty()
	{
		using var stream = new MemoryStream();

		byte[] uploaded = BufferImage(stream);

		Assert.Empty(uploaded);
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
