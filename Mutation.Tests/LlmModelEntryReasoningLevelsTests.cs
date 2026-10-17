using System.Collections.Generic;
using System.Linq;
using CognitiveSupport;
using Mutation.Ui;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;

namespace Mutation.Tests;

/// <summary>
/// What the reasoning levels button on a model row shows, says to a screen reader,
/// offers in its picker, and saves.
/// </summary>
public class LlmModelEntryReasoningLevelsTests
{
	private static LlmModelEntry Entry(string name, LlmProvider provider, params ReasoningLevel[] ticked) =>
		new(new LlmModelConfig(name, provider) { ReasoningLevels = ticked.ToList() });

	private static List<string> ChangedProperties(LlmModelEntry entry)
	{
		var changed = new List<string>();
		entry.PropertyChanged += (_, e) => changed.Add(e.PropertyName!);
		return changed;
	}

	[Fact]
	public void Text_SummarisesTheTickedLevels()
	{
		var entry = Entry("claude", LlmProvider.Anthropic,
			ReasoningLevel.Low, ReasoningLevel.Medium, ReasoningLevel.High, ReasoningLevel.ExtraHigh, ReasoningLevel.Max);

		Assert.Equal("Reasoning levels: Low to Max", entry.ReasoningLevelsText);
	}

	[Fact]
	public void Text_SaysNone_ForANewModel()
	{
		var entry = new LlmModelEntry(new LlmModelConfig());

		Assert.Equal("Reasoning levels: none", entry.ReasoningLevelsText);
	}

	[Fact]
	public void AccessibleName_IncludesTheModelNameAndTheLevels()
	{
		var entry = Entry("gpt-5", LlmProvider.OpenAI, ReasoningLevel.None, ReasoningLevel.Low, ReasoningLevel.High);

		Assert.Equal("Reasoning levels for gpt-5: None, Low, High", entry.ReasoningLevelsAccessibleName);
		Assert.Equal("Reasoning levels for gpt-5", entry.ReasoningLevelsHeading);
	}

	[Fact]
	public void AccessibleName_ForABlankName_SaysUnnamedModel()
	{
		var entry = Entry("  ", LlmProvider.OpenAI);

		Assert.Equal("Reasoning levels for unnamed model: none", entry.ReasoningLevelsAccessibleName);
	}

	[Fact]
	public void Choices_AreTheServiceListLightestFirst_WithTickedState()
	{
		var entry = Entry("claude", LlmProvider.Anthropic, ReasoningLevel.High, ReasoningLevel.Off);

		var choices = entry.GetReasoningLevelChoices();

		Assert.Equal(ReasoningLevelCatalog.FullList(LlmProvider.Anthropic), choices.Select(c => c.Level));
		Assert.Equal(new[] { "Off", "Low", "Medium", "High", "Extra high", "Max" }, choices.Select(c => c.DisplayName));
		Assert.Equal(new[] { true, false, false, true, false, false }, choices.Select(c => c.IsTicked));
	}

	[Fact]
	public void Choices_FollowTheServiceAfterItChanges()
	{
		var entry = Entry("model", LlmProvider.Anthropic, ReasoningLevel.Low, ReasoningLevel.Max);

		entry.Provider = LlmProvider.OpenAI;
		var choices = entry.GetReasoningLevelChoices();

		Assert.Equal(ReasoningLevelCatalog.FullList(LlmProvider.OpenAI), choices.Select(c => c.Level));
		Assert.Equal(new[] { ReasoningLevel.Low }, choices.Where(c => c.IsTicked).Select(c => c.Level));
		Assert.Equal("Reasoning levels: Low", entry.ReasoningLevelsText);
	}

	[Fact]
	public void SetReasoningLevels_SavesInServiceOrder_EachOnce()
	{
		var config = new LlmModelConfig("claude", LlmProvider.Anthropic);
		var entry = new LlmModelEntry(config);

		entry.SetReasoningLevels(new[] { ReasoningLevel.Max, ReasoningLevel.Off, ReasoningLevel.Max, ReasoningLevel.Medium });

		Assert.Equal(new[] { ReasoningLevel.Off, ReasoningLevel.Medium, ReasoningLevel.Max }, config.ReasoningLevels);
	}

	[Fact]
	public void SetReasoningLevels_DropsLevelsTheServiceDoesNotOffer()
	{
		var config = new LlmModelConfig("model", LlmProvider.Anthropic) { ReasoningLevels = { ReasoningLevel.Max } };
		var entry = new LlmModelEntry(config);
		entry.Provider = LlmProvider.OpenAI;

		entry.SetReasoningLevels(new[] { ReasoningLevel.Max, ReasoningLevel.Minimal });

		Assert.Equal(new[] { ReasoningLevel.Minimal }, config.ReasoningLevels);
	}

	[Fact]
	public void SetReasoningLevels_ToNothing_SavesAnEmptyList()
	{
		var config = new LlmModelConfig("claude", LlmProvider.Anthropic) { ReasoningLevels = { ReasoningLevel.Low } };
		var entry = new LlmModelEntry(config);

		entry.SetReasoningLevels(System.Array.Empty<ReasoningLevel>());

		Assert.Empty(config.ReasoningLevels);
		Assert.Equal("Reasoning levels: none", entry.ReasoningLevelsText);
	}

	[Fact]
	public void SetReasoningLevels_RaisesChangesForTheButtonTextAndName()
	{
		var entry = Entry("claude", LlmProvider.Anthropic);
		var changed = ChangedProperties(entry);

		entry.SetReasoningLevels(new[] { ReasoningLevel.Low });

		Assert.Contains(nameof(LlmModelEntry.ReasoningLevelsText), changed);
		Assert.Contains(nameof(LlmModelEntry.ReasoningLevelsAccessibleName), changed);
	}

	[Fact]
	public void Renaming_RaisesAChangeForTheAccessibleName()
	{
		var entry = Entry("old", LlmProvider.OpenAI);
		var changed = ChangedProperties(entry);

		entry.Name = "new";

		Assert.Contains(nameof(LlmModelEntry.ReasoningLevelsAccessibleName), changed);
		Assert.Equal("Reasoning levels for new: none", entry.ReasoningLevelsAccessibleName);
	}

	[Fact]
	public void ChangingTheService_RaisesAChangeForTheButtonText()
	{
		var entry = Entry("model", LlmProvider.OpenAI, ReasoningLevel.Minimal);
		var changed = ChangedProperties(entry);

		entry.Provider = LlmProvider.Anthropic;

		Assert.Contains(nameof(LlmModelEntry.ReasoningLevelsText), changed);
		Assert.Equal("Reasoning levels: none", entry.ReasoningLevelsText);
	}

	[Fact]
	public void SavedLevels_SurviveRepeatedSettingsDialogCopies_WithoutDuplicates()
	{
		// The Settings dialog copies settings by writing JSON and reading it back, and the
		// reader adds saved items to any list the object already starts with.
		var config = new LlmModelConfig("claude", LlmProvider.Anthropic);
		new LlmModelEntry(config).SetReasoningLevels(new[] { ReasoningLevel.Low, ReasoningLevel.High });
		var serializer = new JsonSerializerSettings { Converters = { new StringEnumConverter() } };

		var copy = JsonConvert.DeserializeObject<LlmModelConfig>(JsonConvert.SerializeObject(config, serializer), serializer)!;
		var copyOfCopy = JsonConvert.DeserializeObject<LlmModelConfig>(JsonConvert.SerializeObject(copy, serializer), serializer)!;

		Assert.Equal(new[] { ReasoningLevel.Low, ReasoningLevel.High }, copyOfCopy.ReasoningLevels);
	}
}
