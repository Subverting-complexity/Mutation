using System;
using System.IO;
using CognitiveSupport;
using Mutation.Ui.Services;
using Newtonsoft.Json.Linq;

namespace Mutation.Tests;

public class SettingsManagerMigrationTests : IDisposable
{
	private readonly TempSettingsFile _settingsFile = new("settings");

	private string _tempPath => _settingsFile.FilePath;

	public void Dispose() => _settingsFile.Dispose();

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
		// Compare the bytes, not the mtime: Windows file times advance only on the
		// ~15.6 ms system clock tick, so a rewrite inside one tick is invisible. The
		// bytes are also a stronger check, since a rewrite would reindent the JSON.
		byte[] before = File.ReadAllBytes(_tempPath);

		var manager = new SettingsManager(_tempPath);
		manager.UpgradeSettings();

		Assert.Equal(before, File.ReadAllBytes(_tempPath));
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
	public void UpgradeSettings_RenamesLegacyLlmApiKeyAndMovesToRootApiKeys()
	{
		// Legacy LlmSettings.ApiKey is renamed to OpenAiApiKey, then both provider keys
		// are moved out of LlmSettings into the central root ApiKeys section.
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
		Assert.Null(llm["OpenAiApiKey"]);
		Assert.Null(llm["AnthropicApiKey"]);

		var apiKeys = (JObject)result["ApiKeys"]!;
		Assert.Equal("sk-openai-xxx", apiKeys["OpenAiApiKey"]?.ToString());
		Assert.Equal("ant-yyy", apiKeys["AnthropicApiKey"]?.ToString());
	}

	[Fact]
	public void UpgradeSettings_DoesNotOverwriteExistingRootApiKeys()
	{
		// A key already present under root ApiKeys wins; the stale LlmSettings copy is
		// dropped rather than clobbering the central one.
		string json = """
			{
				"ApiKeys": { "OpenAiApiKey": "root-key" },
				"LlmSettings": {
					"OpenAiApiKey": "stale-llm-key",
					"AnthropicApiKey": "ant-yyy",
					"Prompts": []
				}
			}
			""";

		JObject result = UpgradeAndReload(json);
		var llm = (JObject)result["LlmSettings"]!;

		Assert.Null(llm["OpenAiApiKey"]);
		Assert.Null(llm["AnthropicApiKey"]);

		var apiKeys = (JObject)result["ApiKeys"]!;
		Assert.Equal("root-key", apiKeys["OpenAiApiKey"]?.ToString());
		Assert.Equal("ant-yyy", apiKeys["AnthropicApiKey"]?.ToString());
	}

