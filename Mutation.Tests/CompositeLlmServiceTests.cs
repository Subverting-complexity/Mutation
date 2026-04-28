using CognitiveSupport;

namespace Mutation.Tests;

public class CompositeLlmServiceTests
{
	[Theory]
	[InlineData("claude-sonnet-4-6", true)]
	[InlineData("claude-3-opus", true)]
	[InlineData("Claude-Sonnet", true)]
	[InlineData("CLAUDE", true)]
	[InlineData("gpt-4", false)]
	[InlineData("gpt-4.1", false)]
	[InlineData("o1-mini", false)]
	[InlineData("", false)]
	[InlineData("anthropic-claude", false)]
	public void IsAnthropicModel_RecognizesPrefix(string model, bool expected)
	{
		Assert.Equal(expected, CompositeLlmService.IsAnthropicModel(model));
	}

	[Fact]
	public async Task CreateChatCompletion_AnthropicModel_RoutesToAnthropicService()
	{
		var openAi = new StubLlmService("from-openai");
		var anthropic = new StubLlmService("from-anthropic");
		var composite = new CompositeLlmService(openAi, anthropic);

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
		var composite = new CompositeLlmService(openAi, anthropic);

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
	public async Task CreateChatCompletion_AnthropicModelButServiceNull_ThrowsWithHelpfulMessage()
	{
		var composite = new CompositeLlmService(openAiService: new StubLlmService("openai"), anthropicService: null);

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
		var composite = new CompositeLlmService(openAiService: null, anthropicService: new StubLlmService("anthropic"));

		var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
			composite.CreateChatCompletion(
				new List<LlmChatMessage> { new(LlmChatRole.User, "hi") },
				"gpt-4"));

		Assert.Contains("OpenAI", ex.Message, StringComparison.OrdinalIgnoreCase);
		Assert.Contains("ApiKey", ex.Message);
	}

	[Fact]
	public async Task CreateChatCompletion_PassesTemperatureThrough()
	{
		var anthropic = new StubLlmService("ok");
		var composite = new CompositeLlmService(openAiService: null, anthropicService: anthropic);

		await composite.CreateChatCompletion(
			new List<LlmChatMessage> { new(LlmChatRole.User, "hi") },
			"claude-sonnet-4-6",
			temperature: 0.2m);

		Assert.Equal(0.2m, anthropic.LastTemperature);
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
		public decimal LastTemperature { get; private set; }

		public Task<string> CreateChatCompletion(IList<LlmChatMessage> messages, string llmModelName, decimal temperature = 0.7M)
		{
			CallCount++;
			LastMessages = messages;
			LastModel = llmModelName;
			LastTemperature = temperature;
			return Task.FromResult(_response);
		}
	}
}
