using System.ClientModel;
using System.ClientModel.Primitives;
using System.Net;
using System.Net.Http;
using CognitiveSupport;
using OpenAI.Audio;

namespace Mutation.Tests;

/// <summary>
/// Issue #311: nothing drove a retry through this service. A call count here means "this
/// many times our pipeline tried", not "this many times two nested loops did" — and since
/// issue #318 that holds in production too, because
/// <see cref="OpenAiClientOptionsFactory.Create"/> ships with the SDK's own retry policy
/// off rather than leaving it on for everyone but the tests.
/// </summary>
public class OpenAiSpeechToTextServiceRetryTests : IDisposable
{
	private const int BaseTimeoutSeconds = 10;
	private const string TranscriptBody = """{"text":"the recovered transcript"}""";

	private readonly List<HttpClient> _clients = [];
	private readonly List<string> _files = [];

	public void Dispose()
	{
		foreach (var client in _clients)
			client.Dispose();
		foreach (var file in _files)
			File.Delete(file);
	}

	private string WriteAudioFile()
	{
		string path = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".wav");
		File.WriteAllBytes(path, new byte[] { 1, 2, 3, 4 });
		_files.Add(path);
		return path;
	}

	private (OpenAiSpeechToTextService Service, ScriptedHandler Handler, RecordingClock Clock, List<int> Announced)
		CreateService(params Func<int, HttpResponseMessage>[] replies)
	{
		var clock = new RecordingClock();
		var handler = new ScriptedHandler(replies);
		var httpClient = new HttpClient(handler);
		_clients.Add(httpClient);

		var audioClient = new AudioClient(
			"whisper-1",
			new ApiKeyCredential("test-key"),
			OpenAiClientOptionsFactory.Create(
				transport: new HttpClientPipelineTransport(httpClient)));

		var announced = new List<int>();
		var service = new OpenAiSpeechToTextService("Whisper", audioClient, BaseTimeoutSeconds, clock, announced.Add);
		return (service, handler, clock, announced);
	}

	private static Func<int, HttpResponseMessage> Ok(string body) =>
		_ => new HttpResponseMessage(HttpStatusCode.OK)
		{
			Content = new StringContent(body, System.Text.Encoding.UTF8, "application/json")
		};

	private static Func<int, HttpResponseMessage> Status(HttpStatusCode status, string? retryAfter = null) =>
		_ =>
		{
			var response = new HttpResponseMessage(status)
			{
				Content = new StringContent("""{"error":{"message":"nope"}}""", System.Text.Encoding.UTF8, "application/json")
			};
			if (retryAfter is not null)
				response.Headers.TryAddWithoutValidation("Retry-After", retryAfter);
			return response;
		};

	private static Func<int, HttpResponseMessage> Dropped() =>
		_ => throw new HttpRequestException("connection reset");

	/// <summary>
	/// Drops the connection, having first tripped the caller's token. The cancel is
	/// therefore in place well before Polly reaches its backoff, which is what makes the
	/// test that uses it decide the same way every run.
	/// </summary>
	private static Func<int, HttpResponseMessage> DroppedAfterCancelling(CancellationTokenSource cts) =>
		_ =>
		{
			cts.Cancel();
			throw new HttpRequestException("connection reset");
		};

	[Fact]
	public async Task ADroppedConnectionIsRetriedUntilItSucceeds()
	{
		// The SDK hands a connection failure over as a ClientResultException carrying
		// status 0, never as the HttpRequestException the handler threw. Retrying it is
		// the whole point of the ladder: the recording is already made, and losing a
		// dictated transcript to one bad packet is the worst outcome available.
		var (service, handler, _, _) = CreateService(Dropped(), Dropped(), Ok(TranscriptBody));

		string text = await service.ConvertAudioToText("", WriteAudioFile(), CancellationToken.None);

		Assert.Equal("the recovered transcript", text);
		Assert.Equal(3, handler.Calls);
	}

	[Theory]
	[InlineData(HttpStatusCode.TooManyRequests)]
	[InlineData(HttpStatusCode.ServiceUnavailable)]
	[InlineData(HttpStatusCode.InternalServerError)]
	public async Task AServerSideBlipIsRetriedUntilItSucceeds(HttpStatusCode status)
	{
		var (service, handler, _, _) = CreateService(Status(status), Ok(TranscriptBody));

		string text = await service.ConvertAudioToText("", WriteAudioFile(), CancellationToken.None);

		Assert.Equal("the recovered transcript", text);
		Assert.Equal(2, handler.Calls);
	}

	[Fact]
	public async Task ATransientFailureGivesUpAfterThreeRetries()
	{
		var (service, handler, _, _) = CreateService(Dropped());

		await Assert.ThrowsAsync<ClientResultException>(() =>
			service.ConvertAudioToText("", WriteAudioFile(), CancellationToken.None));

		// The first try plus three retries, and then it reports the failure rather than
		// retrying for ever.
		Assert.Equal(4, handler.Calls);
	}

	[Theory]
	[InlineData(HttpStatusCode.Unauthorized)]
	[InlineData(HttpStatusCode.BadRequest)]
	[InlineData(HttpStatusCode.NotFound)]
	public async Task APermanentFailureIsNotRetried(HttpStatusCode status)
	{
		// A rejected API key does not become valid on the second ask. Reporting it at once
		// beats making the user sit out four attempts on a lengthening timeout first.
		var (service, handler, _, _) = CreateService(Status(status));

		await Assert.ThrowsAsync<ClientResultException>(() =>
			service.ConvertAudioToText("", WriteAudioFile(), CancellationToken.None));

		Assert.Equal(1, handler.Calls);
	}

	[Fact]
	public async Task APersistentRateLimitCostsFourRequestsAndNotSixteen()
	{
		// The number issue #318 is about: our four attempts used to wrap the SDK's own three
		// retries. Nothing here caps the SDK any more, because the factory ships with it off.
		var (service, handler, _, _) = CreateService(Status(HttpStatusCode.TooManyRequests));

		await Assert.ThrowsAsync<ClientResultException>(() =>
			service.ConvertAudioToText("", WriteAudioFile(), CancellationToken.None));

		Assert.Equal(4, handler.Calls);
	}

	[Fact]
	public async Task ARateLimitThatAsksForAParticularWaitGetsIt()
	{
		// Honouring Retry-After was the one thing the SDK's loop did better than ours, so it
		// moved into TransientRetry rather than being lost with it (issue #318).
		var (service, _, clock, _) = CreateService(
			Status(HttpStatusCode.TooManyRequests, retryAfter: "2"),
			Ok(TranscriptBody));

		string text = await service.ConvertAudioToText("", WriteAudioFile(), CancellationToken.None);

		Assert.Equal("the recovered transcript", text);
		Assert.Equal([2000d], clock.BackoffMilliseconds);
	}

	[Fact]
	public async Task EachAttemptGivesItselfLongerThanTheLast()
	{
		var (service, _, clock, _) = CreateService(Dropped());

		await Assert.ThrowsAsync<ClientResultException>(() =>
			service.ConvertAudioToText("", WriteAudioFile(), CancellationToken.None));

		Assert.Equal([10d, 20d, 30d, 40d], clock.DeadlineSeconds);
		Assert.Equal([500d, 1000d, 1500d], clock.BackoffMilliseconds);
	}

	[Fact]
	public async Task ACallerSuppliedTimeoutSetsTheLadderInsteadOfTheConfiguredOne()
	{
		// The file-transcription path passes a much longer timeout than the dictation one.
		var (service, _, clock, _) = CreateService(Dropped());

		await Assert.ThrowsAsync<ClientResultException>(() =>
			service.ConvertAudioToText("", WriteAudioFile(), CancellationToken.None, timeoutSeconds: 300));

		Assert.Equal([300d, 600d, 900d, 1200d], clock.DeadlineSeconds);
	}

	[Fact]
	public async Task CancellingPartWayUpTheLadderStopsIt()
	{
		// This PR widened what gets retried here, so it also has to say that a user who
		// gives up is still obeyed. Cancelling a dictation has its own history (#179, #256,
		// #305), and the one test that looked like it covered this drove a hand-written
		// stand-in rather than the real service — which is what #311 was filed about.
		using var cts = new CancellationTokenSource();
		var (service, handler, _, _) = CreateService(DroppedAfterCancelling(cts));

		await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
			service.ConvertAudioToText("", WriteAudioFile(), cts.Token));

		// One request, not four. A dropped connection is transient here, so it had earned
		// three more attempts; the cancelled token is the only reason none of them ran.
		Assert.Equal(1, handler.Calls);
	}

	[Fact]
	public async Task TheListenerIsToldWhichRetryThisIs()
	{
		var (service, _, _, announced) = CreateService(Dropped());

		await Assert.ThrowsAsync<ClientResultException>(() =>
			service.ConvertAudioToText("", WriteAudioFile(), CancellationToken.None));

		Assert.Equal([1, 2, 3, 4], announced);
		Assert.Equal([2, 3, 4], announced.Where(RetryBeepPolicy.ShouldBeep));
	}

	/// <summary>
	/// Answers each request from a script, so a test can say what the network does on
	/// attempt one, two and three — including failing to connect at all, which the
	/// shared <see cref="FakeHttpMessageHandler"/> has no way to express. The last entry
	/// stands in for every call past the end of the script.
	/// </summary>
	private sealed class ScriptedHandler(Func<int, HttpResponseMessage>[] replies) : HttpMessageHandler
	{
		private int _calls;

		internal int Calls => Volatile.Read(ref _calls);

		protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
		{
			int call = Interlocked.Increment(ref _calls);
			return Task.FromResult(replies[Math.Min(call, replies.Length) - 1](call));
		}
	}
}
