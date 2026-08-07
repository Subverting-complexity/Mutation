using System.ClientModel.Primitives;
using System.Net;
using System.Net.Http;
using System.Text;
using CognitiveSupport;

namespace Mutation.Tests;

/// <summary>
/// Covers issue #256: <see cref="ILlmService"/> took no <see cref="CancellationToken"/>, so
/// nothing outside a call could cut it short. With the shipped defaults (60 s timeout,
/// 3 retries) an outage climbs an escalating ladder of 60 + 120 + 180 + 240 s, and a Fast
/// mode request that falls back to standard speed climbs the whole thing twice — twenty
/// minutes with no way out, and no cancel offered anywhere in the UI.
/// </summary>
public class LlmCancellationTests : IDisposable
{
	private const string AnthropicModel = "claude-fable-5";
	private const string OpenAiModel = "gpt-5.6";

	private const string AnthropicSuccess =
		"""{"content":[{"type":"text","text":"done"}]}""";
	private const string OpenAiSuccess =
		"""{"id":"c1","object":"chat.completion","created":1,"model":"gpt-5.6","choices":[{"index":0,"finish_reason":"stop","message":{"role":"assistant","content":"done"}}]}""";

	private readonly List<HttpClient> _clients = new();

	public void Dispose()
	{
		foreach (var client in _clients)
			client.Dispose();
	}

	private HttpClient Track(HttpMessageHandler handler)
	{
		var client = new HttpClient(handler);
		_clients.Add(client);
		return client;
	}

	private AnthropicLlmService CreateAnthropic(HttpMessageHandler handler, int retryCount) =>
		new(
			"test-key",
			new[] { new LlmModelConfig(AnthropicModel, LlmProvider.Anthropic, customTemperature: null) },
			Track(handler),
			timeoutSeconds: 30,
			retryCount: retryCount);

	private LlmService CreateOpenAi(HttpMessageHandler handler, int retryCount) =>
		new(
			"test-key",
			new[] { new LlmModelConfig(OpenAiModel, LlmProvider.OpenAI, customTemperature: null) },
			timeoutSeconds: 30,
			retryCount: retryCount,
			transport: new HttpClientPipelineTransport(Track(handler)));

	private static IList<LlmChatMessage> Messages() =>
		new List<LlmChatMessage> { new(LlmChatRole.User, "hello") };

	// ----- The ladder, uncancelled. The baseline the tests below are measured against.

	[Fact]
	public async Task WithoutCancellation_ATransientFailureClimbsTheWholeLadder()
	{
		var handler = new ScriptedHandler(HttpStatusCode.ServiceUnavailable);

		await Assert.ThrowsAsync<HttpRequestException>(() =>
			CreateAnthropic(handler, retryCount: 2).CreateChatCompletion(Messages(), AnthropicModel));

		// One attempt plus both retries. This is what a cancel has to be able to escape.
		Assert.Equal(3, handler.RequestCount);
	}

	// ----- Cancelling mid-ladder.

	[Fact]
	public async Task Anthropic_CancelDuringAnAttempt_StopsInsteadOfRetrying()
	{
		using var cts = new CancellationTokenSource();
		// Cancels as it answers the second attempt and then honours the token, which is
		// what a real HttpClient does when the token trips during a request. The resulting
		// TaskCanceledException must not be mistaken for the per-attempt timeout it
		// otherwise looks exactly like.
		var handler = new ScriptedHandler(HttpStatusCode.ServiceUnavailable)
		{
			CancelOnRequest = 2,
			CancelWith = cts,
			HonourTokenAfterCancelling = true,
		};

		await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
			CreateAnthropic(handler, retryCount: 2).CreateChatCompletion(
				Messages(), AnthropicModel, options: null, cts.Token));

