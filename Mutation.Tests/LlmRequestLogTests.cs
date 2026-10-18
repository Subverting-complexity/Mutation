using System.ClientModel.Primitives;
using System.Net;
using System.Net.Http;
using CognitiveSupport;

namespace Mutation.Tests;

/// <summary>
/// The lines both LLM services write about each request (issue #392): a start line, then
/// a completion, failure or cancellation line, each naming the entry point and the
/// requested model. Read through the services' log seam, so these tests see only their
/// own request's lines and never the shared log file.
/// </summary>
public class LlmRequestLogTests : IDisposable
{
	private const string OpenAiModel = "gpt-5.6";
	private const string AnthropicModel = "claude-opus-5";

	// Distinctive, so a test can prove neither ever reaches a log line.
	private const string PromptText = "PROMPT-TEXT-never-logged";
	private const string ApiKey = "sk-test-key-never-logged-123456";

	private const string OpenAiSuccessBody =
		"""{"id":"c1","object":"chat.completion","created":1,"model":"gpt-5.6-2026-09-01","service_tier":"priority","choices":[{"index":0,"finish_reason":"stop","message":{"role":"assistant","content":"done"}}],"usage":{"prompt_tokens":12,"completion_tokens":3,"total_tokens":15}}""";

	private const string AnthropicSuccessBody =
		"""{"model":"claude-opus-5-20261001","content":[{"type":"text","text":"done"}],"stop_reason":"end_turn","usage":{"input_tokens":21,"output_tokens":4,"service_tier":"standard"}}""";

	private readonly List<HttpClient> _clients = new();
	private readonly List<string> _lines = new();

	public void Dispose()
	{
		foreach (var client in _clients)
			client.Dispose();
	}

	private LlmService CreateOpenAi(HttpMessageHandler handler)
	{
		var client = new HttpClient(handler);
		_clients.Add(client);
		return new LlmService(
			ApiKey,
			new[] { new LlmModelConfig(OpenAiModel, LlmProvider.OpenAI, customTemperature: null) },
			timeoutSeconds: 5,
			retryCount: 0,
			transport: new HttpClientPipelineTransport(client),
			requestLog: _lines.Add);
	}

	private AnthropicLlmService CreateAnthropic(HttpMessageHandler handler)
	{
		var client = new HttpClient(handler);
		_clients.Add(client);
		return new AnthropicLlmService(
			ApiKey,
			new[] { new LlmModelConfig(AnthropicModel, LlmProvider.Anthropic, customTemperature: null) },
			client,
			timeoutSeconds: 5,
			retryCount: 0,
			requestLog: _lines.Add);
	}

	private static IList<LlmChatMessage> Messages() =>
		new List<LlmChatMessage>
		{
			new(LlmChatRole.System, PromptText),
			new(LlmChatRole.User, PromptText),
		};

	private static LlmRequestOptions Options(bool fastMode = false) =>
		new() { EntryPoint = LlmEntryPoint.PromptHotkey, FastMode = fastMode };

	private void AssertNothingPrivateLogged()
	{
		foreach (var line in _lines)
		{
			Assert.DoesNotContain(PromptText, line, StringComparison.Ordinal);
			Assert.DoesNotContain(ApiKey, line, StringComparison.Ordinal);
		}
	}

	// ----- OpenAI -----

