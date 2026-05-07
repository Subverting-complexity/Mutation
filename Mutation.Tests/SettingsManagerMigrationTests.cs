using System;
using System.IO;
using CognitiveSupport;
using Mutation.Ui.Services;
using Newtonsoft.Json.Linq;

namespace Mutation.Tests;

public class SettingsManagerMigrationTests : IDisposable
{
	private readonly string _tempPath;

	public SettingsManagerMigrationTests()
	{
		_tempPath = Path.Combine(Path.GetTempPath(), $"mutation-settings-{Guid.NewGuid():N}.json");
	}

	public void Dispose()
	{
		if (File.Exists(_tempPath))
			File.Delete(_tempPath);
	}

	private JObject UpgradeAndReload(string json)
	{
		File.WriteAllText(_tempPath, json);
		var manager = new SettingsManager(_tempPath);
		manager.UpgradeSettings();
		return JObject.Parse(File.ReadAllText(_tempPath));
	}

	[Fact]
	public void UpgradeSettings_RemovesLegacySelectedLlmModel()
	{
		string json = """
			{
				"LlmSettings": {
					"SelectedLlmModel": "gpt-4.1",
					"Prompts": [
						{ "Id": 1, "Name": "P1", "Content": "x", "ModelName": "claude-sonnet-4-6" }
					]
				}
			}
			""";

		JObject result = UpgradeAndReload(json);

		Assert.Null(result["LlmSettings"]?["SelectedLlmModel"]);
	}

	[Fact]
	public void UpgradeSettings_BackfillsMissingModelNameWithDefault()
	{
		string json = """
			{
				"LlmSettings": {
					"SelectedLlmModel": "gpt-4.1",
					"Prompts": [
						{ "Id": 1, "Name": "Translate", "Content": "translate" },
						{ "Id": 2, "Name": "Summarize", "Content": "summarize", "ModelName": null },
						{ "Id": 3, "Name": "Empty model", "Content": "x", "ModelName": "" }
					]
				}
			}
			""";

		JObject result = UpgradeAndReload(json);
		var prompts = (JArray)result["LlmSettings"]!["Prompts"]!;

		foreach (var prompt in prompts)
		{
			Assert.Equal(LlmSettings.DefaultModel, prompt["ModelName"]?.ToString());
		}
	}

	[Fact]
	public void UpgradeSettings_PreservesExistingPerPromptModelName()
	{
		string json = """
			{
				"LlmSettings": {
					"Prompts": [
						{ "Id": 1, "Name": "Keep", "Content": "x", "ModelName": "claude-sonnet-4-6" }
					]
				}
			}
			""";

		JObject result = UpgradeAndReload(json);

		Assert.Equal("claude-sonnet-4-6", result["LlmSettings"]!["Prompts"]![0]!["ModelName"]?.ToString());
	}

	[Fact]
	public void UpgradeSettings_AlreadyMigratedFile_IsIdempotent()
	{
		string json = """
			{
				"LlmSettings": {
					"Prompts": [
						{ "Id": 1, "Name": "P1", "Content": "x", "ModelName": "chat-latest" }
					]
				}
			}
			""";
		File.WriteAllText(_tempPath, json);
		var beforeMtime = File.GetLastWriteTimeUtc(_tempPath);

		var manager = new SettingsManager(_tempPath);
		manager.UpgradeSettings();

		var afterMtime = File.GetLastWriteTimeUtc(_tempPath);
		Assert.Equal(beforeMtime, afterMtime);
	}
}
