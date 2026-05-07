using CognitiveSupport;

namespace Mutation.Tests;

public class LlmServiceTemperatureTests
{
	[Fact]
	public void BuildChatOptions_SetsTemperature_WhenConfigured()
	{
		var config = new LlmModelConfig("gpt-4.1", LlmProvider.OpenAI, customTemperature: 0.42m);

		var options = LlmService.BuildChatOptions(config);

		Assert.NotNull(options.Temperature);
		Assert.Equal(0.42f, options.Temperature.Value, precision: 4);
	}

	[Fact]
	public void BuildChatOptions_OmitsTemperature_WhenNotConfigured()
	{
		var config = new LlmModelConfig("chat-latest", LlmProvider.OpenAI, customTemperature: null);

		var options = LlmService.BuildChatOptions(config);

		Assert.Null(options.Temperature);
	}
}