	[Fact]
	public async Task OpenAi_Success_LogsStartAndCompletionWithTheServedModel()
	{
		var handler = new FakeHttpMessageHandler().Respond(HttpStatusCode.OK, OpenAiSuccessBody);

		await CreateOpenAi(handler).CreateChatCompletion(Messages(), OpenAiModel, Options());

		Assert.Equal(2, _lines.Count);
		Assert.StartsWith("LLM request starting (", _lines[0], StringComparison.Ordinal);
		Assert.Contains("entryPoint=prompt hotkey", _lines[0], StringComparison.Ordinal);
		Assert.Contains("provider=OpenAI", _lines[0], StringComparison.Ordinal);
		Assert.Contains("requestedModel=gpt-5.6,", _lines[0], StringComparison.Ordinal);
		Assert.Contains("fastMode=False", _lines[0], StringComparison.Ordinal);

		Assert.StartsWith("LLM request completed (", _lines[1], StringComparison.Ordinal);
		Assert.Contains("requestedModel=gpt-5.6,", _lines[1], StringComparison.Ordinal);
		Assert.Contains("servedModel=gpt-5.6-2026-09-01", _lines[1], StringComparison.Ordinal);
		Assert.Contains("serviceTier=priority", _lines[1], StringComparison.Ordinal);
		Assert.Contains("inputTokens=12", _lines[1], StringComparison.Ordinal);
		Assert.Contains("outputTokens=3", _lines[1], StringComparison.Ordinal);
		AssertNothingPrivateLogged();
	}

	[Fact]
	public async Task OpenAi_FastModeRejected_CompletionSaysItRanAtStandardSpeed()
	{
		var handler = new FakeHttpMessageHandler()
			.Respond(HttpStatusCode.BadRequest,
				"""{"error":{"message":"Invalid service_tier: fast is not available for this model.","type":"invalid_request_error","param":"service_tier","code":null}}""")
			.Respond(HttpStatusCode.OK, OpenAiSuccessBody);

		await CreateOpenAi(handler).CreateChatCompletion(Messages(), OpenAiModel, Options(fastMode: true));

		Assert.Contains("fastMode=True", _lines[0], StringComparison.Ordinal);
		Assert.Contains("fastModeSent=False", _lines[^1], StringComparison.Ordinal);
	}

	[Fact]
	public async Task OpenAi_Failure_LogsTheRequestedModelWithTheError()
	{
		var handler = new FakeHttpMessageHandler().Respond(HttpStatusCode.Unauthorized,
			"""{"error":{"message":"Incorrect API key provided.","type":"invalid_request_error","param":null,"code":"invalid_api_key"}}""");

		await Assert.ThrowsAnyAsync<Exception>(() =>
			CreateOpenAi(handler).CreateChatCompletion(Messages(), OpenAiModel, Options()));

		Assert.Equal(2, _lines.Count);
		Assert.StartsWith("LLM request failed (", _lines[1], StringComparison.Ordinal);
		Assert.Contains("requestedModel=gpt-5.6", _lines[1], StringComparison.Ordinal);
		Assert.Contains("Incorrect API key provided.", _lines[1], StringComparison.Ordinal);
		AssertNothingPrivateLogged();
	}

	[Fact]
	public async Task OpenAi_CallerCancel_LogsACancelNotAFailure()
	{
		using var cts = new CancellationTokenSource();
		var handler = new CancelsTheCallerHandler(cts);

		await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
			CreateOpenAi(handler).CreateChatCompletion(Messages(), OpenAiModel, Options(), cts.Token));

