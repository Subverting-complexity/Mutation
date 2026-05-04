using CognitiveSupport;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;

namespace Mutation.Ui.Services;

internal class SettingsManager : ISettingsManager
{
	private static readonly JsonSerializerSettings _jsonSerializerSettings = new JsonSerializerSettings
	{
		Converters = new List<JsonConverter> { new StringEnumConverter() }
	};

	private string SettingsFilePath { get; set; }
	private string SettingsFileFullPath => Path.GetFullPath(SettingsFilePath);

	public SettingsManager(
		string settingsFilePath)
	{
		SettingsFilePath = settingsFilePath;
	}

	private bool CreateSettingsFileIfNotExists(string fullPath)
	{
		if (!File.Exists(fullPath))
		{
			var settings = new Settings();
			// Brand new file: allow defaults (including sample router mapping)
			EnsureSettings(settings, isNewFile: true);
			SaveSettingsToFile(settings);
			try { Process.Start("notepad.exe", SettingsFilePath); } catch { }
			return true;
		}
		return false;
	}

	private bool EnsureSettings(Settings settings, bool isNewFile)
	{
		const string PlaceholderValue = "<placeholder>";
		const string PlaceholderUrl = "https://placeholder.com";

		bool somethingWasMissing = false;

                if (string.IsNullOrWhiteSpace(settings.UserInstructions))
                {
                        settings.UserInstructions = "Change the values of the settings below to your preferences, save the file, and restart Mutation.exe. DeploymentId in the LlmSettings should be set to your Azure model Deployment Name.";
                        somethingWasMissing = true;
                }

                if (settings.MainWindowUiSettings is null)
                {
                        settings.MainWindowUiSettings = new MainWindowUiSettings();
                        somethingWasMissing = true;
                }

		if (settings.MainWindowUiSettings.MaxTextBoxLineCount <= 0)
		{
			settings.MainWindowUiSettings.MaxTextBoxLineCount = 5;
			somethingWasMissing = true;
		}

		if (string.IsNullOrWhiteSpace(settings.MainWindowUiSettings.DictationInsertPreference))
		{
			settings.MainWindowUiSettings.DictationInsertPreference = "Paste";
			somethingWasMissing = true;
		}

		if (settings.AzureComputerVisionSettings is null)
                {
                        settings.AzureComputerVisionSettings = new AzureComputerVisionSettings();
                        somethingWasMissing = true;
                }
		var azureComputerVisionSettings = settings.AzureComputerVisionSettings;

		if (azureComputerVisionSettings.TimeoutSeconds <= 0)
		{
			azureComputerVisionSettings.TimeoutSeconds = 10;
		}

		if (string.IsNullOrWhiteSpace(azureComputerVisionSettings.ScreenshotHotKey))
		{
			azureComputerVisionSettings.ScreenshotHotKey = "SHIFT+ALT+K";
			somethingWasMissing = true;
		}

		if (string.IsNullOrWhiteSpace(azureComputerVisionSettings.OcrHotKey))
		{
			azureComputerVisionSettings.OcrHotKey = "ALT+J";
			somethingWasMissing = true;
		}
		if (string.IsNullOrWhiteSpace(azureComputerVisionSettings.ScreenshotOcrHotKey))
		{
			azureComputerVisionSettings.ScreenshotOcrHotKey = "SHIFT+ALT+J";
			somethingWasMissing = true;
		}

		if (string.IsNullOrWhiteSpace(azureComputerVisionSettings.OcrLeftToRightTopToBottomHotKey))
		{
			azureComputerVisionSettings.OcrLeftToRightTopToBottomHotKey = "ALT+K";
			somethingWasMissing = true;
		}
		if (string.IsNullOrWhiteSpace(azureComputerVisionSettings.ScreenshotLeftToRightTopToBottomOcrHotKey))
		{
			azureComputerVisionSettings.ScreenshotLeftToRightTopToBottomOcrHotKey = "SHIFT+ALT+E";
			somethingWasMissing = true;
		}

		if (string.IsNullOrWhiteSpace(azureComputerVisionSettings.ApiKey))
		{
			azureComputerVisionSettings.ApiKey = PlaceholderValue;
			somethingWasMissing = true;
		}
		if (string.IsNullOrWhiteSpace(azureComputerVisionSettings.Endpoint))
		{
			azureComputerVisionSettings.Endpoint = PlaceholderUrl;
			somethingWasMissing = true;
		}

                if (azureComputerVisionSettings.FreeTierPageLimit <= 0)
                {
                        azureComputerVisionSettings.FreeTierPageLimit = 2;
                        somethingWasMissing = true;
                }

                if (azureComputerVisionSettings.MaxParallelDocuments <= 0)
                {
                        azureComputerVisionSettings.MaxParallelDocuments = 2;
                        somethingWasMissing = true;
                }

                if (azureComputerVisionSettings.MaxParallelRequests <= 0)
                {
                        azureComputerVisionSettings.MaxParallelRequests = 4;
                        somethingWasMissing = true;
                }

                if (azureComputerVisionSettings.MaxParallelRequests > 20)
                {
                        azureComputerVisionSettings.MaxParallelRequests = 20;
                        somethingWasMissing = true;
                }

                if (azureComputerVisionSettings.FreeTierPageLimit > 20)
                {
                        azureComputerVisionSettings.FreeTierPageLimit = 20;
                        somethingWasMissing = true;
                }


		if (settings.AudioSettings is null)
		{
			settings.AudioSettings = new AudioSettings();
			somethingWasMissing = true;
		}
		var audioSettings = settings.AudioSettings;
		if (string.IsNullOrWhiteSpace(audioSettings.MicrophoneToggleMuteHotKey))
		{
			audioSettings.MicrophoneToggleMuteHotKey = "ALT+Q";
			somethingWasMissing = true;
		}
		if (audioSettings.CustomBeepSettings == null)
		{
			audioSettings.CustomBeepSettings = new AudioSettings.CustomBeepSettingsData();
			somethingWasMissing = true;
		}
		if (audioSettings.CustomBeepSettings.UseCustomBeeps)
		{
			string[] allowedExtensions = new[] { ".wav" };
			var beepIssues = new List<string>();

			string successPath = audioSettings.CustomBeepSettings?.BeepSuccessFile ?? string.Empty;
			string resolvedSuccessPath = audioSettings.CustomBeepSettings.ResolveAudioFilePath(successPath);
			if (string.IsNullOrWhiteSpace(successPath) ||
				!allowedExtensions.Contains(Path.GetExtension(successPath)?.ToLower()) ||
				!File.Exists(resolvedSuccessPath))
			{
				somethingWasMissing = true;

				if (!string.IsNullOrWhiteSpace(successPath) && Path.GetExtension(successPath)?.ToLower() != ".wav")
					beepIssues.Add($"Custom success beep must be a .wav file: {successPath}");
				else
					beepIssues.Add($"Could not load success beep file: {successPath}");
			}

			string failurePath = audioSettings.CustomBeepSettings?.BeepFailureFile ?? string.Empty;
			string resolvedFailurePath = audioSettings.CustomBeepSettings.ResolveAudioFilePath(failurePath);
			if (string.IsNullOrWhiteSpace(failurePath) ||
				!allowedExtensions.Contains(Path.GetExtension(failurePath)?.ToLower()) ||
				!File.Exists(resolvedFailurePath))
			{
				somethingWasMissing = true;

				if (!string.IsNullOrWhiteSpace(failurePath) && Path.GetExtension(failurePath)?.ToLower() != ".wav")
					beepIssues.Add($"Custom failure beep must be a .wav file: {failurePath}");
				else
					beepIssues.Add($"Could not load failure beep file: {failurePath}");
			}

			string startPath = audioSettings.CustomBeepSettings?.BeepStartFile ?? string.Empty;
			string resolvedStartPath = audioSettings.CustomBeepSettings.ResolveAudioFilePath(startPath);
			if (string.IsNullOrWhiteSpace(startPath) ||
				!allowedExtensions.Contains(Path.GetExtension(startPath)?.ToLower()) ||
				!File.Exists(resolvedStartPath))
			{
				somethingWasMissing = true;

				if (!string.IsNullOrWhiteSpace(startPath) && Path.GetExtension(startPath)?.ToLower() != ".wav")
					beepIssues.Add($"Custom start beep must be a .wav file: {startPath}");
				else
					beepIssues.Add($"Could not load start beep file: {startPath}");
			}

			string endPath = audioSettings.CustomBeepSettings?.BeepEndFile ?? string.Empty;
			string resolvedEndPath = audioSettings.CustomBeepSettings.ResolveAudioFilePath(endPath);
			if (string.IsNullOrWhiteSpace(endPath) ||
				!allowedExtensions.Contains(Path.GetExtension(endPath)?.ToLower()) ||
				!File.Exists(resolvedEndPath))
			{
				somethingWasMissing = true;

				if (!string.IsNullOrWhiteSpace(endPath) && Path.GetExtension(endPath)?.ToLower() != ".wav")
					beepIssues.Add($"Custom end beep must be a .wav file: {endPath}");
				else
					beepIssues.Add($"Could not load end beep file: {endPath}");
			}

			string mutePath = audioSettings.CustomBeepSettings?.BeepMuteFile ?? string.Empty;
			string resolvedMutePath = audioSettings.CustomBeepSettings.ResolveAudioFilePath(mutePath);
			if (string.IsNullOrWhiteSpace(mutePath) ||
				!allowedExtensions.Contains(Path.GetExtension(mutePath)?.ToLower()) ||
				!File.Exists(resolvedMutePath))
			{
				somethingWasMissing = true;

				if (!string.IsNullOrWhiteSpace(mutePath) && Path.GetExtension(mutePath)?.ToLower() != ".wav")
					beepIssues.Add($"Custom mute beep must be a .wav file: {mutePath}");
				else
					beepIssues.Add($"Could not load mute beep file: {mutePath}");
			}

			string unmutePath = audioSettings.CustomBeepSettings?.BeepUnmuteFile ?? string.Empty;
			string resolvedUnmutePath = audioSettings.CustomBeepSettings.ResolveAudioFilePath(unmutePath);
			if (string.IsNullOrWhiteSpace(unmutePath) ||
				!allowedExtensions.Contains(Path.GetExtension(unmutePath)?.ToLower()) ||
				!File.Exists(resolvedUnmutePath))
			{
				somethingWasMissing = true;

				if (!string.IsNullOrWhiteSpace(unmutePath) && Path.GetExtension(unmutePath)?.ToLower() != ".wav")
					beepIssues.Add($"Custom unmute beep must be a .wav file: {unmutePath}");
				else
					beepIssues.Add($"Could not load unmute beep file: {unmutePath}");
			}

			if (beepIssues.Any())
			{
				if (audioSettings.CustomBeepSettings != null)
					audioSettings.CustomBeepSettings.UseCustomBeeps = false;

				string message =
					"The following issues were found with the custom beep settings:" + Environment.NewLine + Environment.NewLine +
					string.Join(Environment.NewLine, beepIssues) + Environment.NewLine + Environment.NewLine +
					"Falling back to default beep sounds. UseCustomBeeps has been disabled." + Environment.NewLine + Environment.NewLine +
					"To use custom beeps again, fix the issues above and re-enable UseCustomBeeps in the settings file.";

				System.Windows.Forms.MessageBox.Show(
					message,
					"Custom Beep Settings Issues",
					System.Windows.Forms.MessageBoxButtons.OK,
					System.Windows.Forms.MessageBoxIcon.Warning
				);
			}
		}


		if (settings.SpeechToTextSettings is null)
		{
			settings.SpeechToTextSettings = new SpeechToTextSettings();
			somethingWasMissing = true;
		}
		var speechToTextSettings = settings.SpeechToTextSettings;
		if (string.IsNullOrWhiteSpace(speechToTextSettings.SpeechToTextHotKey))
		{
			speechToTextSettings.SpeechToTextHotKey = "SHIFT+ALT+U";
			somethingWasMissing = true;
		}
		if (speechToTextSettings.Services is null)
		{
			speechToTextSettings.Services = Array.Empty<SpeechToTextServiceSettings>();
			somethingWasMissing = true;
		}
		if (!speechToTextSettings.Services.Any())
		{
			speechToTextSettings.ActiveSpeechToTextService = "OpenAI Whisper 1";
			SpeechToTextServiceSettings service = new SpeechToTextServiceSettings
			{
				Name = speechToTextSettings.ActiveSpeechToTextService,
				Provider = SpeechToTextProviders.OpenAi,
				ModelId = "whisper-1",
				BaseDomain = "https://api.openai.com/",
			};
			speechToTextSettings.Services = speechToTextSettings.Services.Append(service).ToArray();
			somethingWasMissing = true;
		}
		foreach (var s in speechToTextSettings.Services)
		{
			if (s.Provider == SpeechToTextProviders.None)
				s.Provider = SpeechToTextProviders.OpenAi;
			if (string.IsNullOrWhiteSpace(s.ApiKey))
			{
				s.ApiKey = PlaceholderValue;
				somethingWasMissing = true;
			}
			if (string.IsNullOrWhiteSpace(s.SpeechToTextPrompt))
			{
				s.SpeechToTextPrompt = "Hello, let's use punctuation. Names: Kobus, Piro.";
			}
			if (s.TimeoutSeconds <= 0)
			{
				s.TimeoutSeconds = 10;
			}
		}

		if (string.IsNullOrWhiteSpace(speechToTextSettings.SpeechToTextWithLlmFormattingHotKey))
		{
			speechToTextSettings.SpeechToTextWithLlmFormattingHotKey = "SHIFT+ALT+I";
			somethingWasMissing = true;
		}
		if (string.IsNullOrWhiteSpace(speechToTextSettings.TempDirectory))
		{
			speechToTextSettings.TempDirectory = @"C:\Temp\Mutation";
			somethingWasMissing = true;
		}

		var duplicateGroups = speechToTextSettings.Services
			 .GroupBy(s => s.Name, StringComparer.OrdinalIgnoreCase)
			 .Where(g => g.Count() > 1)
			 .ToArray();
		if (duplicateGroups.Length > 0)
		{
			foreach (var group in duplicateGroups)
			{
				int suffix = 2;
				foreach (var dup in group.Skip(1))
				{
					string baseName = string.IsNullOrWhiteSpace(dup.Name) ? "Service" : dup.Name!;
					string newName;
					do { newName = $"{baseName} ({suffix++})"; }
					while (speechToTextSettings.Services.Any(s => string.Equals(s.Name, newName, StringComparison.OrdinalIgnoreCase)));
					dup.Name = newName;
				}
			}
			somethingWasMissing = true;
		}


		if (settings.LlmSettings is null)
		{
			settings.LlmSettings = new LlmSettings();
			somethingWasMissing = true;
		}
		var llmSettings = settings.LlmSettings;
		if (string.IsNullOrWhiteSpace(llmSettings.ApiKey))
		{
			llmSettings.ApiKey = PlaceholderValue;
			somethingWasMissing = true;
		}
		if (string.IsNullOrWhiteSpace(llmSettings.AnthropicApiKey))
		{
			llmSettings.AnthropicApiKey = PlaceholderValue;
			somethingWasMissing = true;
		}

		/* ResourceName removed
		if (string.IsNullOrWhiteSpace(llmSettings.ResourceName))
		{
			llmSettings.ResourceName = "The-Azure-resource-name-for-your-OpenAI-service";
			somethingWasMissing = true;
		}
		*/

		if (llmSettings.Prompts == null)
		{
			llmSettings.Prompts = new List<LlmSettings.LlmPrompt>();
			somethingWasMissing = true;
		}

		if (!llmSettings.Prompts.Any())
		{
             string legacyPrompt = llmSettings.FormatTranscriptPrompt;
             string legacyHotkey = llmSettings.FormatWithLlmHotKey;
             
             if (string.IsNullOrWhiteSpace(legacyPrompt))
             {
                 legacyPrompt = @"You are a helpful proofreader and editor. When you are asked to format a transcript, apply the following rules to improve the formatting of the text:
Replace the words 'new line' (case insensitive) with an actual new line character, and replace the words 'new paragraph' (case insensitive) with 2 new line characters, and replace the words 'new bullet' (case insensitive) with a newline character and a bullet character, eg. '- ', and end the preceding sentence with a full stop '.', and start the new sentence with a capital letter, and do not make any other changes.

Here is an example of a raw transcript and the reformatted text:

----- Transcript:
The radiology report - the written analysis by the radiologist interpreting your imaging study - is transmitted to the requesting physician or medical specialist new line the doctor or specialist will then relay the full analysis to you, along with recommendations and/or prescriptions. New paragraph Depending on the results, this might include new bullet scheduling further diagnostic tests new bullet initiating a new medication regimen new bullet recommending physical therapy new bullet or possibly even planning for a surgical intervention. New paragraph. Collaboration among various healthcare professionals ensures that the information gleaned from the radiology report is utilized to provide the most effective and individualized care tailored to your specific condition and needs. New line end of summary.


----- Reformatted Text:
The radiology report - the written analysis by the radiologist interpreting your imaging study - is transmitted to the requesting physician or medical specialist.
The doctor or specialist will then relay the full analysis to you, along with recommendations and/or prescriptions.

Depending on the results, this might include:
- scheduling further diagnostic tests,
- initiating a new medication regimen,
- recommending physical therapy,
- or possibly even planning for a surgical intervention.

Collaboration among various healthcare professionals ensures that the information gleaned from the radiology report is utilized to provide the most effective and individualized care tailored to your specific condition and needs.
End of summary.
";
             }
             
             if (string.IsNullOrWhiteSpace(legacyHotkey))
             {
                 legacyHotkey = "ALT+SHIFT+P";
             }
             
             llmSettings.Prompts.Add(new LlmSettings.LlmPrompt {
                Id = 1,
                Name = "Default",
                Content = legacyPrompt,
                Hotkey = legacyHotkey,
                AutoRun = false
             });
			 somethingWasMissing = true;
		}

		if (llmSettings.Models == null || !llmSettings.Models.Any())
		{
			llmSettings.Models = new List<string>
			{
				LlmSettings.DefaultModel,
				LlmSettings.DefaultSecondaryModel,
				LlmSettings.DefaultAnthropicModel
			}
			.Distinct(StringComparer.OrdinalIgnoreCase)
			.ToList();
			somethingWasMissing = true;
		}

		if (llmSettings.TranscriptFormatRules == null || !llmSettings.TranscriptFormatRules.Any())
		{
			llmSettings.TranscriptFormatRules = new List<LlmSettings.TranscriptFormatRule>
			{
				new LlmSettings.TranscriptFormatRule
				{
					Find= "new line",
					ReplaceWith= $"{Environment.NewLine}",
					CaseSensitive = false,
					MatchType = LlmSettings.TranscriptFormatRule.MatchTypeEnum.Smart,
				},
				new LlmSettings.TranscriptFormatRule
				{
					Find= "newline",
					ReplaceWith= $"{Environment.NewLine}",
					CaseSensitive = false,
					MatchType = LlmSettings.TranscriptFormatRule.MatchTypeEnum.Smart,
				},
				new LlmSettings.TranscriptFormatRule
				{
					Find= "next line",
					ReplaceWith= $"{Environment.NewLine}",
					CaseSensitive = false,
					MatchType = LlmSettings.TranscriptFormatRule.MatchTypeEnum.Smart,
				},
				new LlmSettings.TranscriptFormatRule
				{
					Find= "new paragraph",
					ReplaceWith= $"{Environment.NewLine}{Environment.NewLine}",
					CaseSensitive = false,
					MatchType = LlmSettings.TranscriptFormatRule.MatchTypeEnum.Smart,
				},
				new LlmSettings.TranscriptFormatRule
				{
					Find= "new paragraphs",
					ReplaceWith= $"{Environment.NewLine}{Environment.NewLine}",
					CaseSensitive = false,
					MatchType = LlmSettings.TranscriptFormatRule.MatchTypeEnum.Smart,
				},
				new LlmSettings.TranscriptFormatRule
				{
					Find= "next paragraph",
					ReplaceWith= $"{Environment.NewLine}{Environment.NewLine}",
					CaseSensitive = false,
					MatchType = LlmSettings.TranscriptFormatRule.MatchTypeEnum.Smart,
				},
				new LlmSettings.TranscriptFormatRule
				{
					Find= "new bullet",
					ReplaceWith= $"{Environment.NewLine}- ",
					CaseSensitive = false,
					MatchType = LlmSettings.TranscriptFormatRule.MatchTypeEnum.Smart,
				},
				new LlmSettings.TranscriptFormatRule
				{
					Find= "next bullet",
					ReplaceWith= $"{Environment.NewLine}- ",
					CaseSensitive = false,
					MatchType = LlmSettings.TranscriptFormatRule.MatchTypeEnum.Smart,
				},
				new LlmSettings.TranscriptFormatRule
				{
					Find= "new colon",
					ReplaceWith= $": ",
					CaseSensitive = false,
					MatchType = LlmSettings.TranscriptFormatRule.MatchTypeEnum.Smart,
				},
				new LlmSettings.TranscriptFormatRule
				{
					Find= "semicolon",
					ReplaceWith= $"; ",
					CaseSensitive = false,
					MatchType = LlmSettings.TranscriptFormatRule.MatchTypeEnum.Smart,
				},
				new LlmSettings.TranscriptFormatRule
				{
					Find= "full stop",
					ReplaceWith= $". ",
					CaseSensitive = false,
					MatchType = LlmSettings.TranscriptFormatRule.MatchTypeEnum.Smart,
				},
				new LlmSettings.TranscriptFormatRule
				{
					Find= "comma",
					ReplaceWith= $", ",
					CaseSensitive = false,
					MatchType = LlmSettings.TranscriptFormatRule.MatchTypeEnum.Smart,
				},
				new LlmSettings.TranscriptFormatRule
				{
					Find= "exclamation mark",
					ReplaceWith= $"! ",
					CaseSensitive = false,
					MatchType = LlmSettings.TranscriptFormatRule.MatchTypeEnum.Smart,
				},
				new LlmSettings.TranscriptFormatRule
				{
					Find= "question mark",
					ReplaceWith= $"? ",
					CaseSensitive = false,
					MatchType = LlmSettings.TranscriptFormatRule.MatchTypeEnum.Smart,
				},
				new LlmSettings.TranscriptFormatRule
				{
					Find= "ellipsis",
					ReplaceWith= $"... ",
					CaseSensitive = false,
					MatchType = LlmSettings.TranscriptFormatRule.MatchTypeEnum.Smart,
				},
				new LlmSettings.TranscriptFormatRule
				{
					Find= "dot dot dot",
					ReplaceWith= $"... ",
					CaseSensitive = false,
					MatchType = LlmSettings.TranscriptFormatRule.MatchTypeEnum.Smart,
				},


			};

		}

		if (settings.TextToSpeechSettings is null)
		{
			settings.TextToSpeechSettings = new TextToSpeechSettings();
			somethingWasMissing = true;
		}
		var textToSpeechSettings = settings.TextToSpeechSettings;
		if (string.IsNullOrWhiteSpace(textToSpeechSettings.TextToSpeechHotKey))
		{
			textToSpeechSettings.TextToSpeechHotKey = "CTRL+SHIFT+Q";
			somethingWasMissing = true;
		}

		// Only inject a sample router mapping on FIRST creation of the settings file.
		// Previously we also injected when the list was empty, which could overwrite ("wipe")
		// user-defined mappings if deserialization produced an empty list for any reason.
		if (settings.HotKeyRouterSettings is null)
		{
			settings.HotKeyRouterSettings = new();
		}
		if (isNewFile && !settings.HotKeyRouterSettings.Mappings.Any())
		{
			settings.HotKeyRouterSettings.Mappings.Add(
				new HotKeyRouterSettings.HotKeyRouterMap("CONTROL+SHIFT+ALT+8", "CONTROL+SHIFT+ALT+9"));
		}

		return somethingWasMissing;
	}

