using System.Collections.Generic;
using System.Linq;
using CognitiveSupport;
using Mutation.Ui.Services;

namespace Mutation.Tests;

public class PromptIdBackfillTests
{
	private static List<LlmSettings.LlmPrompt> Prompts(params int[] ids) =>
		ids.Select((id, index) => new LlmSettings.LlmPrompt { Id = id, Name = $"P{index}" }).ToList();

	[Fact]
	public void Apply_AllIdsMissing_NumbersThemFromOne()
	{
		var prompts = Prompts(0, 0, 0);

		Assert.True(PromptIdBackfill.Apply(prompts));
		Assert.Equal(new[] { 1, 2, 3 }, prompts.Select(p => p.Id));
	}

	[Fact]
	public void Apply_ValidDistinctIds_LeavesThemAloneAndReportsNoChange()
	{
		var prompts = Prompts(3, 1, 7);

		Assert.False(PromptIdBackfill.Apply(prompts));
		Assert.Equal(new[] { 3, 1, 7 }, prompts.Select(p => p.Id));
	}

	[Fact]
	public void Apply_MissingIdDoesNotStealAnIdAnotherPromptAlreadyHas()
	{
		// The prompt that already owns 1 must keep it, even though it comes second.
		var prompts = Prompts(0, 1);

		Assert.True(PromptIdBackfill.Apply(prompts));
		Assert.Equal(1, prompts[1].Id);
		Assert.NotEqual(1, prompts[0].Id);
	}

	[Fact]
	public void Apply_DuplicateIds_KeepsTheFirstAndRenumbersTheRest()
	{
		var prompts = Prompts(5, 5, 5);

		Assert.True(PromptIdBackfill.Apply(prompts));
		Assert.Equal(5, prompts[0].Id);
		Assert.Equal(3, prompts.Select(p => p.Id).Distinct().Count());
	}

	[Fact]
	public void Apply_NegativeIdIsTreatedAsMissing()
	{
		var prompts = Prompts(-4, 2);

		Assert.True(PromptIdBackfill.Apply(prompts));
		Assert.True(prompts[0].Id > 0);
		Assert.Equal(2, prompts[1].Id);
	}

	[Theory]
	[InlineData(new int[0])]
	[InlineData(new[] { 0, 0, 3, 3, 1 })]
	[InlineData(new[] { 9, 0, 9, 0, 1 })]
	public void Apply_AlwaysLeavesEveryIdPositiveAndUnique(int[] ids)
	{
		var prompts = Prompts(ids);

		PromptIdBackfill.Apply(prompts);

		Assert.All(prompts, p => Assert.True(p.Id > 0));
		Assert.Equal(prompts.Count, prompts.Select(p => p.Id).Distinct().Count());
	}

	[Fact]
	public void Apply_IsIdempotent()
	{
		var prompts = Prompts(0, 0, 4);
		PromptIdBackfill.Apply(prompts);
		var afterFirstPass = prompts.Select(p => p.Id).ToArray();

		// A second load must not renumber anything, or the settings file would be
		// rewritten on every startup.
		Assert.False(PromptIdBackfill.Apply(prompts));
		Assert.Equal(afterFirstPass, prompts.Select(p => p.Id));
	}

	[Fact]
	public void Apply_HugeExistingId_DoesNotPushTheNextIdTowardsOverflow()
	{
		// Highest-plus-one would wrap to int.MinValue here. Assigning from the lowest
		// free number instead keeps every Id positive.
		var prompts = Prompts(int.MaxValue, 0, 0);

		Assert.True(PromptIdBackfill.Apply(prompts));
		Assert.Equal(int.MaxValue, prompts[0].Id);
		Assert.Equal(new[] { 1, 2 }, prompts.Skip(1).Select(p => p.Id));
	}

	[Fact]
	public void Apply_SamePromptObjectInTwoSlots_ConvergesInsteadOfRenumberingForever()
	{
		// Newtonsoft honours $id/$ref, so a hand-written file can alias one prompt into
		// two slots. Assigning to it twice would move both slots together, leaving the
		// list permanently "duplicated" and rewriting the settings file on every start.
		var shared = new LlmSettings.LlmPrompt { Id = 0, Name = "Shared" };
		var prompts = new List<LlmSettings.LlmPrompt> { shared, shared };

		Assert.True(PromptIdBackfill.Apply(prompts));
		Assert.True(shared.Id > 0);
		Assert.False(PromptIdBackfill.Apply(prompts));
	}

	[Fact]
	public void Apply_EmptyOrNull_ReportsNoChange()
	{
		Assert.False(PromptIdBackfill.Apply(new List<LlmSettings.LlmPrompt>()));
		Assert.False(PromptIdBackfill.Apply(null));
	}
}
