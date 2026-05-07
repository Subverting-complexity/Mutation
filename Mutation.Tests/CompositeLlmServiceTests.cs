using CognitiveSupport;

namespace Mutation.Tests;

public class CompositeLlmServiceTests
{
	private static IReadOnlyDictionary<string, LlmProvider> Providers(params (string name, LlmProvider provider)[] entries)
		=> entries.ToDictionary(e => e.name, e => e.provider, StringComparer.OrdinalIgnoreCase);

	[Fact]
	public async Task CreateChatCompletion_AnthropicModel_RoutesToAnthropicService()
	{
		var openAi = new StubLlmService("from-openai");
		var anthropic = new StubLlmService("from-anthropic");
		var providers = Providers(("claude-sonnet-4-6", LlmProvider.Anthropic));
		var composite = new CompositeLlmService(openAi, anthropic, providers);

		var messages = new List<LlmChatMessage>
		{
			new(LlmChatRole.User, "hi"),
		};

		string result = await composite.CreateChatCompletion(messages, "claude-sonnet-4-6");

		Assert.Equal("from-anthropic", result);
		Assert.Equal(1, anthropic.CallCount);
		Assert.Equal(0, openAi.CallCount);
		Assert.Equal("claude-sonnet-4-6", anthropic.LastModel);
	}

	[Fact]
	public async Task CreateChatCompletion_OpenAiModel_RoutesToOpenAiService()
	{
		var openAi = new StubLlmService("from-openai");
		var anthropic = new StubLlmService("from-anthropic");
		var providers = Providers(("gpt-4.1", LlmProvider.OpenAI));
		var composite = new CompositeLlmService(openAi, anthropic, providers);

		var messages = new List<LlmChatMessage>
		{
			new(LlmChatRole.User, "hi"),
		};

		string result = await composite.CreateChatCompletion(messages, "gpt-4.1");

		Assert.Equal("from-openai", result);
		Assert.Equal(1, openAi.CallCount);
		Assert.Equal(0, anthropic.CallCount);
		Assert.Equal("gpt-4.1", openAi.LastModel);
	}

	[Fact]
	public async Task CreateChatCompletion_RoutesByExplicitProvider_NotByNamePrefix()
	{
		// A model named "claude-via-openai-proxy" routed explicitly to OpenAI.
		var openAi = new StubLlmService("from-openai");
		var anthropic = new StubLlmService("from-anthropic");
		var providers = Providers(("claude-via-openai-proxy", LlmProvider.OpenAI));
		var composite = new CompositeLlmService(openAi, anthropic, providers);

		string result = await composite.CreateChatCompletion(
			new List<LlmChatMessage> { new(LlmChatRole.User, "hi") },
			"claude-via-openai-proxy");

		Assert.Equal("from-openai", result);
		Assert.Equal(1, openAi.CallCount);
		Assert.Equal(0, anthropic.CallCount);
	}

	[Fact]
	public async Task CreateChatCompletion_UnknownModel_Throws()
	{
		var composite = new CompositeLlmService(
			openAiService: new StubLlmService("openai"),
			anthropicService: new StubLlmService("anthropic"),
			Providers());

		var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
			composite.CreateChatCompletion(
				new List<LlmChatMessage> { new(LlmChatRole.User, "hi") },
				"some-unknown-model"));

		Assert.Contains("not configured", ex.Message, StringComparison.OrdinalIgnoreCase);
		Assert.Contains("some-unknown-model", ex.Message);
	}

	[Fact]
	public async Task CreateChatCompletion_AnthropicModelButServiceNull_ThrowsWithHelpfulMessage()
	{
		var providers = Providers(("claude-sonnet-4-6", LlmProvider.Anthropic));
		var composite = new CompositeLlmService(
			openAiService: new StubLlmService("openai"),
			anthropicService: null,
			providers);

		var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
			composite.CreateChatCompletion(
				new List<LlmChatMessage> { new(LlmChatRole.User, "hi") },
				"claude-sonnet-4-6"));

		Assert.Contains("Anthropic", ex.Message, StringComparison.OrdinalIgnoreCase);
		Assert.Contains("AnthropicApiKey", ex.Message);
	}

	[Fact]
	public async Task CreateChatCompletion_OpenAiModelButServiceNull_ThrowsWithHelpfulMessage()
	{
		var providers = Providers(("gpt-4", LlmProvider.OpenAI));
		var composite = new CompositeLlmService(
			openAiService: null,
			anthropicService: new StubLlmService("anthropic"),
			providers);

		var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
			composite.CreateChatCompletion(
				new List<LlmChatMessage> { new(LlmChatRole.User, "hi") },
				"gpt-4"));

		Assert.Contains("OpenAI", ex.Message, StringComparison.OrdinalIgnoreCase);
		Assert.Contains("ApiKey", ex.Message);
	}

	private sealed class StubLlmService : ILlmService
	{
		private readonly string _response;

		public StubLlmService(string response)
		{
			_response = response;
		}

		public int CallCount { get; private set; }
		public IList<LlmChatMessage>? LastMessages { get; private set; }
		public string? LastModel { get; private set; }

		public Task<string> CreateChatCompletion(IList<LlmChatMessage> messages, string llmModelName)
		{
			CallCount++;
			LastMessages = messages;
			LastModel = llmModelName;
			return Task.FromResult(_response);
		}
	}
}