		Assert.StartsWith("LLM request cancelled (", _lines[^1], StringComparison.Ordinal);
		Assert.DoesNotContain(_lines, line => line.StartsWith("LLM request failed", StringComparison.Ordinal));
	}

	// ----- Anthropic -----

	[Fact]
	public async Task Anthropic_Success_LogsStartAndCompletionWithTheServedModel()
	{
		var handler = new FakeHttpMessageHandler().Respond(HttpStatusCode.OK, AnthropicSuccessBody);

		await CreateAnthropic(handler).CreateChatCompletion(Messages(), AnthropicModel, Options());

		Assert.Equal(2, _lines.Count);
		Assert.StartsWith("LLM request starting (", _lines[0], StringComparison.Ordinal);
		Assert.Contains("provider=Anthropic", _lines[0], StringComparison.Ordinal);
		Assert.Contains("requestedModel=claude-opus-5,", _lines[0], StringComparison.Ordinal);

		Assert.StartsWith("LLM request completed (", _lines[1], StringComparison.Ordinal);
		Assert.Contains("servedModel=claude-opus-5-20261001", _lines[1], StringComparison.Ordinal);
		Assert.Contains("serviceTier=standard", _lines[1], StringComparison.Ordinal);
		Assert.Contains("inputTokens=21", _lines[1], StringComparison.Ordinal);
		Assert.Contains("outputTokens=4", _lines[1], StringComparison.Ordinal);
		AssertNothingPrivateLogged();
	}

	[Fact]
	public async Task Anthropic_ReplyWithoutUsage_LogsUnknownRatherThanGuessing()
	{
		var handler = new FakeHttpMessageHandler().Respond(HttpStatusCode.OK,
			"""{"content":[{"type":"text","text":"done"}]}""");

		await CreateAnthropic(handler).CreateChatCompletion(Messages(), AnthropicModel, Options());

		Assert.Contains("servedModel=unknown", _lines[1], StringComparison.Ordinal);
		Assert.Contains("inputTokens=unknown", _lines[1], StringComparison.Ordinal);
	}

	[Fact]
	public async Task Anthropic_Failure_LogsTheRequestedModelWithTheError()
	{
		var handler = new FakeHttpMessageHandler().Respond(HttpStatusCode.Unauthorized,
			"""{"type":"error","error":{"type":"authentication_error","message":"invalid x-api-key"}}""");

		await Assert.ThrowsAnyAsync<Exception>(() =>
			CreateAnthropic(handler).CreateChatCompletion(Messages(), AnthropicModel, Options()));

		Assert.Equal(2, _lines.Count);
		Assert.StartsWith("LLM request failed (", _lines[1], StringComparison.Ordinal);
		Assert.Contains("requestedModel=claude-opus-5", _lines[1], StringComparison.Ordinal);
		Assert.Contains("invalid x-api-key", _lines[1], StringComparison.Ordinal);
		AssertNothingPrivateLogged();
	}

	[Fact]
	public async Task Anthropic_Refusal_StillLogsWhatWasServed()
	{
		var handler = new FakeHttpMessageHandler().Respond(HttpStatusCode.OK,
			"""{"model":"claude-opus-5-20261001","content":[],"stop_reason":"refusal","usage":{"input_tokens":21,"output_tokens":0}}""");

		await Assert.ThrowsAsync<LlmRefusalException>(() =>
			CreateAnthropic(handler).CreateChatCompletion(Messages(), AnthropicModel, Options()));

		Assert.StartsWith("LLM request completed (", _lines[^1], StringComparison.Ordinal);
		Assert.Contains("servedModel=claude-opus-5-20261001", _lines[^1], StringComparison.Ordinal);
	}

	[Fact]
	public async Task Anthropic_CallerCancel_LogsACancelNotAFailure()
	{
		using var cts = new CancellationTokenSource();
		var handler = new CancelsTheCallerHandler(cts);

		await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
			CreateAnthropic(handler).CreateChatCompletion(Messages(), AnthropicModel, Options(), cts.Token));

		Assert.StartsWith("LLM request cancelled (", _lines[^1], StringComparison.Ordinal);
		Assert.DoesNotContain(_lines, line => line.StartsWith("LLM request failed", StringComparison.Ordinal));
	}

	// ----- Wording -----

	[Fact]
	public void Started_WithoutAnEntryPoint_SaysUnknown()
	{
		string line = LlmRequestLog.Started(LlmRequestOptions.Default, LlmProvider.OpenAI, OpenAiModel, level: null);

		Assert.Contains("entryPoint=unknown", line, StringComparison.Ordinal);
		Assert.Contains("reasoningLevel=none", line, StringComparison.Ordinal);
	}

	/// <summary>
	/// Stands in for the user pressing cancel while the request is in flight: it cancels
	/// the caller's token, then honours it as a real connection would.
	/// </summary>
	private sealed class CancelsTheCallerHandler(CancellationTokenSource caller) : HttpMessageHandler
	{
		protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
		{
			caller.Cancel();
			cancellationToken.ThrowIfCancellationRequested();
			throw new InvalidOperationException("The caller's token did not reach the request.");
		}
	}
}