		Assert.Equal(2, handler.RequestCount);
	}

	[Fact]
	public async Task Anthropic_CancelBetweenAttempts_IsNotSleptThrough()
	{
		using var cts = new CancellationTokenSource();
		// Cancels but answers normally, so the cancel lands after the retry decision and
		// during the wait before the next attempt. The wait has to notice it.
		var handler = new ScriptedHandler(HttpStatusCode.ServiceUnavailable)
		{
			CancelOnRequest = 2,
			CancelWith = cts,
		};

		await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
			CreateAnthropic(handler, retryCount: 2).CreateChatCompletion(
				Messages(), AnthropicModel, options: null, cts.Token));

		Assert.Equal(2, handler.RequestCount);
	}

	[Fact]
	public async Task Anthropic_CancelDuringFastMode_DoesNotRunTheStandardSpeedLadderToo()
	{
		using var cts = new CancellationTokenSource();
		// 529 is Fast mode's capacity rejection: exhausting the ladder on it normally
		// falls back to standard speed and climbs the whole ladder a second time. That
		// doubling is where the twenty minutes in issue #256 comes from, so a cancel has
		// to stop the fallback from starting at all.
		var handler = new ScriptedHandler((HttpStatusCode)529)
		{
			CancelOnRequest = 1,
			CancelWith = cts,
			HonourTokenAfterCancelling = true,
		};

		await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
			CreateAnthropic(handler, retryCount: 2).CreateChatCompletion(
				Messages(), AnthropicModel, new LlmRequestOptions { FastMode = true }, cts.Token));

		Assert.Equal(1, handler.RequestCount);
	}

	[Fact]
	public async Task OpenAi_CancelDuringAnAttempt_StopsInsteadOfRetrying()
	{
		using var cts = new CancellationTokenSource();
		var handler = new ScriptedHandler(HttpStatusCode.ServiceUnavailable)
		{
			CancelOnRequest = 1,
			CancelWith = cts,
			HonourTokenAfterCancelling = true,
		};

		await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
			CreateOpenAi(handler, retryCount: 3).CreateChatCompletion(
				Messages(), OpenAiModel, requestOptions: null, cts.Token));

		// Configured for four attempts; it stopped after the one that was cancelled.
		Assert.Equal(1, handler.RequestCount);
	}

	// ----- A token that is already spent.

	[Fact]
	public async Task Anthropic_AlreadyCancelledToken_SendsNothing()
	{
		using var cts = new CancellationTokenSource();
		cts.Cancel();
		var handler = new ScriptedHandler(HttpStatusCode.OK, AnthropicSuccess);

		await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
			CreateAnthropic(handler, retryCount: 2).CreateChatCompletion(
				Messages(), AnthropicModel, options: null, cts.Token));

		Assert.Equal(0, handler.RequestCount);
	}

	[Fact]
	public async Task OpenAi_AlreadyCancelledToken_SendsNothing()
	{
		using var cts = new CancellationTokenSource();
		cts.Cancel();
		var handler = new ScriptedHandler(HttpStatusCode.OK, OpenAiSuccess);

		await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
			CreateOpenAi(handler, retryCount: 3).CreateChatCompletion(
				Messages(), OpenAiModel, requestOptions: null, cts.Token));

		Assert.Equal(0, handler.RequestCount);
	}

	// ----- No token supplied: every existing call site keeps working unchanged.

	[Fact]
	public async Task Anthropic_NoTokenSupplied_StillCompletes()
	{
		var handler = new ScriptedHandler(HttpStatusCode.OK, AnthropicSuccess);

		string result = await CreateAnthropic(handler, retryCount: 0)
			.CreateChatCompletion(Messages(), AnthropicModel);

		Assert.Equal("done", result);
	}

	[Fact]
	public async Task OpenAi_NoTokenSupplied_StillCompletes()
	{
		var handler = new ScriptedHandler(HttpStatusCode.OK, OpenAiSuccess);

		string result = await CreateOpenAi(handler, retryCount: 0)
			.CreateChatCompletion(Messages(), OpenAiModel);

		Assert.Equal("done", result);
	}

	// Routing the token through CompositeLlmService is covered by
	// CompositeLlmServiceTests, alongside the rest of what that layer forwards.

	/// <summary>
	/// Replies with one fixed status, counts what it was asked for, and can trip a
	/// cancellation as it answers a chosen request — the only way to land a cancel at a
	/// known point in the ladder without a race.
	/// </summary>
	private sealed class ScriptedHandler : HttpMessageHandler
	{
		private readonly HttpStatusCode _status;
		private readonly string _body;

		internal ScriptedHandler(HttpStatusCode status, string body = "{}")
		{
			_status = status;
			_body = body;
		}

		internal int RequestCount { get; private set; }

		/// <summary>1-based request number to cancel on; 0 never cancels.</summary>
		internal int CancelOnRequest { get; init; }

		internal CancellationTokenSource? CancelWith { get; init; }

		/// <summary>
		/// Whether to fail the request the cancel landed on, the way a real HttpClient
		/// does, rather than answering it and leaving the cancel to be noticed between
		/// attempts. Both are worth exercising: they take different paths out.
		/// </summary>
		internal bool HonourTokenAfterCancelling { get; init; }

		protected override Task<HttpResponseMessage> SendAsync(
			HttpRequestMessage request,
			CancellationToken cancellationToken)
		{
			RequestCount++;

			if (CancelOnRequest > 0 && RequestCount == CancelOnRequest)
			{
				CancelWith?.Cancel();
				if (HonourTokenAfterCancelling)
					cancellationToken.ThrowIfCancellationRequested();
			}

			return Task.FromResult(new HttpResponseMessage(_status)
			{
				Content = new StringContent(_body, Encoding.UTF8, "application/json"),
			});
		}
	}
}
