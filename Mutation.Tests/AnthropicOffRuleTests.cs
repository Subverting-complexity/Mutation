using CognitiveSupport;

namespace Mutation.Tests;

/// <summary>
/// Which way Off asks each Anthropic model not to think, read from the model's name.
/// </summary>
public class AnthropicOffRuleTests
{
	[Theory]
	[InlineData("claude-sonnet-5-5", AnthropicOffSetting.BetweenTools)]
	[InlineData("claude-opus-5-5", AnthropicOffSetting.LowEffort)]
	[InlineData("claude-fable-5", AnthropicOffSetting.LowEffort)]
	[InlineData("claude-fable-5-1", AnthropicOffSetting.LowEffort)]
	[InlineData("claude-mythos-5", AnthropicOffSetting.LowEffort)]
	[InlineData("claude-opus-5", AnthropicOffSetting.Disabled)]
	[InlineData("claude-haiku-5-5", AnthropicOffSetting.Disabled)]
	[InlineData("claude-sonnet-5", AnthropicOffSetting.Disabled)]
	[InlineData("claude-opus-4-8", AnthropicOffSetting.Disabled)]
	[InlineData("claude-opus-4-7", AnthropicOffSetting.Disabled)]
	public void For_KnownModels(string modelName, AnthropicOffSetting expected)
	{
		Assert.Equal(expected, AnthropicOffRule.For(modelName));
	}

	[Theory]
	[InlineData("claude-opus-5-5-20260401", AnthropicOffSetting.LowEffort)]
	[InlineData("anthropic.claude-opus-5-5", AnthropicOffSetting.LowEffort)]
	[InlineData("us.anthropic.claude-sonnet-5-5", AnthropicOffSetting.BetweenTools)]
	[InlineData("Claude-Sonnet-5-5", AnthropicOffSetting.BetweenTools)]
	public void For_FindsTheModelIdInsideALongerName(string modelName, AnthropicOffSetting expected)
	{
		Assert.Equal(expected, AnthropicOffRule.For(modelName));
	}

	[Theory]
	[InlineData("claude-opus-5-50")]
	[InlineData("claude-sonnet-5-55")]
	[InlineData("claude-fable-50")]
	[InlineData("my-proxy-model")]
	[InlineData("")]
	[InlineData(null)]
	public void For_UnknownOrLookAlikeNames_KeepDisabled(string? modelName)
	{
		Assert.Equal(AnthropicOffSetting.Disabled, AnthropicOffRule.For(modelName));
	}
}
