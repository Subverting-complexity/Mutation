using System.ClientModel;
using System.ClientModel.Primitives;
using System.Net;
using System.Net.Http;
using CognitiveSupport;

namespace Mutation.Tests;

/// <summary>
/// Issue #311: every fast-mode test builds this service with a retry count of zero, so the
/// pipeline is empty and no retry has ever run. These use a real one.
/// <para>
/// A request count here is the number of times <em>this</em> service tried, not the product
/// of two nested loops — and since issue #318 that is true of production too, because the
/// SDK's own retry policy is off by default rather than turned off for the tests.
/// </para>
/// </summary>
public class LlmServiceRetryTests : IDisposable
{
	private const string Model = "gpt-5.6";
	private const int TimeoutSeconds = 60;

	private const string SuccessBody =
		"""{"id":"c1","object":"chat.completion","created":1,"model":"gpt-5.6","choices":[{"index":0,"finish_reason":"stop","message":{"role":"assistant","content":"done"}}]}""";

	private static readonly LlmModelConfig Config = new(Model, LlmProvider.OpenAI, customTemperature: null);

	private readonly List<HttpClient> _clients = [];

	public void Dispose()
	{
		foreach (var client in _clients)
			client.Dispose();
	}

	private (LlmService Service, RecordingClock Clock) CreateService(FakeHttpMessageHandler handler, int retryCount = 3)
	{
		var httpClient = new HttpClient(handler);
		_clients.Add(httpClient);

		var clock = new RecordingClock();
		var service = new LlmService(
			"test-key",
			[Config],
			timeoutSeconds: TimeoutSeconds,
			retryCount: retryCount,
			transport: new HttpClientPipelineTransport(httpClient),
			timeProvider: clock);
		return (service, clock);
	}

	private static IList<LlmChatMessage> Messages() => [new(LlmChatRole.User, "hello")];

	private static string ErrorBody(string message) =>
		$$$"""{"error":{"message":"{{{message}}}","type":"server_error"}}""";

	[Theory]
	[InlineData(HttpStatusCode.TooManyRequests)]
	[InlineData(HttpStatusCode.ServiceUnavailable)]
	[InlineData(HttpStatusCode.InternalServerError)]
	[InlineData(HttpStatusCode.RequestTimeout)]
	public async Task ATransientFailureIsRetriedUntilItSucceeds(HttpStatusCode status)
	{
		// The cold-start case this ladder exists for: the first call after a reboot meets a
		// slow or briefly unavailable service, and a second attempt gets straight through.
		var handler = new FakeHttpMessageHandler()
			.Respond(status, ErrorBody("try again"))
			.Respond(HttpStatusCode.OK, SuccessBody);
		var (service, _) = CreateService(handler);

		string result = await service.CreateChatCompletion(Messages(), Model);

		Assert.Equal("done", result);
		Assert.Equal(2, handler.Requests.Count);
	}

	[Fact]
	public async Task ATransientFailureGivesUpAfterThreeRetries()
	{
		var handler = new FakeHttpMessageHandler()
			.Respond(HttpStatusCode.ServiceUnavailable, ErrorBody("still down"));
		var (service, _) = CreateService(handler);

		await Assert.ThrowsAsync<ClientResultException>(() =>
			service.CreateChatCompletion(Messages(), Model));

		// The first try plus three retries, and then it reports the failure rather than
		// retrying for ever.
		Assert.Equal(4, handler.Requests.Count);
	}

	[Theory]
	[InlineData(HttpStatusCode.Unauthorized)]
	[InlineData(HttpStatusCode.Forbidden)]
	[InlineData(HttpStatusCode.BadRequest)]
	[InlineData(HttpStatusCode.NotFound)]
	public async Task APermanentFailureIsNotRetried(HttpStatusCode status)
	{
		// With a 60-second base timeout that grows each attempt, retrying a bad API key
		// could take a minute or more to report something that was never going to work.
		var handler = new FakeHttpMessageHandler()
			.Respond(status, ErrorBody("Incorrect API key provided"));
		var (service, _) = CreateService(handler);

		await Assert.ThrowsAsync<ClientResultException>(() =>
			service.CreateChatCompletion(Messages(), Model));

		Assert.Single(handler.Requests);
	}

