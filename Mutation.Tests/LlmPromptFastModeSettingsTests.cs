using System;
using System.IO;
using System.Linq;
using CognitiveSupport;
using Mutation.Ui.Services;
using Mutation.Ui.Views.SettingsUi;
using Newtonsoft.Json.Linq;

namespace Mutation.Tests;

/// <summary>
/// FastMode is a new scalar on an existing settings shape, so the risks worth pinning
/// down are the silent ones: an older Mutation.json losing the property's default, the
/// upgrade step clobbering a saved value, and the settings working copy dropping it on
/// the way back to the live settings object.
/// </summary>
public class LlmPromptFastModeSettingsTests : IDisposable
{
	private readonly TempSettingsFile _settingsFile = new("fastmode");

	private string _tempPath => _settingsFile.FilePath;

	public void Dispose() => _settingsFile.Dispose();

	[Fact]
	public void NewPrompt_DefaultsToStandardSpeed()
	{
		Assert.False(new LlmSettings.LlmPrompt().FastMode);
	}

	[Fact]
	public void SettingsFileWithoutTheProperty_LoadsAsFalse()
	{
		File.WriteAllText(_tempPath, """
			{
				"LlmSettings": {
					"Prompts": [
						{ "Id": 1, "Name": "Legacy", "Content": "x", "ModelName": "gpt-4.1" }
					]
				}
			}
			""");

		var settings = new SettingsManager(_tempPath).LoadAndEnsureSettings();

		var prompt = Assert.Single(settings.LlmSettings!.Prompts, p => p.Name == "Legacy");
		Assert.False(prompt.FastMode);
	}

	[Fact]
	public void UpgradeSettings_LeavesASavedTrueValueAlone()
	{
		File.WriteAllText(_tempPath, """
			{
				"LlmSettings": {
					"Prompts": [
						{ "Id": 1, "Name": "Live", "Content": "x", "ModelName": "claude-opus-5", "FastMode": true }
					]
				}
			}
			""");

		var manager = new SettingsManager(_tempPath);
		manager.UpgradeSettings();

		var upgraded = JObject.Parse(File.ReadAllText(_tempPath));
		Assert.True(upgraded["LlmSettings"]!["Prompts"]![0]!["FastMode"]!.Value<bool>());
	}

	[Fact]
	public void SaveAndReload_RoundTripsFastMode()
	{
		var manager = new SettingsManager(_tempPath);
		var settings = manager.LoadAndEnsureSettings();
		settings.LlmSettings!.Prompts.Clear();
		settings.LlmSettings.Prompts.Add(new LlmSettings.LlmPrompt
		{
			Id = 42,
			Name = "Fast one",
			Content = "go",
			ModelName = "claude-opus-5",
			FastMode = true,
		});
		settings.LlmSettings.Prompts.Add(new LlmSettings.LlmPrompt
		{
			Id = 43,
			Name = "Standard one",
			Content = "go",
			ModelName = "gpt-4.1",
		});

		manager.SaveSettingsToFile(settings);
		var reloaded = new SettingsManager(_tempPath).LoadAndEnsureSettings();

		Assert.True(reloaded.LlmSettings!.Prompts.Single(p => p.Id == 42).FastMode);
		Assert.False(reloaded.LlmSettings.Prompts.Single(p => p.Id == 43).FastMode);
	}

	[Fact]
	public void SettingsWorkingCopy_CarriesFastModeBackIntoLiveSettings()
	{
		// Prompts are copied wholesale, so this guards the copy staying wholesale.
		var live = new Settings { LlmSettings = new LlmSettings() };
		var working = new Settings { LlmSettings = new LlmSettings() };
		working.LlmSettings.Prompts.Add(new LlmSettings.LlmPrompt
		{
			Id = 1,
			Name = "Fast one",
			FastMode = true,
		});

		SettingsWorkingCopy.CommitInto(live, working);

		Assert.True(Assert.Single(live.LlmSettings!.Prompts).FastMode);
	}
}
