namespace Mutation.Ui.Views.SettingsUi;

// Single source of truth for default values exposed by the Settings dialog and
// used by SettingsManager.EnsureSettings. Keep parity with EnsureSettings — a
// unit test verifies every default round-trips through EnsureSettings unchanged.
internal static class SettingsDefaults
{
	public const string PlaceholderValue = "<placeholder>";
	public const string PlaceholderUrl = "https://placeholder.com";

	public static class Audio
	{
		public const string MicrophoneToggleMuteHotKey = "ALT+Q";
		public const bool EnableMicrophoneVisualization = true;
	}

	public static class Ocr
	{
		public const string ScreenshotHotKey = "SHIFT+ALT+K";
		public const string OcrHotKey = "ALT+J";
		public const string ScreenshotOcrHotKey = "SHIFT+ALT+J";
		public const string OcrLeftToRightTopToBottomHotKey = "ALT+K";
		public const string ScreenshotLeftToRightTopToBottomOcrHotKey = "SHIFT+ALT+E";
		public const int TimeoutSeconds = 10;
		public const bool UseFreeTier = true;
		public const int FreeTierPageLimit = 2;
		public const int MaxParallelDocuments = 2;
		public const int MaxParallelRequests = 4;
		public const int MaxFreeTierPageLimit = 20;
		public const int MaxParallelRequestsLimit = 20;
	}

	public static class Speech
	{
		public const string SpeechToTextHotKey = "SHIFT+ALT+U";
		public const string SpeechToTextWithLlmFormattingHotKey = "SHIFT+ALT+I";
		public const string TempDirectory = @"C:\Temp\Mutation";
		public const int FileTranscriptionTimeoutSeconds = 300;
		public const int ServiceTimeoutSeconds = 10;
		public const string DefaultServiceName = "OpenAI Whisper 1";
		public const string DefaultServiceModelId = "whisper-1";
		public const string DefaultServiceBaseDomain = "https://api.openai.com/";
		public const string DefaultServicePrompt = "Hello, let's use punctuation. Names: Kobus, Piro.";
	}

	public static class Tts
	{
		public const string SpeakClipboard = "CTRL+SHIFT+ALT+Q";
		public const string SpeakSelectionHotKey = "CTRL+SHIFT+Q";
		public const string RestartFromBeginningHotKey = "CTRL+SHIFT+B";
		public const string SkipSentenceBackwardHotKey = "CTRL+SHIFT+J";
		public const string SkipSentenceForwardHotKey = "CTRL+SHIFT+K";
		public const int Rate = 8;
		public const int Volume = 100;
		public const bool EnableSpeechPreprocessing = true;
	}

	public static class Llm
	{
		public const int TimeoutSeconds = 60;
		public const string FormatPromptHotKey = "ALT+SHIFT+P";
	}

	public static class MainWindowUi
	{
		public const int MaxTextBoxLineCount = 5;
		public const string DictationInsertPreference = "Paste";
	}
}
