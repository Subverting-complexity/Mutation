using System;
using System.IO;
using System.Linq;
using CognitiveSupport;
using Mutation.Ui.Services;
using Mutation.Ui.Views.SettingsUi;
using Newtonsoft.Json.Linq;

namespace Mutation.Tests;

/// <summary>
/// The stored side of reasoning levels: the one-time upgrade of an older settings file,
/// that a user's choices survive saving and loading, and that the settings dialog's copy
/// does not duplicate the ticked levels.
/// </summary>
// Loading and saving settings replaces ErrorLogger's process-wide secret snapshot, so
// this class shares the collection that serialises those statics (issue #250).
[Collection(ErrorLoggerCollection.Name)]
public class ReasoningLevelSettingsTests : IDisposable
{
	private readonly TempSettingsFile _settingsFile = new("reasoning");

	private string _tempPath => _settingsFile.FilePath;

	public void Dispose() => _settingsFile.Dispose();

	private const string OlderFile = """
		{
			"LlmSettings": {
				"Models": [
					{ "Name": "gpt-4.1", "Provider": "OpenAI" },
					{ "Name": "claude-opus-5", "Provider": "Anthropic", "CustomTemperature": 0.7 }
				],
				"Prompts": [
					{ "Id": 1, "Name": "Tidy", "Content": "x", "ModelName": "gpt-4.1" },
					{ "Id": 2, "Name": "Think", "Content": "y", "ModelName": "claude-opus-5" }
				]
			}
		}
		""";

	[Fact]
	public void OlderFile_LoadsWithFullListsAndLowPrompts()
	{
		File.WriteAllText(_tempPath, OlderFile);

		var llm = new SettingsManager(_tempPath).LoadAndEnsureSettings().LlmSettings!;

		Assert.Equal(ReasoningLevelCatalog.FullList(LlmProvider.OpenAI), llm.Models.Single(m => m.Name == "gpt-4.1").ReasoningLevels);
		Assert.Equal(ReasoningLevelCatalog.FullList(LlmProvider.Anthropic), llm.Models.Single(m => m.Name == "claude-opus-5").ReasoningLevels);
		Assert.All(llm.Prompts, p => Assert.Equal(ReasoningLevel.Low, p.ReasoningLevel));
	}

	[Fact]
	public void OlderFile_UpgradeIsWrittenBackToTheFile()
	{
		File.WriteAllText(_tempPath, OlderFile);

		new SettingsManager(_tempPath).UpgradeSettings();

		var json = JObject.Parse(File.ReadAllText(_tempPath));
		Assert.Equal(
			new[] { "Off", "Low", "Medium", "High", "ExtraHigh", "Max" },
			json["LlmSettings"]!["Models"]![1]!["ReasoningLevels"]!.Values<string>());
		Assert.Equal("Low", json["LlmSettings"]!["Prompts"]![0]!["ReasoningLevel"]!.Value<string>());
	}

	[Fact]
	public void SavingAgain_DoesNotRepeatTheUpgrade()
	{
		File.WriteAllText(_tempPath, OlderFile);
		var manager = new SettingsManager(_tempPath);
		var settings = manager.LoadAndEnsureSettings();

		// The user unticks everything on one model, narrows another, and turns a prompt up.
		settings.LlmSettings!.Models.Single(m => m.Name == "gpt-4.1").ReasoningLevels.Clear();
		settings.LlmSettings.Models.Single(m => m.Name == "claude-opus-5").ReasoningLevels =
			new() { ReasoningLevel.High };
		settings.LlmSettings.Prompts.Single(p => p.Id == 2).ReasoningLevel = ReasoningLevel.Max;
		manager.SaveSettingsToFile(settings);

		var reloaded = new SettingsManager(_tempPath).LoadAndEnsureSettings().LlmSettings!;

		Assert.Empty(reloaded.Models.Single(m => m.Name == "gpt-4.1").ReasoningLevels);
		Assert.Equal(new[] { ReasoningLevel.High }, reloaded.Models.Single(m => m.Name == "claude-opus-5").ReasoningLevels);
		Assert.Equal(ReasoningLevel.Max, reloaded.Prompts.Single(p => p.Id == 2).ReasoningLevel);
	}