	[Fact]
	public void UpgradeSettings_RenamesAreIdempotent()
	{
		string json = """
			{
				"TextToSpeechSettings": { "SpeakClipboard": "CTRL+SHIFT+ALT+Q" },
				"ApiKeys": {
					"OpenAiApiKey": "sk-openai-xxx",
					"AnthropicApiKey": "ant-yyy"
				},
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
		// Legacy FormatWithLlmHotKey is renamed to ProcessWithLlmHotKey, used to seed the
		// prompt's Hotkey above, then dropped as obsolete. Both keys end up removed.
		Assert.Null(llm["FormatWithLlmHotKey"]);
		Assert.Null(llm["ProcessWithLlmHotKey"]);
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
	public void UpgradeSettings_ConvertsLegacyStringModelsToLlmModelConfig()
	{
		string json = """
			{
				"LlmSettings": {
					"Models": ["gpt-4.1", "claude-sonnet-4-6", "chat-latest"]
				}
			}
			""";

		JObject result = UpgradeAndReload(json);
		var models = (JArray)result["LlmSettings"]!["Models"]!;

		Assert.Equal(3, models.Count);
		Assert.All(models, m => Assert.Equal(JTokenType.Object, m.Type));

		Assert.Equal("gpt-4.1", models[0]?["Name"]?.ToString());
		Assert.Equal("OpenAI", models[0]?["Provider"]?.ToString());
		Assert.Equal(JTokenType.Null, models[0]?["CustomTemperature"]?.Type);

		Assert.Equal("claude-sonnet-4-6", models[1]?["Name"]?.ToString());
		Assert.Equal("Anthropic", models[1]?["Provider"]?.ToString());

		Assert.Equal("chat-latest", models[2]?["Name"]?.ToString());
		Assert.Equal("OpenAI", models[2]?["Provider"]?.ToString());
	}

	[Fact]
	public void UpgradeSettings_LeavesAlreadyMigratedModelsUntouched()
	{
		string json = """
			{
				"LlmSettings": {
					"Models": [
						{ "Name": "gpt-4.1", "Provider": "OpenAI", "CustomTemperature": 0.7 }
					]
				}
			}
			""";
		File.WriteAllText(_tempPath, json);
		// Byte comparison rather than mtime — see the note in
		// UpgradeSettings_AlreadyMigratedFile_IsIdempotent.
		byte[] before = File.ReadAllBytes(_tempPath);

		var manager = new SettingsManager(_tempPath);
		manager.UpgradeSettings();

		Assert.Equal(before, File.ReadAllBytes(_tempPath));
	}

	[Fact]
	public void UpgradeSettings_DropsLegacyFormatAndProcessWithLlmHotKeys()
	{
		// FormatWithLlmHotKey is renamed to ProcessWithLlmHotKey, which is now itself
		// obsolete (per-prompt hotkeys replaced it) and removed in the same pass.
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
		Assert.Null(llm["ProcessWithLlmHotKey"]);
	}

	[Fact]
	public void UpgradeSettings_DropsProcessWithLlmHotKey_PreservesExistingPrompts()
	{
		// An already-migrated file: ProcessWithLlmHotKey lingers but per-prompt hotkeys
		// drive everything. The leftover key is removed; the prompt is left untouched.
		string json = """
			{
				"LlmSettings": {
					"ProcessWithLlmHotKey": "CTRL+SHIFT+F",
					"Prompts": [
						{ "Id": 1, "Name": "Mine", "Content": "do it", "Hotkey": "CTRL+ALT+G", "ModelName": "chat-latest" }
					]
				}
			}
			""";

		JObject result = UpgradeAndReload(json);
		var llm = (JObject)result["LlmSettings"]!;

		Assert.Null(llm["ProcessWithLlmHotKey"]);
		var prompts = (JArray)llm["Prompts"]!;
		Assert.Single(prompts);
		Assert.Equal("Mine", prompts[0]?["Name"]?.ToString());
		Assert.Equal("CTRL+ALT+G", prompts[0]?["Hotkey"]?.ToString());
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
		Assert.Null(result["LlmSettings"]?["OpenAiApiKey"]);
		Assert.Equal("llm-key", result["ApiKeys"]?["OpenAiApiKey"]?.ToString());
	}

	// Chain test: a JSON containing every legacy key still recognised by UpgradeSettings.
	// Verifies that all migrations run in one pass without interfering with each other,
	// and (critically) that ordering produces correct A->B->C chains where one block reads
	// a key produced by an earlier rename in the same pass — e.g. the FormatTranscriptPrompt
	// seed pulls its Hotkey from ProcessWithLlmHotKey, which only exists because the
	// FormatWithLlmHotKey -> ProcessWithLlmHotKey rename ran first. If a future migration is
	// inserted out of order and breaks such a chain, this test should catch it.
	[Fact]
	public void UpgradeSettings_FullLegacyJson_MigratesEverythingInOnePass()
	{
		string json = """
			{
				"UserInstructions": "old guidance",
				"SpeetchToTextSettings": {
					"Service": "OpenAiWhisper",
					"ApiKey": "stt-key",
					"BaseDomain": "https://api.example.com/",
					"ModelId": "whisper-1",
					"SpeechToTextPrompt": "hello",
					"ActiveSpeetchToTextService": "OpenAiWhisper",
					"SendKotKeyAfterTranscriptionOperation": "CTRL+ALT+V",
					"SpeechToTextWithLlmFormattingHotKey": "SHIFT+ALT+I"
				},
				"AzureComputerVisionSettings": {
					"SendKotKeyAfterOcrOperation": "CTRL+ALT+C"
				},
				"LlmSettings": {
					"SelectedLlmModel": "gpt-4.1",
					"ApiKey": "openai-key",
					"Models": ["gpt-4.1", "claude-sonnet-4-6"],
					"FormatWithLlmHotKey": "ALT+SHIFT+P",
					"FormatTranscriptPrompt": "Clean this up.",
					"TranscriptFormatRules": [
						{ "Find": "um", "ReplaceWith": "", "CaseSensitive": false, "MatchType": "Smart" }
					],
					"Prompts": []
				},
				"TextToSpeechSettings": {
					"TextToSpeechHotKey": "CTRL+SHIFT+ALT+Q"
				}
			}
			""";

		JObject result = UpgradeAndReload(json);

		// Root: UserInstructions dropped.
		Assert.Null(result["UserInstructions"]);

		// SpeetchToTextSettings -> SpeechToTextSettings (typo fix).
		Assert.Null(result["SpeetchToTextSettings"]);
		var stt = (JObject)result["SpeechToTextSettings"]!;

		// Loose Service/ApiKey/BaseDomain/ModelId/SpeechToTextPrompt fields collapsed into Services[0].
		Assert.Null(stt["Service"]);
		Assert.Null(stt["ApiKey"]);
		Assert.Null(stt["BaseDomain"]);
		Assert.Null(stt["ModelId"]);
		Assert.Null(stt["SpeechToTextPrompt"]);
		var services = (JArray)stt["Services"]!;
		Assert.Single(services);
		Assert.Equal("OpenAi", services[0]?["Provider"]?.ToString()); // OpenAiWhisper -> OpenAi enum rename
		Assert.Equal("stt-key", services[0]?["ApiKey"]?.ToString());
		Assert.Equal("whisper-1", services[0]?["ModelId"]?.ToString());

		// ActiveSpeetchToTextService -> ActiveSpeechToTextService.
		Assert.Null(stt["ActiveSpeetchToTextService"]);
		Assert.Equal("OpenAiWhisper", stt["ActiveSpeechToTextService"]?.ToString());

		// SendKotKey... -> SendHotkey... (transcription).
		Assert.Null(stt["SendKotKeyAfterTranscriptionOperation"]);
		Assert.Equal("CTRL+ALT+V", stt["SendHotkeyAfterTranscriptionOperation"]?.ToString());

		// SpeechToTextWithLlmFormattingHotKey -> SpeechToTextWithLlmProcessingHotKey.
		Assert.Null(stt["SpeechToTextWithLlmFormattingHotKey"]);
		Assert.Equal("SHIFT+ALT+I", stt["SpeechToTextWithLlmProcessingHotKey"]?.ToString());

		// Vision: SendKotKey... -> SendHotkey... (ocr).
		var vision = (JObject)result["AzureComputerVisionSettings"]!;
		Assert.Null(vision["SendKotKeyAfterOcrOperation"]);
		Assert.Equal("CTRL+ALT+C", vision["SendHotkeyAfterOcrOperation"]?.ToString());

		// LlmSettings: SelectedLlmModel dropped, ApiKey -> OpenAiApiKey -> moved to root ApiKeys,
		// FormatWithLlmHotKey -> ProcessWithLlmHotKey -> dropped as obsolete.
		var llm = (JObject)result["LlmSettings"]!;
		Assert.Null(llm["SelectedLlmModel"]);
		Assert.Null(llm["ApiKey"]);
		Assert.Null(llm["OpenAiApiKey"]);
		Assert.Equal("openai-key", result["ApiKeys"]?["OpenAiApiKey"]?.ToString());
		Assert.Null(llm["FormatWithLlmHotKey"]);
		Assert.Null(llm["ProcessWithLlmHotKey"]);

		// Models: List<string> -> List<LlmModelConfig>, provider inferred from name.
		var models = (JArray)llm["Models"]!;
		Assert.Equal(2, models.Count);
		Assert.Equal("gpt-4.1", models[0]?["Name"]?.ToString());
		Assert.Equal("OpenAI", models[0]?["Provider"]?.ToString());
		Assert.Equal("claude-sonnet-4-6", models[1]?["Name"]?.ToString());
		Assert.Equal("Anthropic", models[1]?["Provider"]?.ToString());

		// TranscriptFormatRules moved out of LlmSettings to root.
		Assert.Null(llm["TranscriptFormatRules"]);
		var rules = (JArray)result["TranscriptFormatRules"]!;
		Assert.Single(rules);
		Assert.Equal("um", rules[0]?["Find"]?.ToString());

		// FormatTranscriptPrompt dropped + Prompts[0] seeded from it.
		// CHAIN CHECK: the seeded prompt's Hotkey reads from ProcessWithLlmHotKey, which
		// only exists because the rename block above ran first in the same pass.
		Assert.Null(llm["FormatTranscriptPrompt"]);
		var prompts = (JArray)llm["Prompts"]!;
		Assert.Single(prompts);
		Assert.Equal("Default", prompts[0]?["Name"]?.ToString());
		Assert.Equal("Clean this up.", prompts[0]?["Content"]?.ToString());
		Assert.Equal("ALT+SHIFT+P", prompts[0]?["Hotkey"]?.ToString());
		Assert.Equal(LlmSettings.DefaultModel, prompts[0]?["ModelName"]?.ToString());

		// TextToSpeechHotKey -> SpeakClipboard.
		var tts = (JObject)result["TextToSpeechSettings"]!;
		Assert.Null(tts["TextToSpeechHotKey"]);
		Assert.Equal("CTRL+SHIFT+ALT+Q", tts["SpeakClipboard"]?.ToString());
	}
}