	public void UpgradeSettings()
	{
		string json = File.ReadAllText(SettingsFileFullPath);

		JObject jObj = JObject.Parse(json);
		bool saveRequired = false;

		JObject? speechSettings = jObj["SpeechToTextSettings"] as JObject;
		if (speechSettings is null && jObj["SpeetchToTextSettings"] is JObject legacySpeechSettings)
		{
			speechSettings = legacySpeechSettings;
			jObj["SpeechToTextSettings"] = legacySpeechSettings;
			jObj.Remove("SpeetchToTextSettings");
			saveRequired = true;
		}

		if (speechSettings is not null)
		{
			if (speechSettings["Services"] == null || speechSettings["Services"].Type != JTokenType.Array)
			{
				string providerName = speechSettings.Value<string>("Service") ?? string.Empty;

				JObject serviceObj = new JObject
				{
					["Name"] = providerName,
					["Provider"] = providerName,
					["ApiKey"] = speechSettings["ApiKey"],
					["BaseDomain"] = speechSettings["BaseDomain"],
					["ModelId"] = speechSettings["ModelId"],
					["SpeechToTextPrompt"] = speechSettings["SpeechToTextPrompt"]
				};

				JArray createdServicesArray = new JArray { serviceObj };

				speechSettings.Remove("Service");
				speechSettings.Remove("ApiKey");
				speechSettings.Remove("BaseDomain");
				speechSettings.Remove("ModelId");
				speechSettings.Remove("SpeechToTextPrompt");

				speechSettings["ActiveSpeechToTextService"] = providerName;
				speechSettings["Services"] = createdServicesArray;
				saveRequired = true;
			}

			if (speechSettings["ActiveSpeechToTextService"] == null && speechSettings["ActiveSpeetchToTextService"] != null)
			{
				speechSettings["ActiveSpeechToTextService"] = speechSettings["ActiveSpeetchToTextService"];
				speechSettings.Remove("ActiveSpeetchToTextService");
				saveRequired = true;
			}

			if (speechSettings["SendHotkeyAfterTranscriptionOperation"] == null && speechSettings["SendKotKeyAfterTranscriptionOperation"] != null)
			{
				speechSettings["SendHotkeyAfterTranscriptionOperation"] = speechSettings["SendKotKeyAfterTranscriptionOperation"];
				speechSettings.Remove("SendKotKeyAfterTranscriptionOperation");
				saveRequired = true;
			}

			if (speechSettings["Services"] is JArray servicesArray)
			{
				foreach (var service in servicesArray)
				{
					if (service["Provider"]?.ToString() == "OpenAiWhisper")
					{
						service["Provider"] = "OpenAi";
						saveRequired = true;
					}
				}
			}
		}

		if (jObj["AzureComputerVisionSettings"] is JObject visionSettings)
		{
			if (visionSettings["SendHotkeyAfterOcrOperation"] == null && visionSettings["SendKotKeyAfterOcrOperation"] != null)
			{
				visionSettings["SendHotkeyAfterOcrOperation"] = visionSettings["SendKotKeyAfterOcrOperation"];
				visionSettings.Remove("SendKotKeyAfterOcrOperation");
				saveRequired = true;
			}
		}

		if (saveRequired)
		{
			AtomicWriteAllText(SettingsFileFullPath, jObj.ToString(Formatting.Indented));
		}
	}

