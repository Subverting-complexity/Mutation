using System;
using System.Collections.Generic;
using System.Linq;
using CognitiveSupport;

namespace Mutation.Tests;

/// <summary>
/// The reasoning level rules from the "store reasoning levels" story: the fixed lists,
/// the resolver's three rules, the limits table, and the fallback wording.
/// </summary>
public class ReasoningLevelTests
{
	private static readonly ReasoningLevel[] AllLevels = Enum.GetValues<ReasoningLevel>();

	// ---- Full lists ----

	[Fact]
	public void Anthropic_FullList_IsLightestFirst()
	{
		Assert.Equal(
			new[] { ReasoningLevel.Off, ReasoningLevel.Low, ReasoningLevel.Medium, ReasoningLevel.High, ReasoningLevel.ExtraHigh, ReasoningLevel.Max },
			ReasoningLevelCatalog.FullList(LlmProvider.Anthropic));
	}

	[Fact]
	public void OpenAi_FullList_IsLightestFirst()
	{
		Assert.Equal(
			new[] { ReasoningLevel.None, ReasoningLevel.Minimal, ReasoningLevel.Low, ReasoningLevel.Medium, ReasoningLevel.High, ReasoningLevel.ExtraHigh },
			ReasoningLevelCatalog.FullList(LlmProvider.OpenAI));
	}

	[Theory]
	[InlineData(ReasoningLevel.Off, "Off")]
	[InlineData(ReasoningLevel.None, "None")]
	[InlineData(ReasoningLevel.Minimal, "Minimal")]
	[InlineData(ReasoningLevel.Low, "Low")]
	[InlineData(ReasoningLevel.Medium, "Medium")]
	[InlineData(ReasoningLevel.High, "High")]
	[InlineData(ReasoningLevel.ExtraHigh, "Extra high")]
	[InlineData(ReasoningLevel.Max, "Max")]
	public void DisplayName_MatchesTheWordingInTheGuide(ReasoningLevel level, string expected)
	{
		Assert.Equal(expected, ReasoningLevelCatalog.DisplayName(level));
	}

	[Fact]
	public void EveryLevel_BelongsToAtLeastOneService()
	{
		var offered = ReasoningLevelCatalog.FullList(LlmProvider.Anthropic)
			.Concat(ReasoningLevelCatalog.FullList(LlmProvider.OpenAI))
			.ToHashSet();
		Assert.All(AllLevels, level => Assert.Contains(level, offered));
	}

	// ---- Resolver rule 1: the prompt's level when it is ticked ----

	[Theory]
	[InlineData(ReasoningLevel.Off)]
	[InlineData(ReasoningLevel.Low)]
	[InlineData(ReasoningLevel.Medium)]
	[InlineData(ReasoningLevel.High)]
	[InlineData(ReasoningLevel.ExtraHigh)]
	[InlineData(ReasoningLevel.Max)]
	public void Resolve_PromptLevelTicked_UsesIt_Anthropic(ReasoningLevel level)
	{
		var ticked = ReasoningLevelCatalog.FullList(LlmProvider.Anthropic);
		Assert.Equal(level, ReasoningLevelResolver.Resolve(LlmProvider.Anthropic, ticked, level));
	}

	[Theory]
	[InlineData(ReasoningLevel.None)]
	[InlineData(ReasoningLevel.Minimal)]
	[InlineData(ReasoningLevel.ExtraHigh)]
	public void Resolve_PromptLevelTicked_UsesIt_OpenAi(ReasoningLevel level)
	{
		var ticked = ReasoningLevelCatalog.FullList(LlmProvider.OpenAI);
		Assert.Equal(level, ReasoningLevelResolver.Resolve(LlmProvider.OpenAI, ticked, level));
	}

	[Fact]
	public void Resolve_ModelOverload_ReadsTheModelsProviderAndTickedLevels()
	{
		var model = new LlmModelConfig("claude", LlmProvider.Anthropic)
		{
			ReasoningLevels = new List<ReasoningLevel> { ReasoningLevel.High },
		};
		Assert.Equal(ReasoningLevel.High, ReasoningLevelResolver.Resolve(model, ReasoningLevel.High));
		Assert.Equal(ReasoningLevel.High, ReasoningLevelResolver.Resolve(model, ReasoningLevel.Low));
	}

	// ---- Resolver rule 2: the lightest ticked level, not counting Off or None ----

	[Fact]
	public void Resolve_PromptLevelNotTicked_UsesLightestTicked()
	{
		var ticked = new[] { ReasoningLevel.Max, ReasoningLevel.Medium, ReasoningLevel.High };
		Assert.Equal(ReasoningLevel.Medium, ReasoningLevelResolver.Resolve(LlmProvider.Anthropic, ticked, ReasoningLevel.Low));
	}

	[Fact]
	public void Resolve_Fallback_SkipsOff()
	{
		var ticked = new[] { ReasoningLevel.Off, ReasoningLevel.High };
		Assert.Equal(ReasoningLevel.High, ReasoningLevelResolver.Resolve(LlmProvider.Anthropic, ticked, ReasoningLevel.Low));
	}

	[Fact]
	public void Resolve_Fallback_SkipsNone()
	{
		var ticked = new[] { ReasoningLevel.None, ReasoningLevel.Minimal, ReasoningLevel.High };
		Assert.Equal(ReasoningLevel.Minimal, ReasoningLevelResolver.Resolve(LlmProvider.OpenAI, ticked, ReasoningLevel.Low));
	}

