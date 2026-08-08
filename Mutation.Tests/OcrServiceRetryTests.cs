using System.Net;
using System.Net.Http;
using System.Reflection;
using System.Text;
using Azure;
using Azure.AI.Vision.ImageAnalysis;
using Azure.Core.Pipeline;
using CognitiveSupport;

namespace Mutation.Tests;

/// <summary>
/// Issue #311: nothing called <c>OcrService.Read</c>, so the retry ladder around it had no
/// coverage at all. The client is injected here — a real one, wired to a fake transport, so
/// the responses go through the SDK's own deserialization rather than a stubbed result.
/// Azure's own retry policy is turned off, so a request count is the number of times this
/// service tried, not the number of times two nested loops did.
/// </summary>
[Collection(OcrServiceStaticsCollection.Name)]
public class OcrServiceRetryTests : IDisposable
{
	private const int TimeoutSeconds = 30;

	private const string ReadBody = """
		{"modelVersion":"2023-10-01","metadata":{"width":100,"height":100},
		 "readResult":{"blocks":[{"lines":[
		   {"text":"the recovered text",
		    "boundingPolygon":[{"x":0,"y":0},{"x":50,"y":0},{"x":50,"y":20},{"x":0,"y":20}],
		    "words":[]}]}]}}
		""";

	private readonly List<HttpClient> _clients = [];

	// The 20-per-minute limiter is static and outlives a test. Four attempts apiece would
	// drain it partway through this class and leave the rest of it waiting out a real
	// minute, so it is cleared either side of every test. Issue #250 created the collection
	// that makes reaching in here safe.
	public OcrServiceRetryTests() => ResetSharedRateLimiter();

	public void Dispose()
	{
		foreach (var client in _clients)
			client.Dispose();
		ResetSharedRateLimiter();
	}

	private static void ResetSharedRateLimiter()
	{
		Type limiterType = typeof(OcrService).GetNestedType("RequestRateLimiter", BindingFlags.NonPublic)!;
		object limiter = typeof(OcrService)
			.GetField("SharedRateLimiter", BindingFlags.NonPublic | BindingFlags.Static)!
			.GetValue(null)!;
		limiterType.GetMethod("Reset", BindingFlags.NonPublic | BindingFlags.Instance)!
			.Invoke(limiter, Array.Empty<object>());
	}

	private (OcrService Service, ScriptedTransport Transport, RecordingClock Clock, List<int> Announced)
		CreateService(params Func<int, HttpResponseMessage>[] replies) =>
		CreateService(TimeoutSeconds, replies);

	private (OcrService Service, ScriptedTransport Transport, RecordingClock Clock, List<int> Announced)
		CreateService(int timeoutSeconds, params Func<int, HttpResponseMessage>[] replies)
	{
		var transport = new ScriptedTransport(replies);
		var httpClient = new HttpClient(transport);
		_clients.Add(httpClient);

		var options = new ImageAnalysisClientOptions
		{
			Transport = new HttpClientTransport(httpClient)
		};
		// Azure.Core retries 429 and 5xx on its own account. Left on, a single scripted
		// failure would be swallowed underneath and never reach the ladder under test.
		options.Retry.MaxRetries = 0;

		var client = new ImageAnalysisClient(
			new Uri("https://example.cognitiveservices.azure.com/"),
			new AzureKeyCredential("test-key"),
			options);

		var clock = new RecordingClock();
		var announced = new List<int>();
		var service = new OcrService("test-key", "https://example.cognitiveservices.azure.com/", timeoutSeconds, client, clock, announced.Add);
		return (service, transport, clock, announced);
	}

	private static Stream Image() => new MemoryStream(new byte[] { 1, 2, 3, 4 });

	private static Func<int, HttpResponseMessage> Ok(string body) =>
		_ => new HttpResponseMessage(HttpStatusCode.OK)
		{
			Content = new StringContent(body, Encoding.UTF8, "application/json")
		};

	private static Func<int, HttpResponseMessage> Status(HttpStatusCode status) =>
		_ => new HttpResponseMessage(status)
		{
			Content = new StringContent("""{"error":{"code":"Nope","message":"nope"}}""", Encoding.UTF8, "application/json")
		};

	[Fact]
	public async Task AServerSideBlipIsRetriedUntilItSucceeds()
	{
		var (service, transport, _, _) = CreateService(Status(HttpStatusCode.ServiceUnavailable), Ok(ReadBody));

		string text = await service.ExtractText(OcrReadingOrder.TopToBottomColumnAware, Image(), CancellationToken.None);

		Assert.Contains("the recovered text", text);
		Assert.Equal(2, transport.Calls);
	}

	[Fact]
	public async Task ARateLimitIsRetriedUntilItSucceeds()
	{
		// Azure Computer Vision allows 20 requests a minute on the free tier, so a 429 in
		// the middle of a batch is the everyday case, not the exotic one.
		var (service, transport, _, _) = CreateService(Status(HttpStatusCode.TooManyRequests), Ok(ReadBody));

		string text = await service.ExtractText(OcrReadingOrder.TopToBottomColumnAware, Image(), CancellationToken.None);

		Assert.Contains("the recovered text", text);
		Assert.Equal(2, transport.Calls);
	}