	[Fact]
	public async Task NoRetriesConfiguredMeansOneAttempt()
	{
		// The retry count is a user setting and zero is a legal value for it.
		var handler = new FakeHttpMessageHandler()
			.Respond(HttpStatusCode.ServiceUnavailable, ErrorBody("still down"));
		var (service, _) = CreateService(handler, retryCount: 0);

		await Assert.ThrowsAsync<ClientResultException>(() =>
			service.CreateChatCompletion(Messages(), Model));

		Assert.Single(handler.Requests);
	}

	[Fact]
	public async Task APersistentRateLimitCostsFourRequestsAndNotSixteen()
	{
		// The number issue #318 is about. Our four attempts used to wrap the SDK's own three
		// retries, so a rate-limited account was answered by asking sixteen more times. This
		// service is built exactly as App.xaml.cs builds it — no test-only cap — so the count
		// is production's count.
		var handler = new FakeHttpMessageHandler()
			.Respond(HttpStatusCode.TooManyRequests, ErrorBody("slow down"));
		var (service, _) = CreateService(handler);

		await Assert.ThrowsAsync<ClientResultException>(() =>
			service.CreateChatCompletion(Messages(), Model));

		Assert.Equal(4, handler.Requests.Count);
	}

	[Fact]
	public async Task ARateLimitThatAsksForAParticularWaitGetsIt()
	{
		// Honouring Retry-After was the one thing the SDK's retry loop did better than ours,
		// and turning that loop off would have thrown it away. 2 s here, not the 500 ms the
		// linear backoff would otherwise have picked.
		var handler = new FakeHttpMessageHandler()
			.Respond(HttpStatusCode.TooManyRequests, ErrorBody("slow down"), retryAfter: "2")
			.Respond(HttpStatusCode.OK, SuccessBody);
		var (service, clock) = CreateService(handler);

		string result = await service.CreateChatCompletion(Messages(), Model);

		Assert.Equal("done", result);
		Assert.Equal([2000d], clock.BackoffMilliseconds);
	}

	[Theory]
	[InlineData("0")]
	[InlineData("-1")]
	public async Task ARateLimitAskingForNoWaitAtAllDoesNotCollapseTheLadder(string retryAfter)
	{
		// The header comes from the server, so a bad one must not be able to delete our own
		// backoff and run all four attempts inside a few milliseconds — which is the
		// hammering the whole change exists to stop.
		var handler = new FakeHttpMessageHandler()
			.Respond(HttpStatusCode.TooManyRequests, ErrorBody("slow down"), retryAfter: retryAfter);
		var (service, clock) = CreateService(handler);

		await Assert.ThrowsAsync<ClientResultException>(() =>
			service.CreateChatCompletion(Messages(), Model));

		Assert.Equal(4, handler.Requests.Count);
		Assert.Equal([500d, 1000d, 1500d], clock.BackoffMilliseconds);
	}

	[Fact]
	public async Task AFailureThatAsksForNothingKeepsTheLinearBackoff()
	{
		var handler = new FakeHttpMessageHandler()
			.Respond(HttpStatusCode.ServiceUnavailable, ErrorBody("still down"))
			.Respond(HttpStatusCode.OK, SuccessBody);
		var (service, clock) = CreateService(handler);

		await service.CreateChatCompletion(Messages(), Model);

		Assert.Equal([500d], clock.BackoffMilliseconds);
	}

