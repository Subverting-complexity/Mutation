using CognitiveSupport;

namespace Mutation.Tests;

public class LlmServiceFastModeTests
{
	private static readonly LlmModelConfig Config =
		new("gpt-5.6", LlmProvider.OpenAI, customTemperature: null);

	[Fact]
	public void BuildChatOptions_SetsFastServiceTier_WhenFastModeOn()
	{
#pragma warning disable OPENAI001 // Evaluation-only SDK surface; see LlmService.BuildChatOptions.
		var options = LlmService.BuildChatOptions(Config, fastMode: true);

		Assert.NotNull(options.ServiceTier);
		Assert.Equal("fast", options.ServiceTier!.Value.ToString());
#pragma warning restore OPENAI001
	}

	[Fact]
	public void BuildChatOptions_LeavesServiceTierUnset_WhenFastModeOff()
	{
#pragma warning disable OPENAI001
		Assert.Null(LlmService.BuildChatOptions(Config, fastMode: false).ServiceTier);
#pragma warning restore OPENAI001
	}

	[Fact]
	public void BuildChatOptions_DefaultsToStandardSpeed()
	{
#pragma warning disable OPENAI001
		Assert.Null(LlmService.BuildChatOptions(Config).ServiceTier);
#pragma warning restore OPENAI001
	}

	[Fact]
	public void BuildChatOptions_FastModeDoesNotDisturbTemperature()
	{
		var withTemperature = new LlmModelConfig("gpt-5.6", LlmProvider.OpenAI, customTemperature: 0.25m);

		var options = LlmService.BuildChatOptions(withTemperature, fastMode: true);

		Assert.Equal(0.25f, options.Temperature!.Value, precision: 4);
	}
}