	[Fact]
	public void UpgradeSettings_OnAnUpgradedFile_ChangesNothing()
	{
		File.WriteAllText(_tempPath, OlderFile);
		new SettingsManager(_tempPath).UpgradeSettings();
		string once = File.ReadAllText(_tempPath);

		new SettingsManager(_tempPath).UpgradeSettings();

		Assert.Equal(once, File.ReadAllText(_tempPath));
	}

	[Fact]
	public void Upgrade_ReadsAProviderStoredAsANumber()
	{
		var llm = JObject.Parse("""{ "Models": [ { "Name": "c", "Provider": 2 } ] }""");

		Assert.True(ReasoningLevelsUpgrade.Apply(llm));

		Assert.Equal("Max", llm["Models"]![0]!["ReasoningLevels"]!.Last!.Value<string>());
	}

	[Fact]
	public void Upgrade_ModelWithNoProvider_GetsTheOpenAiList()
	{
		var llm = JObject.Parse("""{ "Models": [ { "Name": "m" } ] }""");

		ReasoningLevelsUpgrade.Apply(llm);

		Assert.Equal("None", llm["Models"]![0]!["ReasoningLevels"]!.First!.Value<string>());
	}

	[Fact]
	public void LegacyStringModels_AreUpgradedAfterConversion()
	{
		File.WriteAllText(_tempPath, """
			{ "LlmSettings": { "Models": [ "gpt-4.1" ], "Prompts": [ { "Id": 1, "Name": "P", "Content": "x", "ModelName": "gpt-4.1" } ] } }
			""");

		var llm = new SettingsManager(_tempPath).LoadAndEnsureSettings().LlmSettings!;

		Assert.Equal(ReasoningLevelCatalog.FullList(LlmProvider.OpenAI), Assert.Single(llm.Models).ReasoningLevels);
	}

	[Fact]
	public void ExplicitNullLevels_LoadAsNothingTicked()
	{
		File.WriteAllText(_tempPath, """
			{ "LlmSettings": { "Models": [ { "Name": "gpt-4.1", "Provider": "OpenAI", "ReasoningLevels": null } ] } }
			""");

		var llm = new SettingsManager(_tempPath).LoadAndEnsureSettings().LlmSettings!;

		Assert.Empty(Assert.Single(llm.Models).ReasoningLevels);
	}

	[Fact]
	public void NewSettingsFile_StarterModelsHaveTheirFullLists()
	{
		var llm = new SettingsManager(_tempPath).LoadAndEnsureSettings().LlmSettings!;

		Assert.NotEmpty(llm.Models);
		Assert.All(llm.Models, m => Assert.Equal(ReasoningLevelCatalog.FullList(m.Provider), m.ReasoningLevels));
		Assert.All(llm.Prompts, p => Assert.Equal(ReasoningLevel.Low, p.ReasoningLevel));
	}

	[Fact]
	public void SettingsWorkingCopy_DoesNotDuplicateTickedLevels()
	{
		// The dialog copies by writing JSON and reading it back. A list that started
		// non-empty would have the saved levels added on top of it.
		var live = new Settings { LlmSettings = new LlmSettings() };
		live.LlmSettings.Models.Add(new LlmModelConfig("claude-opus-5", LlmProvider.Anthropic)
		{
			ReasoningLevels = new() { ReasoningLevel.Low, ReasoningLevel.High },
		});
		live.LlmSettings.Prompts.Add(new LlmSettings.LlmPrompt { Id = 1, ReasoningLevel = ReasoningLevel.High });

		var working = SettingsWorkingCopy.Clone(live);
		SettingsWorkingCopy.CommitInto(live, working);

		Assert.Equal(new[] { ReasoningLevel.Low, ReasoningLevel.High }, Assert.Single(live.LlmSettings.Models).ReasoningLevels);
		Assert.Equal(ReasoningLevel.High, Assert.Single(live.LlmSettings.Prompts).ReasoningLevel);
	}
}