	[Fact]
	public async Task ATransientFailureGivesUpAfterThreeRetries()
	{
		var (service, transport, _, _) = CreateService(Status(HttpStatusCode.ServiceUnavailable));

		await Assert.ThrowsAsync<RequestFailedException>(() =>
			service.ExtractText(OcrReadingOrder.TopToBottomColumnAware, Image(), CancellationToken.None));

		Assert.Equal(4, transport.Calls);
	}

	[Theory]
	[InlineData(HttpStatusCode.Unauthorized)]
	[InlineData(HttpStatusCode.BadRequest)]
	[InlineData(HttpStatusCode.NotFound)]
	public async Task APermanentFailureIsNotRetried(HttpStatusCode status)
	{
		// A rejected key or a malformed request does not improve on the second ask, and an
		// OCR batch reports it far sooner this way.
		var (service, transport, _, _) = CreateService(Status(status));

		await Assert.ThrowsAsync<RequestFailedException>(() =>
			service.ExtractText(OcrReadingOrder.TopToBottomColumnAware, Image(), CancellationToken.None));

		Assert.Equal(1, transport.Calls);
	}

	[Fact]
	public async Task EveryRetrySendsTheWholeImageAgain()
	{
		// Read buffers the image precisely so each attempt can re-send it; a stream read to
		// the end on attempt one would otherwise upload nothing on attempt two (issue #239).
		var (service, transport, _, _) = CreateService(Status(HttpStatusCode.ServiceUnavailable), Ok(ReadBody));

		await service.ExtractText(OcrReadingOrder.TopToBottomColumnAware, Image(), CancellationToken.None);

		Assert.Equal(2, transport.Bodies.Count);
		Assert.Equal(transport.Bodies[0], transport.Bodies[1]);
		Assert.Equal(new byte[] { 1, 2, 3, 4 }, transport.Bodies[1]);
	}

	[Fact]
	public async Task EachAttemptGivesItselfLongerThanTheLastUpToTheCeiling()
	{
		// Issue #315 settled what issue #311 had assumed: this service stretches its
		// deadline by the attempt number like the other four, so a slow cold start gets
		// more room on the retry than it had on the first try. The 60-second ceiling caps
		// each attempt rather than only the first, so a wedged read cannot hold an OCR
		// batch up for longer and longer.
		var (service, _, clock, _) = CreateService(Status(HttpStatusCode.ServiceUnavailable));

		await Assert.ThrowsAsync<RequestFailedException>(() =>
			service.ExtractText(OcrReadingOrder.TopToBottomColumnAware, Image(), CancellationToken.None));

		Assert.Equal([30d, 60d, 60d, 60d], clock.DeadlineSeconds);
		Assert.Equal([500d, 1000d, 1500d], clock.BackoffMilliseconds);
	}

	[Fact]
	public async Task AShortTimeoutStretchesAllTheWayUpTheLadder()
	{
		// With the ceiling well out of reach, every rung is longer than the one before —
		// the point of stretching in the first place. Read alongside the test above, which
		// is the same ladder run into the cap.
		var (service, _, clock, _) = CreateService(
			timeoutSeconds: 5, Status(HttpStatusCode.ServiceUnavailable));

		await Assert.ThrowsAsync<RequestFailedException>(() =>
			service.ExtractText(OcrReadingOrder.TopToBottomColumnAware, Image(), CancellationToken.None));

		Assert.Equal([5d, 10d, 15d, 20d], clock.DeadlineSeconds);
	}

	[Fact]
	public async Task TheListenerIsToldWhichRetryThisIs()
	{
		// One beep per attempt, and none for the first: a first try has nothing to report
		// yet, and a third retry has to be distinguishable from a first (issue #216).
		var (service, _, _, announced) = CreateService(Status(HttpStatusCode.ServiceUnavailable));

		await Assert.ThrowsAsync<RequestFailedException>(() =>
			service.ExtractText(OcrReadingOrder.TopToBottomColumnAware, Image(), CancellationToken.None));

		Assert.Equal([1, 2, 3, 4], announced);
		Assert.Equal([2, 3, 4], announced.Where(RetryBeepPolicy.ShouldBeep));
	}

	/// <summary>
	/// Answers each request from a script and keeps the body it was sent, so a test can say
	/// what Azure does on attempt one, two and three — and check that the retry uploaded the
	/// same image the first attempt did. The last entry stands in for every call past the
	/// end of the script.
	/// </summary>
	private sealed class ScriptedTransport(Func<int, HttpResponseMessage>[] replies) : HttpMessageHandler
	{
		private readonly List<byte[]> _bodies = [];

		internal int Calls
		{
			get { lock (_bodies) return _bodies.Count; }
		}

		internal IReadOnlyList<byte[]> Bodies
		{
			get { lock (_bodies) return _bodies.ToArray(); }
		}

		protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
		{
			byte[] body = request.Content is null
				? []
				: await request.Content.ReadAsByteArrayAsync(cancellationToken).ConfigureAwait(false);

			int call;
			lock (_bodies)
			{
				_bodies.Add(body);
				call = _bodies.Count;
			}

			return replies[Math.Min(call, replies.Length) - 1](call);
		}
	}
}
