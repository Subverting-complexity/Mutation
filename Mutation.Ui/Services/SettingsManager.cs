using CognitiveSupport;
using Mutation.Ui.Views.SettingsUi;
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

	/// <summary>
	/// The fields a pre-<c>Services</c> settings file kept loose on the
	/// <c>SpeechToTextSettings</c> section, one set per single configured provider.
	/// Their presence is what identifies such a file; <see cref="UpgradeSettings"/>
	/// collapses them into <c>Services[0]</c> and removes them.
	/// </summary>
	private static readonly string[] LegacyServiceFieldNames =
		{ "Service", "ApiKey", "BaseDomain", "ModelId", "SpeechToTextPrompt" };

	public string SettingsFilePath { get; }
	private string SettingsFileFullPath => Path.GetFullPath(SettingsFilePath);

	/// <summary>
	/// Custom beep files that could not be used, as found by the most recent
	/// <see cref="EnsureSettings"/>. Empty when they are all fine or custom beeps are
	/// off. Custom beeps have been switched off in the settings whenever this is
	/// non-empty.
	///
	/// Deliberately not on <see cref="ISettingsManager"/>: only startup surfaces these,
	/// and it holds the concrete manager. Everything else consumes settings, not the
	/// story of how they were loaded.
	/// </summary>
	public IReadOnlyList<string> CustomBeepIssues { get; private set; } = Array.Empty<string>();

	/// <summary>
	/// Hotkey-router mappings that had to be repaired by the most recent
	/// <see cref="EnsureSettings"/>. Empty when they were all well-formed. Surfaced
	/// the same way as <see cref="CustomBeepIssues"/>, and for the same reason.
	/// </summary>
	public IReadOnlyList<string> HotKeyRouterIssues { get; private set; } = Array.Empty<string>();

	/// <summary>
	/// Set when the most recent <see cref="EnsureSettings"/> had to replace an
	/// unusable temp directory, describing what was wrong and where recordings are
	/// stored instead. Null when the stored path was fine. Surfaced like
	/// <see cref="CustomBeepIssues"/>: the dialog can say this for itself, but a
	/// hand-edited file is only repaired at load, where there is no window yet.
	/// </summary>
	public string? TempDirectoryIssue { get; private set; }

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
			// Intentionally NOT opening the file in a text editor. First-run
			// configuration is handled in-app: App.OnLaunched shows a friendly
			// welcome message and opens the Settings dialog. The file is still
			// created here so the dialog has a target to save onto.
			return true;
		}
		return false;
	}

	internal bool EnsureSettings(Settings settings, bool isNewFile)
	{
		const string PlaceholderValue = "<placeholder>";
		const string PlaceholderUrl = "https://placeholder.com";

		bool somethingWasMissing = false;

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

                // null = never configured -> apply the 10 MB default. A stored 0 (or
                // negative) is left as-is and means "no limit".
                if (azureComputerVisionSettings.MaxDocumentBytes is null)
                {
                        azureComputerVisionSettings.MaxDocumentBytes = 10L * 1024 * 1024;
                        somethingWasMissing = true;
                }

		// Pointer-nudge timings. A settings file written before the nudge existed has neither
		// key, so both land on 0 rather than on the property initializer, and 0 would mean a
		// nudge that is switched on but never moves. Clamping into the same range the dialog
		// offers is what keeps a hand-edited file from producing a nudge nobody can stop.
		int nudgeInterval = azureComputerVisionSettings.PointerNudgeIntervalMilliseconds <= 0
			? AzureComputerVisionSettings.DefaultPointerNudgeIntervalMilliseconds
			: Math.Clamp(
				azureComputerVisionSettings.PointerNudgeIntervalMilliseconds,
				AzureComputerVisionSettings.MinPointerNudgeIntervalMilliseconds,
				AzureComputerVisionSettings.MaxPointerNudgeIntervalMilliseconds);
		if (nudgeInterval != azureComputerVisionSettings.PointerNudgeIntervalMilliseconds)
		{
			azureComputerVisionSettings.PointerNudgeIntervalMilliseconds = nudgeInterval;
			somethingWasMissing = true;
		}

		int nudgeDuration = azureComputerVisionSettings.PointerNudgeDurationMilliseconds <= 0
			? AzureComputerVisionSettings.DefaultPointerNudgeDurationMilliseconds
			: Math.Clamp(
				azureComputerVisionSettings.PointerNudgeDurationMilliseconds,
				AzureComputerVisionSettings.MinPointerNudgeDurationMilliseconds,
				AzureComputerVisionSettings.MaxPointerNudgeDurationMilliseconds);
		if (nudgeDuration != azureComputerVisionSettings.PointerNudgeDurationMilliseconds)
		{
			azureComputerVisionSettings.PointerNudgeDurationMilliseconds = nudgeDuration;
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
		// Snap playback speed to the nearest supported value so a hand-edited or
		// missing value can never set the player to an unsupported speed.
		double normalizedSpeed = PlaybackSpeedOptions.Normalize(audioSettings.PlaybackSpeed);
		if (normalizedSpeed != audioSettings.PlaybackSpeed)
		{
			audioSettings.PlaybackSpeed = normalizedSpeed;
			somethingWasMissing = true;
		}
		// Reported, not announced. This runs before there is a window to host a dialog,
		// so anything shown from here can only be a bare Win32 message box with no
		// automation name — nothing a screen reader can work with. The issues are handed
		// to the caller instead, and App.OnLaunched raises them once the window is ready.
		var beepIssues = CustomBeepFileValidator.Validate(audioSettings.CustomBeepSettings, File.Exists);
		if (beepIssues.Count > 0)
		{
			audioSettings.CustomBeepSettings.UseCustomBeeps = false;
			somethingWasMissing = true;
		}
		CustomBeepIssues = beepIssues;


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
			// Undefined as well as None. A number the enum never declared reaches the
			// switch that builds the service and throws there — outside the recovery
			// App.OnLaunched wraps the load in, so the user gets a bare startup crash
			// with no offer to restore their backup (issue #283).
			if (!Enum.IsDefined(s.Provider) || s.Provider == SpeechToTextProviders.None)
				s.Provider = SpeechToTextProviders.OpenAi;
			// A service's ApiKey is an optional override of the root-level ApiKeys.
			// Leave it blank by default so the central key is used unless the user
			// explicitly supplies a per-service key here.
			if (string.IsNullOrWhiteSpace(s.SpeechToTextPrompt))
			{
				s.SpeechToTextPrompt = "Hello, let's use punctuation. Names: Kobus, Piro.";
			}
			if (s.TimeoutSeconds <= 0)
			{
				s.TimeoutSeconds = 10;
			}
		}

		if (string.IsNullOrWhiteSpace(speechToTextSettings.SpeechToTextWithLlmProcessingHotKey))
		{
			speechToTextSettings.SpeechToTextWithLlmProcessingHotKey = "SHIFT+ALT+I";
			somethingWasMissing = true;
		}
		TempDirectoryIssue = null;
		if (IsLegacyTempDirectory(speechToTextSettings.TempDirectory))
		{
			// The old default under C:\ was readable by every local user; only an
			// unchanged default is rewritten — an explicitly different path is kept.
			speechToTextSettings.TempDirectory = SettingsDefaults.Speech.TempDirectory;
			somethingWasMissing = true;
		}
		else
		{
			// Blank, relative, or unusable falls back to the default. Left as-is, a
			// blank makes SessionsDirectory resolve relative to the executable, so
			// recordings land in the install folder (issue #230).
			var tempDirectory = TempDirectorySetting.Normalize(speechToTextSettings.TempDirectory);
			if (!string.Equals(tempDirectory.Path, speechToTextSettings.TempDirectory, StringComparison.Ordinal))
			{
				speechToTextSettings.TempDirectory = tempDirectory.Path;
				somethingWasMissing = true;
			}

			// Only a real repair is worth telling the user about; trimming or resolving
			// a path they would recognise anyway is not. Moving where their recordings
			// are kept is, and silently is exactly how this used to go wrong.
			TempDirectoryIssue = tempDirectory.WasRepaired
				? TempDirectorySetting.ComposeMessage(tempDirectory.Problem!, tempDirectory.Path)
				: null;
		}

		// Clamp silence-stripping values to sane ranges in case the file was hand-edited.
		double clampedDbFs = Math.Clamp(speechToTextSettings.SilenceThresholdDbFs, -80.0, 0.0);
		if (clampedDbFs != speechToTextSettings.SilenceThresholdDbFs)
		{
			speechToTextSettings.SilenceThresholdDbFs = clampedDbFs;
			somethingWasMissing = true;
		}
		double clampedMinSilence = Math.Clamp(speechToTextSettings.MinSilenceSeconds, 0.0, 30.0);
		if (clampedMinSilence != speechToTextSettings.MinSilenceSeconds)
		{
			speechToTextSettings.MinSilenceSeconds = clampedMinSilence;
			somethingWasMissing = true;
		}
		double clampedGuard = Math.Clamp(speechToTextSettings.SilenceGuardMilliseconds, 0.0, 2000.0);
		if (clampedGuard != speechToTextSettings.SilenceGuardMilliseconds)
		{
			speechToTextSettings.SilenceGuardMilliseconds = clampedGuard;
			somethingWasMissing = true;
		}

		// A settings file missing this field keeps the property initializer default (10);
		// an explicit out-of-range value is clamped to [1, 500] so cleanup cannot retain
		// zero (which would delete every recording) or an unbounded count.
		int clampedRetained = SpeechToTextSettings.ClampRetainedSessions(speechToTextSettings.MaxRetainedSessions);
		if (clampedRetained != speechToTextSettings.MaxRetainedSessions)
		{
			speechToTextSettings.MaxRetainedSessions = clampedRetained;
			somethingWasMissing = true;
		}

		// Same treatment for the per-request upload cap: a missing field keeps the 24 MB
		// initializer default, and an explicit value is clamped to [1 MB, 1000 MB] so a
		// hand-edited file cannot ask for chunks no service would accept. 0 stays 0 and
		// means "never split".
		long clampedUpload = SpeechToTextSettings.ClampTranscriptionUploadBytes(speechToTextSettings.MaxTranscriptionUploadBytes);
		if (clampedUpload != speechToTextSettings.MaxTranscriptionUploadBytes)
		{
			speechToTextSettings.MaxTranscriptionUploadBytes = clampedUpload;
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


		if (settings.ApiKeys is null)
		{
			settings.ApiKeys = new ApiKeys();
			somethingWasMissing = true;
		}
		var apiKeys = settings.ApiKeys;
		if (string.IsNullOrWhiteSpace(apiKeys.OpenAiApiKey))
		{
			apiKeys.OpenAiApiKey = PlaceholderValue;
			somethingWasMissing = true;
		}
		if (string.IsNullOrWhiteSpace(apiKeys.AnthropicApiKey))
		{
			apiKeys.AnthropicApiKey = PlaceholderValue;
			somethingWasMissing = true;
		}
		if (string.IsNullOrWhiteSpace(apiKeys.DeepgramApiKey))
		{
			apiKeys.DeepgramApiKey = PlaceholderValue;
			somethingWasMissing = true;
		}

		if (settings.LlmSettings is null)
		{
			settings.LlmSettings = new LlmSettings();
			somethingWasMissing = true;
		}
		var llmSettings = settings.LlmSettings;

		// Correct hand-edited invalid retry counts. A fresh LlmSettings already has
		// RetryCount = 3 via field init, so a clean object is unchanged (parity holds).
		if (llmSettings.RetryCount < 0)
		{
			llmSettings.RetryCount = 3;
			somethingWasMissing = true;
		}
		if (llmSettings.RetryCount > 10)
		{
			llmSettings.RetryCount = 10;
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
             string defaultPrompt = @"You are a helpful proofreader and editor. When you are asked to format a transcript, apply the following rules to improve the formatting of the text:
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

             llmSettings.Prompts.Add(new LlmSettings.LlmPrompt {
                Id = 1,
                Name = "Default",
                Content = defaultPrompt,
                Hotkey = "ALT+SHIFT+P",
                AutoRun = false,
                ModelName = LlmSettings.DefaultModel
             });
			 somethingWasMissing = true;
		}

		if (llmSettings.Models == null || !llmSettings.Models.Any())
		{
			llmSettings.Models = new List<LlmModelConfig>
			{
				new LlmModelConfig(LlmSettings.DefaultModel, LlmProvider.OpenAI, customTemperature: null),
				new LlmModelConfig(LlmSettings.DefaultSecondaryModel, LlmProvider.OpenAI, customTemperature: 0.7m),
				new LlmModelConfig(LlmSettings.DefaultAnthropicModel, LlmProvider.Anthropic, customTemperature: 0.7m),
			};
			somethingWasMissing = true;
		}

		foreach (var prompt in llmSettings.Prompts)
		{
			if (string.IsNullOrWhiteSpace(prompt.ModelName))
			{
				prompt.ModelName = LlmSettings.DefaultModel;
				somethingWasMissing = true;
			}
		}

		// Hand-written prompts have no Id and all default to 0, which makes them
		// indistinguishable to anything keyed on prompt identity. Backfilling here means
		// the Ids are also written back, so they stay put across restarts.
		if (PromptIdBackfill.Apply(llmSettings.Prompts))
			somethingWasMissing = true;

		// Seed the starter rules on a brand-new settings file only — the same
		// deliberate guard the router mappings use below. An existing file holding an
		// empty list is a user who deleted every rule on the Transcript formatting
		// page; re-seeding it put the defaults back in memory on every launch while
		// the file still said [], so the feature could never be turned off and the
		// state never converged (issue #220). A null list is still repaired, because the
		// rest of the app expects a list — and because that is how LoadAndEnsureSettings
		// signals a file that has no TranscriptFormatRules key at all, which is a user
		// upgrading from before the feature rather than one who switched it off.
		if (settings.TranscriptFormatRules == null
			|| (isNewFile && !settings.TranscriptFormatRules.Any()))
		{
			somethingWasMissing = true;
			settings.TranscriptFormatRules = new List<TranscriptFormatRule>
			{
				new TranscriptFormatRule
				{
					Find= "new line",
					ReplaceWith= $"{Environment.NewLine}",
					CaseSensitive = false,
					MatchType = TranscriptFormatRule.MatchTypeEnum.Smart,
				},
				new TranscriptFormatRule
				{
					Find= "newline",
					ReplaceWith= $"{Environment.NewLine}",
					CaseSensitive = false,
					MatchType = TranscriptFormatRule.MatchTypeEnum.Smart,
				},
				new TranscriptFormatRule
				{
					Find= "next line",
					ReplaceWith= $"{Environment.NewLine}",
					CaseSensitive = false,
					MatchType = TranscriptFormatRule.MatchTypeEnum.Smart,
				},
				new TranscriptFormatRule
				{
					Find= "new paragraph",
					ReplaceWith= $"{Environment.NewLine}{Environment.NewLine}",
					CaseSensitive = false,
					MatchType = TranscriptFormatRule.MatchTypeEnum.Smart,
				},
				new TranscriptFormatRule
				{
					Find= "new paragraphs",
					ReplaceWith= $"{Environment.NewLine}{Environment.NewLine}",
					CaseSensitive = false,
					MatchType = TranscriptFormatRule.MatchTypeEnum.Smart,
				},
				new TranscriptFormatRule
				{
					Find= "next paragraph",
					ReplaceWith= $"{Environment.NewLine}{Environment.NewLine}",
					CaseSensitive = false,
					MatchType = TranscriptFormatRule.MatchTypeEnum.Smart,
				},
				new TranscriptFormatRule
				{
					Find= "new bullet",
					ReplaceWith= $"{Environment.NewLine}- ",
					CaseSensitive = false,
					MatchType = TranscriptFormatRule.MatchTypeEnum.Smart,
				},
				new TranscriptFormatRule
				{
					Find= "next bullet",
					ReplaceWith= $"{Environment.NewLine}- ",
					CaseSensitive = false,
					MatchType = TranscriptFormatRule.MatchTypeEnum.Smart,
				},
				new TranscriptFormatRule
				{
					Find= "new colon",
					ReplaceWith= $": ",
					CaseSensitive = false,
					MatchType = TranscriptFormatRule.MatchTypeEnum.Smart,
				},
				new TranscriptFormatRule
				{
					Find= "semicolon",
					ReplaceWith= $"; ",
					CaseSensitive = false,
					MatchType = TranscriptFormatRule.MatchTypeEnum.Smart,
				},
				new TranscriptFormatRule
				{
					Find= "full stop",
					ReplaceWith= $". ",
					CaseSensitive = false,
					MatchType = TranscriptFormatRule.MatchTypeEnum.Smart,
				},
				new TranscriptFormatRule
				{
					Find= "comma",
					ReplaceWith= $", ",
					CaseSensitive = false,
					MatchType = TranscriptFormatRule.MatchTypeEnum.Smart,
				},
				new TranscriptFormatRule
				{
					Find= "exclamation mark",
					ReplaceWith= $"! ",
					CaseSensitive = false,
					MatchType = TranscriptFormatRule.MatchTypeEnum.Smart,
				},
				new TranscriptFormatRule
				{
					Find= "question mark",
					ReplaceWith= $"? ",
					CaseSensitive = false,
					MatchType = TranscriptFormatRule.MatchTypeEnum.Smart,
				},
				new TranscriptFormatRule
				{
					Find= "ellipsis",
					ReplaceWith= $"... ",
					CaseSensitive = false,
					MatchType = TranscriptFormatRule.MatchTypeEnum.Smart,
				},
				new TranscriptFormatRule
				{
					Find= "dot dot dot",
					ReplaceWith= $"... ",
					CaseSensitive = false,
					MatchType = TranscriptFormatRule.MatchTypeEnum.Smart,
				},


			};

		}

		if (settings.TextToSpeechSettings is null)
		{
			settings.TextToSpeechSettings = new TextToSpeechSettings();
			somethingWasMissing = true;
		}
		var textToSpeechSettings = settings.TextToSpeechSettings;
		if (string.IsNullOrWhiteSpace(textToSpeechSettings.SpeakClipboard))
		{
			textToSpeechSettings.SpeakClipboard = "CTRL+SHIFT+ALT+Q";
			somethingWasMissing = true;
		}
		if (string.IsNullOrWhiteSpace(textToSpeechSettings.SpeakSelectionHotKey))
		{
			textToSpeechSettings.SpeakSelectionHotKey = "CTRL+SHIFT+Q";
			somethingWasMissing = true;
		}
		if (string.IsNullOrWhiteSpace(textToSpeechSettings.RestartFromBeginningHotKey))
		{
			textToSpeechSettings.RestartFromBeginningHotKey = "CTRL+SHIFT+B";
			somethingWasMissing = true;
		}
		if (string.IsNullOrWhiteSpace(textToSpeechSettings.SkipSentenceBackwardHotKey))
		{
			textToSpeechSettings.SkipSentenceBackwardHotKey = "CTRL+SHIFT+J";
			somethingWasMissing = true;
		}
		if (string.IsNullOrWhiteSpace(textToSpeechSettings.SkipSentenceForwardHotKey))
		{
			textToSpeechSettings.SkipSentenceForwardHotKey = "CTRL+SHIFT+K";
			somethingWasMissing = true;
		}
		if (string.IsNullOrWhiteSpace(textToSpeechSettings.SpeakPositionHotKey))
		{
			textToSpeechSettings.SpeakPositionHotKey = "CTRL+SHIFT+P";
			somethingWasMissing = true;
		}
		if (string.IsNullOrWhiteSpace(textToSpeechSettings.PauseResumeHotKey))
		{
			textToSpeechSettings.PauseResumeHotKey = "CTRL+SHIFT+SPACE";
			somethingWasMissing = true;
		}

		// Only inject a sample router mapping on FIRST creation of the settings file.
		// Previously we also injected when the list was empty, which could overwrite ("wipe")
		// user-defined mappings if deserialization produced an empty list for any reason.
		if (settings.HotKeyRouterSettings is null)
		{
			settings.HotKeyRouterSettings = new();
		}
		settings.HotKeyRouterSettings.Mappings ??= new List<HotKeyRouterSettings.HotKeyRouterMap>();

		// Same shape as the custom beep check above: repair here, report to the
		// caller, and let App raise it once there is a window that can host an
		// accessible notice.
		var routerIssues = HotKeyRouterMappingRepair.Repair(settings.HotKeyRouterSettings.Mappings);
		if (routerIssues.Count > 0)
			somethingWasMissing = true;
		HotKeyRouterIssues = routerIssues;

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

		// Drop legacy UserInstructions (removed setting).
		if (jObj.Remove("UserInstructions"))
			saveRequired = true;

		JObject? speechSettings = jObj["SpeechToTextSettings"] as JObject;
		if (speechSettings is null && jObj["SpeetchToTextSettings"] is JObject legacySpeechSettings)
		{
			jObj["SpeechToTextSettings"] = legacySpeechSettings;
			jObj.Remove("SpeetchToTextSettings");
			// Re-fetch from jObj — Newtonsoft.Json deep-clones a JToken with an existing
			// parent on assignment, so the local `legacySpeechSettings` reference points at
			// a detached object. Subsequent in-block STT migrations would silently no-op
			// without this re-fetch.
			speechSettings = jObj["SpeechToTextSettings"] as JObject;
			saveRequired = true;
		}

		if (speechSettings is not null)
		{
			// Rename SpeechToTextWithLlmFormattingHotKey -> SpeechToTextWithLlmProcessingHotKey.
			if (speechSettings["SpeechToTextWithLlmFormattingHotKey"] is JToken legacyFormattingHotkey)
			{
				if (speechSettings["SpeechToTextWithLlmProcessingHotKey"] is null)
				{
					speechSettings["SpeechToTextWithLlmProcessingHotKey"] = legacyFormattingHotkey;
				}
				speechSettings.Remove("SpeechToTextWithLlmFormattingHotKey");
				saveRequired = true;
			}

			// Run the Active...Speetch... typo fix BEFORE the Service->Services collapse.
			// The collapse populates ActiveSpeechToTextService from the loose Service field,
			// which would otherwise shadow this typo-fix's null guard and leave the legacy
			// key in the JSON forever. (Caught by the full-legacy chain test.)
			if (speechSettings["ActiveSpeechToTextService"] == null && speechSettings["ActiveSpeetchToTextService"] != null)
			{
				speechSettings["ActiveSpeechToTextService"] = speechSettings["ActiveSpeetchToTextService"];
				speechSettings.Remove("ActiveSpeetchToTextService");
				saveRequired = true;
			}

			// A section with no Services array predates the array, so the loose fields
			// beside it are collapsed into a single synthesized service. Only do that when
			// at least one of them is actually there: a section carrying none of them is
			// not a legacy file at all, and synthesizing from nothing wrote a service with
			// a blank Name and a blank Provider — which the very next deserialize then
			// threw on, taking the whole settings file down with it (issue #283). Left
			// alone, EnsureSettings seeds the proper OpenAI Whisper default instead.
			bool hasServicesArray = speechSettings["Services"] is JToken servicesToken
				&& servicesToken.Type == JTokenType.Array;

			// A Services that is present but is neither an array nor null — an object left
			// behind by half-deleting the array, say — cannot be deserialized into one
			// either, and the throw takes the whole file with it. Drop it and let
			// EnsureSettings seed the default, the same outcome an absent key gets.
			if (!hasServicesArray
				&& speechSettings["Services"] is JToken malformedServices
				&& malformedServices.Type != JTokenType.Null)
			{
				speechSettings.Remove("Services");
				saveRequired = true;
			}

			if (!hasServicesArray && LegacyServiceFieldNames.Any(name => speechSettings[name] is not null))
			{
				// No legacy Service to carry over means the provider is unknown, not blank.
				// Name it what EnsureSettings would have: the OpenAI default.
				string legacyService = speechSettings.Value<string>("Service") ?? string.Empty;
				string providerName = string.IsNullOrWhiteSpace(legacyService)
					? nameof(SpeechToTextProviders.OpenAi)
					: legacyService;

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

				foreach (string name in LegacyServiceFieldNames)
					speechSettings.Remove(name);

				// Only seed ActiveSpeechToTextService if the typo-fix above didn't already
				// populate it from a legacy ActiveSpeetchToTextService.
				if (speechSettings["ActiveSpeechToTextService"] == null)
				{
					speechSettings["ActiveSpeechToTextService"] = providerName;
				}
				speechSettings["Services"] = createdServicesArray;
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
					if (service["Provider"] is not JToken provider)
						continue;

					string providerText = provider.Type is JTokenType.String or JTokenType.Integer
						? provider.ToString()
						: string.Empty;

					// Written back as the one spelling the enum actually has. That covers
					// three faults at once: the old "OpenAiWhisper" name, a value the enum
					// does not know (blank, null, a hand-typed name), and a value it parses
					// but never defined — a bare number, or a comma-list, both of which
					// Enum.TryParse accepts. The first is fatal inside the deserializer and
					// the last is fatal later, at the switch that builds the service, past
					// the recovery App.OnLaunched wraps the load in (issue #283).
					service["Provider"] = NormalizeProviderName(providerText);
					if (service["Provider"]!.ToString() != providerText)
						saveRequired = true;
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

		if (jObj["LlmSettings"] is JObject llmSettingsJObj)
		{
			if (llmSettingsJObj.Remove("SelectedLlmModel"))
			{
				saveRequired = true;
			}

			// Convert legacy Models from List<string> to List<LlmModelConfig>. The schema
			// changed when LlmModelConfig was introduced (May 2026). Without this, an old
			// config's `"Models": ["gpt-4.1", ...]` throws JsonSerializationException on
			// deserialize, falling through to recovery that wipes the user's LLM config.
			// Provider is inferred from the model name (claude* -> Anthropic, else OpenAI).
			if (llmSettingsJObj["Models"] is JArray modelsArray && modelsArray.Any(m => m.Type == JTokenType.String))
			{
				var converted = new JArray();
				foreach (var entry in modelsArray)
				{
					if (entry.Type == JTokenType.String)
					{
						string name = entry.ToString();
						string provider = name.StartsWith("claude", StringComparison.OrdinalIgnoreCase)
							? "Anthropic"
							: "OpenAI";
						converted.Add(new JObject
						{
							["Name"] = name,
							["Provider"] = provider,
							["CustomTemperature"] = null,
						});
					}
					else
					{
						converted.Add(entry);
					}
				}
				llmSettingsJObj["Models"] = converted;
				saveRequired = true;
			}

			// Rename LlmSettings.ApiKey -> OpenAiApiKey (only this class; Azure/STT ApiKey untouched).
			if (llmSettingsJObj["ApiKey"] is JToken legacyOpenAiKey)
			{
				if (llmSettingsJObj["OpenAiApiKey"] is null)
				{
					llmSettingsJObj["OpenAiApiKey"] = legacyOpenAiKey;
				}
				llmSettingsJObj.Remove("ApiKey");
				saveRequired = true;
			}

			// Move LlmSettings.OpenAiApiKey / AnthropicApiKey -> root ApiKeys section.
			// These keys are no longer LLM-specific (e.g. the OpenAI key is also used
			// for OpenAI/Whisper speech-to-text), so they live in a central ApiKeys
			// object. Runs AFTER the ApiKey -> OpenAiApiKey rename above so a legacy
			// LlmSettings.ApiKey is carried all the way to ApiKeys.OpenAiApiKey in one pass.
			{
				JObject apiKeys = jObj["ApiKeys"] as JObject ?? new JObject();
				bool movedAnyKey = false;
				foreach (string keyName in new[] { "OpenAiApiKey", "AnthropicApiKey" })
				{
					if (llmSettingsJObj[keyName] is JToken movedKey)
					{
						if (apiKeys[keyName] is null)
							apiKeys[keyName] = movedKey;
						llmSettingsJObj.Remove(keyName);
						movedAnyKey = true;
					}
				}
				if (movedAnyKey)
				{
					jObj["ApiKeys"] = apiKeys;
					saveRequired = true;
				}
			}

			// Move LlmSettings.TranscriptFormatRules -> root TranscriptFormatRules (rules aren't LLM-specific).
			if (llmSettingsJObj["TranscriptFormatRules"] is JToken legacyRules)
			{
				if (jObj["TranscriptFormatRules"] is null)
				{
					jObj["TranscriptFormatRules"] = legacyRules;
				}
				llmSettingsJObj.Remove("TranscriptFormatRules");
				saveRequired = true;
			}

			// Rename LlmSettings.FormatWithLlmHotKey -> ProcessWithLlmHotKey.
			if (llmSettingsJObj["FormatWithLlmHotKey"] is JToken legacyProcessHotkey)
			{
				if (llmSettingsJObj["ProcessWithLlmHotKey"] is null)
				{
					llmSettingsJObj["ProcessWithLlmHotKey"] = legacyProcessHotkey;
				}
				llmSettingsJObj.Remove("FormatWithLlmHotKey");
				saveRequired = true;
			}

			// Drop legacy FormatTranscriptPrompt. If non-empty and Prompts is empty, seed Prompts[0] from it.
			if (llmSettingsJObj["FormatTranscriptPrompt"] is JToken legacyFormatPrompt)
			{
				string? legacyPromptText = legacyFormatPrompt.Type == JTokenType.String ? legacyFormatPrompt.ToString() : null;
				bool noPrompts = llmSettingsJObj["Prompts"] is not JArray existingPrompts || existingPrompts.Count == 0;
				if (noPrompts && !string.IsNullOrWhiteSpace(legacyPromptText))
				{
					string? legacyHotkey = llmSettingsJObj["ProcessWithLlmHotKey"]?.ToString();
					if (string.IsNullOrWhiteSpace(legacyHotkey))
						legacyHotkey = "ALT+SHIFT+P";

					llmSettingsJObj["Prompts"] = new JArray
					{
						new JObject
						{
							["Id"] = 1,
							["Name"] = "Default",
							["Content"] = legacyPromptText,
							["Hotkey"] = legacyHotkey,
							["AutoRun"] = false,
							["ModelName"] = LlmSettings.DefaultModel,
						},
					};
				}
				llmSettingsJObj.Remove("FormatTranscriptPrompt");
				saveRequired = true;
			}

			// Drop the obsolete single-hotkey LlmSettings.ProcessWithLlmHotKey. Per-prompt
			// hotkeys (Prompts[].Hotkey) now drive every LLM action. This runs AFTER the
			// FormatTranscriptPrompt seed above, which still reads ProcessWithLlmHotKey to
			// carry a legacy single hotkey forward into the seeded Prompts[0].
			if (llmSettingsJObj["ProcessWithLlmHotKey"] is not null)
			{
				llmSettingsJObj.Remove("ProcessWithLlmHotKey");
				saveRequired = true;
			}

			if (llmSettingsJObj["Prompts"] is JArray promptsArray)
			{
				foreach (var promptToken in promptsArray)
				{
					if (promptToken is JObject promptObj)
					{
						JToken? modelToken = promptObj["ModelName"];
						if (modelToken == null || modelToken.Type == JTokenType.Null || string.IsNullOrWhiteSpace(modelToken.ToString()))
						{
							promptObj["ModelName"] = LlmSettings.DefaultModel;
							saveRequired = true;
						}
					}
				}
			}
		}

		// Rename TextToSpeechSettings.TextToSpeechHotKey -> SpeakClipboard.
		if (jObj["TextToSpeechSettings"] is JObject ttsSettingsJObj
			&& ttsSettingsJObj["TextToSpeechHotKey"] is JToken legacyTtsHotkey)
		{
			if (ttsSettingsJObj["SpeakClipboard"] is null)
			{
				ttsSettingsJObj["SpeakClipboard"] = legacyTtsHotkey;
			}
			ttsSettingsJObj.Remove("TextToSpeechHotKey");
			saveRequired = true;
		}

		if (saveRequired)
		{
			AtomicWriteAllText(SettingsFileFullPath, jObj.ToString(Formatting.Indented));
		}
	}

	/// <summary>
	/// True when the settings JSON actually carries a <c>TranscriptFormatRules</c> key —
	/// including an explicit <c>null</c> or an empty array. False means the file predates
	/// the feature, which is the one case that still deserves the starter rules.
	/// Never throws: an unparseable file is reported as declaring the key, so the loaded
	/// settings are left exactly as they came out of the deserializer.
	/// </summary>
	internal static bool DeclaresTranscriptFormatRules(string json)
	{
		try
		{
			return JObject.Parse(json)["TranscriptFormatRules"] is not null;
		}
		catch
		{
			return true;
		}
	}

	/// <summary>
	/// The stored provider spelled the way <see cref="SpeechToTextProviders"/> spells it,
	/// or the OpenAI default when it names no provider the app can actually build.
	/// <see cref="Enum.TryParse{TEnum}(string, bool, out TEnum)"/> alone is not enough:
	/// it happily returns a value of 5 for "5" and 3 for "OpenAi, Deepgram", neither of
	/// which is defined, so <see cref="Enum.IsDefined{TEnum}(TEnum)"/> has to gate it too.
	/// </summary>
	internal static string NormalizeProviderName(string? storedProvider) =>
		Enum.TryParse<SpeechToTextProviders>(storedProvider, ignoreCase: true, out var provider)
		&& Enum.IsDefined(provider)
		&& provider != SpeechToTextProviders.None
			? provider.ToString()
			: nameof(SpeechToTextProviders.OpenAi);

	internal static bool IsLegacyTempDirectory(string? tempDirectory)
	{
		if (string.IsNullOrWhiteSpace(tempDirectory))
			return false;
		string normalized = tempDirectory.Trim().TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
		return string.Equals(normalized, SettingsDefaults.Speech.LegacyTempDirectory, StringComparison.OrdinalIgnoreCase);
	}

	public void SaveSettingsToFile(Settings settings)
	{
		string json = JsonConvert.SerializeObject(settings, Formatting.Indented, _jsonSerializerSettings);
		AtomicWriteAllText(SettingsFilePath, json);

		// Keep the error-log redactor current with the just-saved keys so a key
		// entered via the Settings dialog is redacted without an app restart.
		RegisterSecretsForRedaction(settings);
	}

	// Feeds every configured provider key into ErrorLogger's exact-match redactor
	// so none of them can ever be written verbatim to the error log, regardless
	// of the key's format. Placeholder/blank values are filtered by the redactor.
	private static void RegisterSecretsForRedaction(Settings settings)
	{
		var secrets = new List<string?>
		{
			settings.ApiKeys?.OpenAiApiKey,
			settings.ApiKeys?.AnthropicApiKey,
			settings.ApiKeys?.DeepgramApiKey,
			settings.AzureComputerVisionSettings?.ApiKey,
		};

		var services = settings.SpeechToTextSettings?.Services;
		if (services is not null)
		{
			foreach (var service in services)
				secrets.Add(service?.ApiKey);
		}

		ErrorLogger.RegisterSecretValues(secrets);
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

        // Settings.TranscriptFormatRules is declared with a `= new List<>()` initialiser,
        // so a file written before transcript formatting existed — one carrying no
        // TranscriptFormatRules key at all — deserialises to exactly what a user who
        // deleted every rule leaves behind: an empty list. Only the JSON can tell those
        // two apart, and only the first should get the starter rules. Handing the null
        // to EnsureSettings seeds them once and writes them out, after which the key is
        // present and an empty list stays empty (issue #220).
        if (!DeclaresTranscriptFormatRules(json))
            settings.TranscriptFormatRules = null!;

        bool hadLegacyTempDirectory = IsLegacyTempDirectory(settings.SpeechToTextSettings?.TempDirectory);

        if (EnsureSettings(settings, isNewFile: newFile))
        {
            SaveSettingsToFile(settings);
        }

        if (hadLegacyTempDirectory)
        {
            SessionRecordingsMigrator.MigrateSessions(
                SettingsDefaults.Speech.LegacyTempDirectory,
                settings.SpeechToTextSettings!.TempDirectory!);
        }

        // Register the loaded keys so any error logged during this run redacts
        // them by exact match, covering formats no pattern recognises.
        RegisterSecretsForRedaction(settings);

        return settings;
    }
}
