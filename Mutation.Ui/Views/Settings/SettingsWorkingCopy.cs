using CognitiveSupport;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;
using System.Collections.Generic;

namespace Mutation.Ui.Views.SettingsUi;

// Holds an editable snapshot of the live Settings graph for the Settings dialog.
// Save commits the snapshot back into the live instance field-by-field so that
// references held elsewhere in the app (Settings, sub-objects) remain valid.
internal static class SettingsWorkingCopy
{
	private static readonly JsonSerializerSettings _serializer = new()
	{
		Converters = new List<JsonConverter> { new StringEnumConverter() },
	};

	public static Settings Clone(Settings live)
	{
		string json = JsonConvert.SerializeObject(live, _serializer);
		var clone = JsonConvert.DeserializeObject<Settings>(json, _serializer) ?? new Settings();
		return clone;
	}

	// Copy values from the working copy into the live Settings instance, preserving
	// the existing top-level reference so other parts of the app keep observing the
	// same object. Sub-object references are also preserved where possible.
	public static void CommitInto(Settings live, Settings workingCopy)
	{
		live.AudioSettings ??= new AudioSettings();
		if (workingCopy.AudioSettings is not null)
		{
			live.AudioSettings.ActiveCaptureDeviceFullName = workingCopy.AudioSettings.ActiveCaptureDeviceFullName;
			live.AudioSettings.MicrophoneToggleMuteHotKey = workingCopy.AudioSettings.MicrophoneToggleMuteHotKey;
			live.AudioSettings.EnableMicrophoneVisualization = workingCopy.AudioSettings.EnableMicrophoneVisualization;
			live.AudioSettings.CustomBeepSettings = workingCopy.AudioSettings.CustomBeepSettings;
		}

		live.AzureComputerVisionSettings ??= new AzureComputerVisionSettings();
		if (workingCopy.AzureComputerVisionSettings is not null)
		{
			var src = workingCopy.AzureComputerVisionSettings;
			var dst = live.AzureComputerVisionSettings;
			dst.InvertScreenshot = src.InvertScreenshot;
			dst.ScreenshotHotKey = src.ScreenshotHotKey;
			dst.ScreenshotOcrHotKey = src.ScreenshotOcrHotKey;
			dst.ScreenshotLeftToRightTopToBottomOcrHotKey = src.ScreenshotLeftToRightTopToBottomOcrHotKey;
			dst.OcrHotKey = src.OcrHotKey;
			dst.OcrLeftToRightTopToBottomHotKey = src.OcrLeftToRightTopToBottomHotKey;
			dst.SendHotkeyAfterOcrOperation = src.SendHotkeyAfterOcrOperation;
			dst.ApiKey = src.ApiKey;
			dst.Endpoint = src.Endpoint;
			dst.TimeoutSeconds = src.TimeoutSeconds;
			dst.UseFreeTier = src.UseFreeTier;
			dst.FreeTierPageLimit = src.FreeTierPageLimit;
			dst.MaxParallelDocuments = src.MaxParallelDocuments;
			dst.MaxParallelRequests = src.MaxParallelRequests;
			dst.MaxDocumentBytes = src.MaxDocumentBytes;
		}

		live.SpeechToTextSettings ??= new SpeechToTextSettings();
		if (workingCopy.SpeechToTextSettings is not null)
		{
			var src = workingCopy.SpeechToTextSettings;
			var dst = live.SpeechToTextSettings;
			dst.TempDirectory = src.TempDirectory;
			dst.SpeechToTextHotKey = src.SpeechToTextHotKey;
			dst.SpeechToTextWithLlmProcessingHotKey = src.SpeechToTextWithLlmProcessingHotKey;
			dst.SendHotkeyAfterTranscriptionOperation = src.SendHotkeyAfterTranscriptionOperation;
			dst.Services = src.Services;
			dst.ActiveSpeechToTextService = src.ActiveSpeechToTextService;
			dst.FileTranscriptionTimeoutSeconds = src.FileTranscriptionTimeoutSeconds;
			dst.EnableSilenceStripping = src.EnableSilenceStripping;
			dst.SilenceThresholdDbFs = src.SilenceThresholdDbFs;
			dst.MinSilenceSeconds = src.MinSilenceSeconds;
			dst.SilenceGuardMilliseconds = src.SilenceGuardMilliseconds;
		}

		live.LlmSettings ??= new LlmSettings();
		if (workingCopy.LlmSettings is not null)
		{
			var src = workingCopy.LlmSettings;
			var dst = live.LlmSettings;
			dst.OpenAiApiKey = src.OpenAiApiKey;
			dst.AnthropicApiKey = src.AnthropicApiKey;
			dst.Models = src.Models;
			dst.ProcessWithLlmHotKey = src.ProcessWithLlmHotKey;
			dst.Prompts = src.Prompts;
			dst.TimeoutSeconds = src.TimeoutSeconds;
			dst.RetryCount = src.RetryCount;
		}

		live.TranscriptFormatRules = workingCopy.TranscriptFormatRules ?? new List<TranscriptFormatRule>();

		live.TextToSpeechSettings ??= new TextToSpeechSettings();
		if (workingCopy.TextToSpeechSettings is not null)
		{
			var src = workingCopy.TextToSpeechSettings;
			var dst = live.TextToSpeechSettings;
			dst.SpeakClipboard = src.SpeakClipboard;
			dst.SpeakSelectionHotKey = src.SpeakSelectionHotKey;
			dst.RestartFromBeginningHotKey = src.RestartFromBeginningHotKey;
			dst.SkipSentenceBackwardHotKey = src.SkipSentenceBackwardHotKey;
			dst.SkipSentenceForwardHotKey = src.SkipSentenceForwardHotKey;
			dst.Rate = src.Rate;
			dst.Volume = src.Volume;
			dst.EnableSpeechPreprocessing = src.EnableSpeechPreprocessing;
			dst.VoiceName = src.VoiceName;
			dst.SkipSentenceGraceWindowMs = src.SkipSentenceGraceWindowMs;
		}

		live.MainWindowUiSettings ??= new MainWindowUiSettings();
		if (workingCopy.MainWindowUiSettings is not null)
		{
			var src = workingCopy.MainWindowUiSettings;
			var dst = live.MainWindowUiSettings;
			dst.WindowLocation = src.WindowLocation;
			dst.WindowSize = src.WindowSize;
			dst.MaxTextBoxLineCount = src.MaxTextBoxLineCount;
			dst.DictationInsertPreference = src.DictationInsertPreference;
		}

		live.HotKeyRouterSettings ??= new HotKeyRouterSettings();
		if (workingCopy.HotKeyRouterSettings is not null)
		{
			live.HotKeyRouterSettings.Mappings = workingCopy.HotKeyRouterSettings.Mappings;
		}
	}
}
