using System.Collections.Generic;
using CognitiveSupport;

namespace Mutation.Tests;

/// <summary>
/// The few words on each model row's reasoning levels button: ranges for runs of three
/// or more neighbouring levels, commas otherwise, and "none" when nothing usable is ticked.
/// </summary>
public class ReasoningLevelSummaryTests
{
	[Fact]
	public void NothingTicked_IsNone()
	{
		Assert.Equal("none", ReasoningLevelSummary.Describe(LlmProvider.Anthropic, new List<ReasoningLevel>()));
	}

	[Fact]
	public void NullList_IsNone()
	{
		Assert.Equal("none", ReasoningLevelSummary.Describe(LlmProvider.OpenAI, null));
	}

	[Fact]
	public void NeighbouringRun_IsARange()
	{
		var ticked = new[] { ReasoningLevel.Low, ReasoningLevel.Medium, ReasoningLevel.High, ReasoningLevel.ExtraHigh, ReasoningLevel.Max };
		Assert.Equal("Low to Max", ReasoningLevelSummary.Describe(LlmProvider.Anthropic, ticked));
	}

	[Fact]
	public void Gaps_AreListedWithCommas()
	{
		var ticked = new[] { ReasoningLevel.Off, ReasoningLevel.Low, ReasoningLevel.High };
		Assert.Equal("Off, Low, High", ReasoningLevelSummary.Describe(LlmProvider.Anthropic, ticked));
	}

	[Fact]
	public void TwoNeighbours_StayAList()
	{
		var ticked = new[] { ReasoningLevel.Low, ReasoningLevel.Medium };
		Assert.Equal("Low, Medium", ReasoningLevelSummary.Describe(LlmProvider.OpenAI, ticked));
	}

	[Fact]
	public void FullOpenAiList_IsNoneToExtraHigh()
	{
		Assert.Equal("None to Extra high",
			ReasoningLevelSummary.Describe(LlmProvider.OpenAI, ReasoningLevelCatalog.FullList(LlmProvider.OpenAI)));
	}

	[Fact]
	public void SingleLevelThenRange_CombinesBoth()
	{
		var ticked = new[] { ReasoningLevel.Off, ReasoningLevel.Medium, ReasoningLevel.High, ReasoningLevel.ExtraHigh };
		Assert.Equal("Off, Medium to Extra high", ReasoningLevelSummary.Describe(LlmProvider.Anthropic, ticked));
	}

	[Fact]
	public void TickedOrder_DoesNotMatter_AndDuplicatesCountOnce()
	{
		var ticked = new[] { ReasoningLevel.High, ReasoningLevel.Low, ReasoningLevel.Medium, ReasoningLevel.Low };
		Assert.Equal("Low to High", ReasoningLevelSummary.Describe(LlmProvider.Anthropic, ticked));
	}

	[Fact]
	public void LevelsTheServiceDoesNotOffer_AreLeftOut()
	{
		// Max is Anthropic only; Minimal and None are OpenAI only.
		Assert.Equal("Low", ReasoningLevelSummary.Describe(LlmProvider.OpenAI, new[] { ReasoningLevel.Max, ReasoningLevel.Low }));
		Assert.Equal("none", ReasoningLevelSummary.Describe(LlmProvider.Anthropic, new[] { ReasoningLevel.Minimal, ReasoningLevel.None }));
	}

	[Fact]
	public void LevelsAreNeighboursOnlyInTheirOwnServiceList()
	{
		// On OpenAI, Minimal sits between None and Low, so None and Low are not neighbours.
		var ticked = new[] { ReasoningLevel.None, ReasoningLevel.Low, ReasoningLevel.Medium };
		Assert.Equal("None, Low, Medium", ReasoningLevelSummary.Describe(LlmProvider.OpenAI, ticked));

		// On Anthropic, Off and Low are neighbours.
		var anthropic = new[] { ReasoningLevel.Off, ReasoningLevel.Low, ReasoningLevel.Medium };
		Assert.Equal("Off to Medium", ReasoningLevelSummary.Describe(LlmProvider.Anthropic, anthropic));
	}
}