	[Fact]
	public void Resolve_PromptLevelFromTheOtherService_FallsBack()
	{
		// Max is an Anthropic level; an OpenAI model can never send it.
		var ticked = ReasoningLevelCatalog.FullList(LlmProvider.OpenAI);
		Assert.Equal(ReasoningLevel.Minimal, ReasoningLevelResolver.Resolve(LlmProvider.OpenAI, ticked, ReasoningLevel.Max));
	}

	[Fact]
	public void Resolve_TickedLevelNotOfferedByTheService_IsIgnored()
	{
		// A hand-edited file ticks Minimal (OpenAI only) on an Anthropic model.
		var ticked = new[] { ReasoningLevel.Minimal, ReasoningLevel.High };
		Assert.Equal(ReasoningLevel.High, ReasoningLevelResolver.Resolve(LlmProvider.Anthropic, ticked, ReasoningLevel.Minimal));
	}

	// ---- Resolver rule 3: no level ----

	[Fact]
	public void Resolve_NothingTicked_SendsNoLevel()
	{
		Assert.Null(ReasoningLevelResolver.Resolve(LlmProvider.Anthropic, Array.Empty<ReasoningLevel>(), ReasoningLevel.Low));
		Assert.Null(ReasoningLevelResolver.Resolve(LlmProvider.OpenAI, Array.Empty<ReasoningLevel>(), ReasoningLevel.Low));
	}

	[Fact]
	public void Resolve_NullTickedList_SendsNoLevel()
	{
		Assert.Null(ReasoningLevelResolver.Resolve(LlmProvider.Anthropic, null, ReasoningLevel.Low));
	}

	[Fact]
	public void Resolve_OnlyOffTicked_AndPromptAsksForMore_SendsNoLevel()
	{
		Assert.Null(ReasoningLevelResolver.Resolve(LlmProvider.Anthropic, new[] { ReasoningLevel.Off }, ReasoningLevel.High));
	}

	[Fact]
	public void Resolve_OnlyUnofferedLevelsTicked_SendsNoLevel()
	{
		Assert.Null(ReasoningLevelResolver.Resolve(LlmProvider.OpenAI, new[] { ReasoningLevel.Max, ReasoningLevel.Off }, ReasoningLevel.Low));
	}

	// ---- New prompts ----

	[Fact]
	public void NewPrompt_StartsAtLow()
	{
		Assert.Equal(ReasoningLevel.Low, new LlmSettings.LlmPrompt().ReasoningLevel);
	}

	[Fact]
	public void NewModel_HasNothingTicked()
	{
		Assert.Empty(new LlmModelConfig().ReasoningLevels);
	}

	// ---- Limits ----

	[Theory]
	[InlineData(null, 8_000, 1)]
	[InlineData(ReasoningLevel.Off, 8_000, 1)]
	[InlineData(ReasoningLevel.None, 8_000, 1)]
	[InlineData(ReasoningLevel.Minimal, 8_000, 1)]
	[InlineData(ReasoningLevel.Low, 8_000, 1)]
	[InlineData(ReasoningLevel.Medium, 12_000, 2)]
	[InlineData(ReasoningLevel.High, 16_000, 3)]
	[InlineData(ReasoningLevel.ExtraHigh, 16_000, 4)]
	[InlineData(ReasoningLevel.Max, 16_000, 4)]
	public void Limits_FollowTheTable(ReasoningLevel? level, int maxTokens, int multiplier)
	{
		var limits = ReasoningLimits.For(level);
		Assert.Equal(maxTokens, limits.AnthropicMaxTokens);
		Assert.Equal(multiplier, limits.WaitMultiplier);
	}

	[Fact]
	public void Limits_NeverExceedTheWholeRequestCeiling()
	{
		Assert.All(AllLevels, level => Assert.True(ReasoningLimits.For(level).AnthropicMaxTokens <= 16_000));
	}

	[Fact]
	public void Limits_WaitPerTry_ScalesTheUsersWaitTime()
	{
		Assert.Equal(TimeSpan.FromSeconds(180), ReasoningLimits.For(ReasoningLevel.High).WaitPerTry(TimeSpan.FromSeconds(60)));
		Assert.Equal(TimeSpan.FromSeconds(60), ReasoningLimits.For(null).WaitPerTry(TimeSpan.FromSeconds(60)));
	}

	// ---- Fallback wording ----

	[Fact]
	public void FallbackMessage_NamesTheModelAndTheLevel()
	{
		Assert.Equal(
			"This prompt ran without a reasoning level, because claude-opus-5 did not accept Extra high.",
			ReasoningMessages.Fallback("claude-opus-5", ReasoningLevel.ExtraHigh));
	}

	[Fact]
	public void MovedMessage_NamesTheModelAndBothLevels()
	{
		Assert.Equal(
			"gpt-5 does not have Max ticked, so the reasoning level is now Low.",
			ReasoningMessages.Moved("gpt-5", ReasoningLevel.Max, ReasoningLevel.Low));
	}

	[Fact]
	public void AppendTo_FoldsTheNoticeAfterTheOutcome()
	{
		Assert.Equal(
			"Transcript ready. This prompt ran without a reasoning level, because m did not accept High.",
			ReasoningMessages.AppendTo("Transcript ready. ", ReasoningMessages.Fallback("m", ReasoningLevel.High)));
	}

	[Theory]
	[InlineData(null)]
	[InlineData("")]
	public void AppendTo_LeavesTheOutcomeAlone_WhenThereIsNoNotice(string? notice)
	{
		Assert.Equal("Transcript ready.", ReasoningMessages.AppendTo("Transcript ready.", notice));
	}

	[Fact]
	public void AppendTo_GivesTheNoticeAlone_WhenTheOutcomeIsEmpty()
	{
		Assert.Equal("Notice.", ReasoningMessages.AppendTo("", "Notice."));
	}
}