	public void SaveSettingsToFile(Settings settings)
	{
		string json = JsonConvert.SerializeObject(settings, Formatting.Indented, _jsonSerializerSettings);
		AtomicWriteAllText(SettingsFilePath, json);
	}

	private static void AtomicWriteAllText(string targetPath, string contents)
	{
		string fullPath = Path.GetFullPath(targetPath);
		string directory = Path.GetDirectoryName(fullPath) ?? string.Empty;
		string tempPath = fullPath + ".tmp";
		string backupPath = fullPath + ".bak";

		File.WriteAllText(tempPath, contents, new UTF8Encoding(false));

		if (File.Exists(fullPath))
		{
			// Atomic on NTFS: original is moved to .bak, temp becomes the new file.
			File.Replace(tempPath, fullPath, backupPath, ignoreMetadataErrors: true);
		}
		else
		{
			File.Move(tempPath, fullPath);
		}
	}

    public Settings LoadAndEnsureSettings()
    {
        bool newFile = CreateSettingsFileIfNotExists(SettingsFileFullPath);

        UpgradeSettings();

        string json = File.ReadAllText(SettingsFileFullPath);
        Settings settings = JsonConvert.DeserializeObject<Settings>(json, _jsonSerializerSettings) ?? new Settings();

        if (EnsureSettings(settings, isNewFile: newFile))
        {
            SaveSettingsToFile(settings);
        }

        return settings;
    }
}
