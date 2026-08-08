using CognitiveSupport;
using Mutation.Ui.Views.SettingsUi;

namespace Mutation.Ui.Core;

/// <summary>
/// Every shortcut the app itself owns, in the order the Hotkeys page shows them.
/// <para>
/// It lives here rather than on the settings page because two screens need it now. The page
/// builds a row per entry; the prompt editor reads the same list to tell the user that the
/// shortcut they are typing is already taken (issue #340). A second, hand-kept copy of this
/// list in the prompt editor would go stale the first time a shortcut was added here.
/// </para>
/// </summary>
internal static class CoreHotkeys
{
	public static readonly HotkeySpec[] All = new[]
	{
		new HotkeySpec("Toggle microphone mute",
			s => s.AudioSettings?.MicrophoneToggleMuteHotKey,
			(s, v) => (s.AudioSettings ??= new AudioSettings()).MicrophoneToggleMuteHotKey = v,
			false, SettingsDefaults.Audio.MicrophoneToggleMuteHotKey),

		new HotkeySpec("Take screenshot",
			s => s.AzureComputerVisionSettings?.ScreenshotHotKey,
			(s, v) => (s.AzureComputerVisionSettings ??= new AzureComputerVisionSettings()).ScreenshotHotKey = v,
			false, SettingsDefaults.Ocr.ScreenshotHotKey),
		new HotkeySpec("Screenshot + OCR",
			s => s.AzureComputerVisionSettings?.ScreenshotOcrHotKey,
			(s, v) => (s.AzureComputerVisionSettings ??= new AzureComputerVisionSettings()).ScreenshotOcrHotKey = v,
			false, SettingsDefaults.Ocr.ScreenshotOcrHotKey),
		new HotkeySpec("Screenshot + OCR (left-to-right)",
			s => s.AzureComputerVisionSettings?.ScreenshotLeftToRightTopToBottomOcrHotKey,
			(s, v) => (s.AzureComputerVisionSettings ??= new AzureComputerVisionSettings()).ScreenshotLeftToRightTopToBottomOcrHotKey = v,
			false, SettingsDefaults.Ocr.ScreenshotLeftToRightTopToBottomOcrHotKey),
		new HotkeySpec("OCR clipboard",
			s => s.AzureComputerVisionSettings?.OcrHotKey,
			(s, v) => (s.AzureComputerVisionSettings ??= new AzureComputerVisionSettings()).OcrHotKey = v,
			false, SettingsDefaults.Ocr.OcrHotKey),
		new HotkeySpec("OCR clipboard (left-to-right)",
			s => s.AzureComputerVisionSettings?.OcrLeftToRightTopToBottomHotKey,
			(s, v) => (s.AzureComputerVisionSettings ??= new AzureComputerVisionSettings()).OcrLeftToRightTopToBottomHotKey = v,
			false, SettingsDefaults.Ocr.OcrLeftToRightTopToBottomHotKey),
		new HotkeySpec("Send key after OCR (optional)",
			s => s.AzureComputerVisionSettings?.SendHotkeyAfterOcrOperation,
			(s, v) => (s.AzureComputerVisionSettings ??= new AzureComputerVisionSettings()).SendHotkeyAfterOcrOperation = v,
			true, null, Registers: false),

		new HotkeySpec("Speech to text",
			s => s.SpeechToTextSettings?.SpeechToTextHotKey,
			(s, v) => (s.SpeechToTextSettings ??= new SpeechToTextSettings()).SpeechToTextHotKey = v,
			false, SettingsDefaults.Speech.SpeechToTextHotKey),
		new HotkeySpec("Speech to text + process with LLM",
			s => s.SpeechToTextSettings?.SpeechToTextWithLlmProcessingHotKey,
			(s, v) => (s.SpeechToTextSettings ??= new SpeechToTextSettings()).SpeechToTextWithLlmProcessingHotKey = v,
			false, SettingsDefaults.Speech.SpeechToTextWithLlmProcessingHotKey),
		new HotkeySpec("Send key after transcription (optional)",
			s => s.SpeechToTextSettings?.SendHotkeyAfterTranscriptionOperation,
			(s, v) => (s.SpeechToTextSettings ??= new SpeechToTextSettings()).SendHotkeyAfterTranscriptionOperation = v,
			true, null, Registers: false),

		new HotkeySpec("Speak clipboard",
			s => s.TextToSpeechSettings?.SpeakClipboard,
			(s, v) => (s.TextToSpeechSettings ??= new TextToSpeechSettings()).SpeakClipboard = v,
			false, SettingsDefaults.Tts.SpeakClipboard),
		new HotkeySpec("Speak selection",
			s => s.TextToSpeechSettings?.SpeakSelectionHotKey,
			(s, v) => (s.TextToSpeechSettings ??= new TextToSpeechSettings()).SpeakSelectionHotKey = v,
			false, SettingsDefaults.Tts.SpeakSelectionHotKey),
		new HotkeySpec("Restart speech from beginning",
			s => s.TextToSpeechSettings?.RestartFromBeginningHotKey,
			(s, v) => (s.TextToSpeechSettings ??= new TextToSpeechSettings()).RestartFromBeginningHotKey = v,
			false, SettingsDefaults.Tts.RestartFromBeginningHotKey),
		new HotkeySpec("Skip sentence backward",
			s => s.TextToSpeechSettings?.SkipSentenceBackwardHotKey,
			(s, v) => (s.TextToSpeechSettings ??= new TextToSpeechSettings()).SkipSentenceBackwardHotKey = v,
			false, SettingsDefaults.Tts.SkipSentenceBackwardHotKey),
		new HotkeySpec("Skip sentence forward",
			s => s.TextToSpeechSettings?.SkipSentenceForwardHotKey,
			(s, v) => (s.TextToSpeechSettings ??= new TextToSpeechSettings()).SkipSentenceForwardHotKey = v,
			false, SettingsDefaults.Tts.SkipSentenceForwardHotKey),
		new HotkeySpec("Speak reading position",
			s => s.TextToSpeechSettings?.SpeakPositionHotKey,
			(s, v) => (s.TextToSpeechSettings ??= new TextToSpeechSettings()).SpeakPositionHotKey = v,
			false, SettingsDefaults.Tts.SpeakPositionHotKey),
		new HotkeySpec("Pause or resume reading",
			s => s.TextToSpeechSettings?.PauseResumeHotKey,
			(s, v) => (s.TextToSpeechSettings ??= new TextToSpeechSettings()).PauseResumeHotKey = v,
			false, SettingsDefaults.Tts.PauseResumeHotKey),
	};
}