	[Fact]
	public async Task EachAttemptGivesItselfLongerThanTheLast()
	{
		var handler = new FakeHttpMessageHandler()
			.Respond(HttpStatusCode.ServiceUnavailable, ErrorBody("still down"));
		var (service, clock) = CreateService(handler);

		await Assert.ThrowsAsync<ClientResultException>(() =>
			service.CreateChatCompletion(Messages(), Model));

		Assert.Equal([60d, 120d, 180d, 240d], clock.DeadlineSeconds);
		Assert.Equal([500d, 1000d, 1500d], clock.BackoffMilliseconds);
	}

	[Fact]
	public async Task AFastModeFallbackStillGetsItsOwnFullRetryLadder()
	{
		// The tier is rejected, then the standard-speed send meets a transient failure. The
		// user's text is only saved if that second send retries in its own right.
		//
		// The deadlines are also what says each send counts its own attempts. A leaked
		// attempt number — one living on the shared pipeline rather than in Polly's
		// per-execution context — would open the fallback send on the abandoned send's
		// stretched deadline and read [60, 120, 180] instead. A fallback after a send that
		// did *not* retry cannot tell the difference, so this is the case that has to
		// carry it.
		var handler = new FakeHttpMessageHandler()
			.Respond(HttpStatusCode.BadRequest, """{"error":{"message":"Invalid value: 'fast'. Supported values are: 'auto', 'default', 'flex' and 'priority'.","type":"invalid_request_error","param":"service_tier"}}""")
			.Respond(HttpStatusCode.ServiceUnavailable, ErrorBody("try again"))
			.Respond(HttpStatusCode.OK, SuccessBody);

		FastModeFallback? reported = null;
		var (service, clock) = CreateService(handler);

		string result = await service.CreateChatCompletion(
			Messages(), Model, new LlmRequestOptions { FastMode = true, OnFastModeFallback = f => reported = f });

		Assert.Equal("done", result);
		Assert.Equal(3, handler.Requests.Count);
		Assert.NotNull(reported);
		// Send one: 60. Send two: 60, then 120 for its retry.
		Assert.Equal([60d, 60d, 120d], clock.DeadlineSeconds);
	}

	[Fact]
	public async Task CancellingPartWayUpTheLadderStopsIt()
	{
		// Handing the caller's token to the pipeline is what makes the ladder escapable:
		// Polly checks it at the head of every attempt and waits on it during the backoff,
		// so an outside cancel ends the loop rather than reading as one more blip (#256).
		//
		// The cancel has to land *inside* the ladder to prove any of that. Cancelling
		// before the call would leave nothing to escape from — the body would run once and
		// throw, which it does with no retry strategy at all.
		using var cts = new CancellationTokenSource();
		var handler = new CancelThenDropHandler(cts);
		var httpClient = new HttpClient(handler);
		_clients.Add(httpClient);

		var service = new LlmService(
			"test-key",
			[Config],
			timeoutSeconds: TimeoutSeconds,
			retryCount: 3,
			transport: new HttpClientPipelineTransport(httpClient),
			timeProvider: new RecordingClock());

		await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
			service.CreateChatCompletion(Messages(), Model, cancellationToken: cts.Token));

		// One request, not four. A dropped connection reaches the pipeline as a status-0
		// ClientResultException, which is transient, so it had earned three more attempts;
		// the cancelled token is the only reason none of them ran.
		Assert.Equal(1, handler.Calls);
	}

	/// <summary>
	/// Drops the connection, having first tripped the caller's token — so the cancel is in
	/// place well before Polly reaches its backoff, and the test decides the same way every
	/// run. Cancelling from a timer hook instead races Polly's own token check; cancelling
	/// before the call at all proves nothing, because a body that runs once and throws is
	/// what happens with no retry strategy whatsoever.
	/// </summary>
	private sealed class CancelThenDropHandler(CancellationTokenSource cts) : HttpMessageHandler
	{
		private int _calls;

		internal int Calls => Volatile.Read(ref _calls);

		protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
		{
			Interlocked.Increment(ref _calls);
			cts.Cancel();
			throw new HttpRequestException("connection reset");
		}
	}
}
