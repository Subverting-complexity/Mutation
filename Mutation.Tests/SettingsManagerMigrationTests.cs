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

	[Fact]
	public void UpgradeSettings_DropsLegacyUserInstructions()
	{
		string json = """
			{
				"UserInstructions": "old guidance text",
				"LlmSettings": { "Prompts": [] }
			}
			""";

		JObject result = UpgradeAndReload(json);

		Assert.Null(result["UserInstructions"]);
	}

	[Fact]
	public void UpgradeSettings_RenamesTextToSpeechHotKeyToSpeakClipboard()
	{
		string json = """
			{
				"TextToSpeechSettings": {
					"TextToSpeechHotKey": "CTRL+SHIFT+ALT+Q",
					"SpeakSelectionHotKey": "CTRL+SHIFT+Q"
				}
			}
			""";

		JObject result = UpgradeAndReload(json);
		var tts = (JObject)result["TextToSpeechSettings"]!;

		Assert.Null(tts["TextToSpeechHotKey"]);
		Assert.Equal("CTRL+SHIFT+ALT+Q", tts["SpeakClipboard"]?.ToString());
		Assert.Equal("CTRL+SHIFT+Q", tts["SpeakSelectionHotKey"]?.ToString());
	}

	[Fact]
	public void UpgradeSettings_RenamesLlmApiKeyToOpenAiApiKey()
	{
		string json = """
			{
				"LlmSettings": {
					"ApiKey": "sk-openai-xxx",
					"AnthropicApiKey": "ant-yyy",
					"Prompts": []
				}
			}
			""";

		JObject result = UpgradeAndReload(json);
		var llm = (JObject)result["LlmSettings"]!;

		Assert.Null(llm["ApiKey"]);
		Assert.Equal("sk-openai-xxx", llm["OpenAiApiKey"]?.ToString());
		Assert.Equal("ant-yyy", llm["AnthropicApiKey"]?.ToString());
	}

	[Fact]
	public void UpgradeSettings_RenamesAreIdempotent()
	{
		string json = """
			{
				"TextToSpeechSettings": { "SpeakClipboard": "CTRL+SHIFT+ALT+Q" },
				"LlmSettings": {
					"OpenAiApiKey": "sk-openai-xxx",
					"AnthropicApiKey": "ant-yyy",
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

	[Fact]
	public void UpgradeSettings_SeedsPromptsFromLegacyFormatTranscriptPrompt()
	{
		string json = """
			{
				"LlmSettings": {
					"FormatTranscriptPrompt": "Clean up this transcript.",
					"FormatWithLlmHotKey": "ALT+SHIFT+P",
					"Prompts": []
				}
			}
			""";

		JObject result = UpgradeAndReload(json);
		var llm = (JObject)result["LlmSettings"]!;

		Assert.Null(llm["FormatTranscriptPrompt"]);
		var prompts = (JArray)llm["Prompts"]!;
		Assert.Single(prompts);
		Assert.Equal("Default", prompts[0]?["Name"]?.ToString());
		Assert.Equal("Clean up this transcript.", prompts[0]?["Content"]?.ToString());
		Assert.Equal("ALT+SHIFT+P", prompts[0]?["Hotkey"]?.ToString());
		Assert.Equal(LlmSettings.DefaultModel, prompts[0]?["ModelName"]?.ToString());
		// Legacy FormatWithLlmHotKey is renamed to ProcessWithLlmHotKey by the rename migration.
		Assert.Null(llm["FormatWithLlmHotKey"]);
		Assert.Equal("ALT+SHIFT+P", llm["ProcessWithLlmHotKey"]?.ToString());
	}

	[Fact]
	public void UpgradeSettings_DropsLegacyFormatTranscriptPrompt_PreservesExistingPrompts()
	{
		string json = """
			{
				"LlmSettings": {
					"FormatTranscriptPrompt": "old text we no longer use",
					"Prompts": [
						{ "Id": 7, "Name": "User prompt", "Content": "keep me", "ModelName": "chat-latest" }
					]
				}
			}
			""";

		JObject result = UpgradeAndReload(json);
		var llm = (JObject)result["LlmSettings"]!;

		Assert.Null(llm["FormatTranscriptPrompt"]);
		var prompts = (JArray)llm["Prompts"]!;
		Assert.Single(prompts);
		Assert.Equal("User prompt", prompts[0]?["Name"]?.ToString());
		Assert.Equal("keep me", prompts[0]?["Content"]?.ToString());
	}

	[Fact]
	public void UpgradeSettings_DropsEmptyFormatTranscriptPrompt_DoesNotSeedPrompts()
	{
		string json = """
			{
				"LlmSettings": {
					"FormatTranscriptPrompt": "",
					"Prompts": []
				}
			}
			""";

		JObject result = UpgradeAndReload(json);
		var llm = (JObject)result["LlmSettings"]!;

		Assert.Null(llm["FormatTranscriptPrompt"]);
		Assert.Empty((JArray)llm["Prompts"]!);
	}

	[Fact]
	public void UpgradeSettings_RenamesFormatWithLlmHotKey()
	{
		string json = """
			{
				"LlmSettings": {
					"FormatWithLlmHotKey": "ALT+SHIFT+P",
					"Prompts": []
				}
			}
			""";

		JObject result = UpgradeAndReload(json);
		var llm = (JObject)result["LlmSettings"]!;

		Assert.Null(llm["FormatWithLlmHotKey"]);
		Assert.Equal("ALT+SHIFT+P", llm["ProcessWithLlmHotKey"]?.ToString());
	}

	[Fact]
	public void UpgradeSettings_RenamesSpeechToTextWithLlmFormattingHotKey()
	{
		string json = """
			{
				"SpeechToTextSettings": {
					"SpeechToTextWithLlmFormattingHotKey": "SHIFT+ALT+I"
				}
			}
			""";

		JObject result = UpgradeAndReload(json);
		var stt = (JObject)result["SpeechToTextSettings"]!;

		Assert.Null(stt["SpeechToTextWithLlmFormattingHotKey"]);
		Assert.Equal("SHIFT+ALT+I", stt["SpeechToTextWithLlmProcessingHotKey"]?.ToString());
	}

	[Fact]
	public void UpgradeSettings_MovesTranscriptFormatRulesToRoot()
	{
		string json = """
			{
				"LlmSettings": {
					"TranscriptFormatRules": [
						{ "Find": "newline", "ReplaceWith": "\n", "CaseSensitive": false, "MatchType": "Smart" }
					],
					"Prompts": []
				}
			}
			""";

		JObject result = UpgradeAndReload(json);

		Assert.Null(result["LlmSettings"]?["TranscriptFormatRules"]);
		var rules = (JArray)result["TranscriptFormatRules"]!;
		Assert.Single(rules);
		Assert.Equal("newline", rules[0]?["Find"]?.ToString());
	}

	[Fact]
	public void UpgradeSettings_DoesNotOverwriteExistingRootTranscriptFormatRules()
	{
		string json = """
			{
				"TranscriptFormatRules": [
					{ "Find": "keep", "ReplaceWith": "me", "CaseSensitive": false, "MatchType": "Plain" }
				],
				"LlmSettings": {
					"TranscriptFormatRules": [
						{ "Find": "drop", "ReplaceWith": "this", "CaseSensitive": false, "MatchType": "Plain" }
					],
					"Prompts": []
				}
			}
			""";

		JObject result = UpgradeAndReload(json);

		Assert.Null(result["LlmSettings"]?["TranscriptFormatRules"]);
		var rules = (JArray)result["TranscriptFormatRules"]!;
		Assert.Single(rules);
		Assert.Equal("keep", rules[0]?["Find"]?.ToString());
	}

	[Fact]
	public void UpgradeSettings_DoesNotRenameOtherApiKeyFields()
	{
		string json = """
			{
				"AzureComputerVisionSettings": { "ApiKey": "azure-key" },
				"SpeechToTextSettings": {
					"Services": [
						{ "Name": "OpenAI Whisper 1", "Provider": "OpenAi", "ApiKey": "stt-key" }
					]
				},
				"LlmSettings": { "ApiKey": "llm-key", "Prompts": [] }
			}
			""";

		JObject result = UpgradeAndReload(json);

		Assert.Equal("azure-key", result["AzureComputerVisionSettings"]?["ApiKey"]?.ToString());
		Assert.Equal("stt-key", result["SpeechToTextSettings"]?["Services"]?[0]?["ApiKey"]?.ToString());
		Assert.Null(result["LlmSettings"]?["ApiKey"]);
		Assert.Equal("llm-key", result["LlmSettings"]?["OpenAiApiKey"]?.ToString());
	}
}
