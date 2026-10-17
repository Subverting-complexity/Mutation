using System.Net;
using System.Net.Http;
using CognitiveSupport;

namespace Mutation.Tests;

/// <summary>
/// How a successful Anthropic reply is read: only text blocks count as the answer, an
/// answer that hit the length limit is returned with a notice, and a refusal is a failure
/// rather than an empty success.
/// </summary>
public class AnthropicReplyTests : IDisposable
{
	private const string Model = "claude-opus-5";

	private readonly List<HttpClient> _clients = new();

	public void Dispose()
	{
		foreach (var client in _clients)
			client.Dispose();
	}

	private AnthropicLlmService CreateService(FakeHttpMessageHandler handler)
	{
		var client = new HttpClient(handler);
		_clients.Add(client);
		return new(
			"test-key",
			new[] { new LlmModelConfig(Model, LlmProvider.Anthropic, customTemperature: null) },
			client,
			timeoutSeconds: 5,
			retryCount: 0);
	}

	private static IList<LlmChatMessage> Messages() =>
		new List<LlmChatMessage>
		{
			new(LlmChatRole.System, "be brief"),
			new(LlmChatRole.User, "hello"),
		};

	// --- Parsing on raw JSON ---

	[Fact]
	public void Parse_ThinkingBlockBeforeText_ReturnsOnlyTheText()
	{
		var reply = AnthropicReply.Parse("""
			{"content":[
				{"type":"thinking","thinking":"Let me reason about this.","signature":"abc"},
				{"type":"text","text":"The answer."}
			],"stop_reason":"end_turn"}
			""");

		Assert.Equal("The answer.", reply.Text);
		Assert.Equal("end_turn", reply.StopReason);
		Assert.False(reply.WasCutShort);
		Assert.False(reply.WasRefused);
	}

	[Fact]
	public void Parse_RedactedThinkingBlock_IsSkipped()
	{
		var reply = AnthropicReply.Parse("""
			{"content":[{"type":"redacted_thinking","data":"xyz"},{"type":"text","text":"ok"}],"stop_reason":"end_turn"}
			""");

		Assert.Equal("ok", reply.Text);
	}

	[Fact]
	public void Parse_SeveralTextBlocks_AreJoinedInOrder()
	{
		var reply = AnthropicReply.Parse("""
			{"content":[{"type":"text","text":"one "},{"type":"thinking","thinking":"hm"},{"type":"text","text":"two"}]}
			""");

		Assert.Equal("one two", reply.Text);
		Assert.Null(reply.StopReason);
	}

	[Theory]
	[InlineData("""{"content":[]}""")]
	[InlineData("""{}""")]
	[InlineData("""{"content":null}""")]
	public void Parse_NoTextBlocks_ReturnsEmptyText(string body)
	{
		Assert.Equal(string.Empty, AnthropicReply.Parse(body).Text);
	}

	[Fact]
	public void Parse_MaxTokens_IsCutShort()
	{
		var reply = AnthropicReply.Parse("""{"content":[{"type":"text","text":"partial"}],"stop_reason":"max_tokens"}""");

		Assert.True(reply.WasCutShort);
		Assert.Equal("partial", reply.Text);
	}

	// --- Through the service ---

	[Fact]
	public async Task ThinkingThenText_ReturnsOnlyTheText()
	{
		var handler = new FakeHttpMessageHandler().Respond(HttpStatusCode.OK, """
			{"content":[{"type":"thinking","thinking":"secret reasoning"},{"type":"text","text":"done"}],"stop_reason":"end_turn"}
			""");

		string result = await CreateService(handler).CreateChatCompletion(Messages(), Model);

		Assert.Equal("done", result);
	}

	[Fact]
	public async Task MaxTokens_ReturnsTheTextAndRaisesCutShort()
	{
		var handler = new FakeHttpMessageHandler().Respond(HttpStatusCode.OK,
			"""{"content":[{"type":"text","text":"The first half"}],"stop_reason":"max_tokens"}""");
		int cutShortCalls = 0;

		string result = await CreateService(handler).CreateChatCompletion(
			Messages(), Model, new LlmRequestOptions { OnCutShort = () => cutShortCalls++ });

		Assert.Equal("The first half", result);
		Assert.Equal(1, cutShortCalls);
	}

	[Theory]
	[InlineData("end_turn")]
	[InlineData("stop_sequence")]
	public async Task NormalStop_DoesNotRaiseCutShort(string stopReason)
	{
		var handler = new FakeHttpMessageHandler().Respond(HttpStatusCode.OK,
			$$"""{"content":[{"type":"text","text":"done"}],"stop_reason":"{{stopReason}}"}""");
		bool cutShort = false;

		await CreateService(handler).CreateChatCompletion(
			Messages(), Model, new LlmRequestOptions { OnCutShort = () => cutShort = true });

		Assert.False(cutShort);
	}

	[Fact]
	public async Task MaxTokens_WithNoCallback_StillReturnsTheText()
	{
		var handler = new FakeHttpMessageHandler().Respond(HttpStatusCode.OK,
			"""{"content":[{"type":"text","text":"partial"}],"stop_reason":"max_tokens"}""");

		string result = await CreateService(handler).CreateChatCompletion(Messages(), Model);

		Assert.Equal("partial", result);
	}

	[Theory]
	[InlineData("""{"content":[],"stop_reason":"refusal"}""")]
	[InlineData("""{"content":[{"type":"text","text":"I started to"}],"stop_reason":"refusal"}""")]
	public async Task Refusal_ThrowsAClearFailure_NotAnEmptyResult(string body)
	{
		var handler = new FakeHttpMessageHandler().Respond(HttpStatusCode.OK, body);

		var ex = await Assert.ThrowsAsync<LlmRefusalException>(
			() => CreateService(handler).CreateChatCompletion(Messages(), Model));

		Assert.Equal(LlmRefusalException.UserMessage, ex.Message);
		// A refusal is final: the same request is not sent again.
		Assert.Single(handler.Requests);
	}

	[Fact]
	public async Task Refusal_InFastMode_IsNotRetriedAtStandardSpeed()
	{
		var handler = new FakeHttpMessageHandler().Respond(HttpStatusCode.OK,
			"""{"content":[],"stop_reason":"refusal"}""");

		await Assert.ThrowsAsync<LlmRefusalException>(
			() => CreateService(handler).CreateChatCompletion(
				Messages(), Model, new LlmRequestOptions { FastMode = true }));

		Assert.Single(handler.Requests);
	}

	[Fact]
	public async Task Request_IsSentExactlyAsBefore()
	{
		var handler = new FakeHttpMessageHandler().Respond(HttpStatusCode.OK,
			"""{"content":[{"type":"text","text":"done"}],"stop_reason":"end_turn"}""");

		await CreateService(handler).CreateChatCompletion(Messages(), Model);

		var request = Assert.Single(handler.Requests);
		Assert.Equal(
			"""{"model":"claude-opus-5","max_tokens":4096,"system":"be brief","messages":[{"role":"user","content":"hello"}]}""",
			request.Body);
		Assert.Equal("2023-06-01", request.Header("anthropic-version"));
		Assert.False(request.HasHeader("anthropic-beta"));
	}
}
