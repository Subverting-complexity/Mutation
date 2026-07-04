using CognitiveSupport;
using System;
using System.IO;

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
		public const long MaxDocumentBytes = 10L * 1024 * 1024;
		public const int MaxDocumentSizeMbUiMax = 500;
	}

	public static class Speech
	{
		public const string SpeechToTextHotKey = "SHIFT+ALT+U";
		public const string SpeechToTextWithLlmProcessingHotKey = "SHIFT+ALT+I";
		// Recordings hold dictated speech (often personal or sensitive), so the
		// default lives under the user profile where ACLs block other local users.
		// The pre-existing C:\Temp default was world-readable; EnsureSettings
		// rewrites it and session files are migrated on first run.
		public static readonly string TempDirectory = Path.Combine(
			Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Mutation");
		public const string LegacyTempDirectory = @"C:\Temp\Mutation";
		public const int FileTranscriptionTimeoutSeconds = 300;
		// Retained-session count: default and UI bounds mirror the domain so the dialog,
		// the load-time clamp, and cleanup all agree on one source of truth.
		public const int MaxRetainedSessions = SpeechToTextSettings.DefaultMaxRetainedSessions;
		public const int MinRetainedSessions = SpeechToTextSettings.MinRetainedSessions;
		public const int MaxRetainedSessionsLimit = SpeechToTextSettings.MaxRetainedSessionsLimit;
		public const int ServiceTimeoutSeconds = 10;
		public const string DefaultServiceName = "OpenAI Whisper 1";
		public const string DefaultServiceModelId = "whisper-1";
		public const string DefaultServiceBaseDomain = "https://api.openai.com/";
		public const string DefaultServicePrompt = "Hello, let's use punctuation. Names: Kobus, Piro.";
		public const bool EnableSilenceStripping = true;
		public const double SilenceThresholdDbFs = -40.0;
		public const double MinSilenceSeconds = 1.0;
		public const double SilenceGuardMilliseconds = 200.0;
	}

	public static class Tts
	{
		public const string SpeakClipboard = "CTRL+SHIFT+ALT+Q";
		public const string SpeakSelectionHotKey = "CTRL+SHIFT+Q";
		public const string RestartFromBeginningHotKey = "CTRL+SHIFT+B";
		public const string SkipSentenceBackwardHotKey = "CTRL+SHIFT+J";
		public const string SkipSentenceForwardHotKey = "CTRL+SHIFT+K";
		public const string SpeakPositionHotKey = "CTRL+SHIFT+P";
		public const string PauseResumeHotKey = "CTRL+SHIFT+SPACE";
		public const int Rate = 8;
		public const int Volume = 100;
		public const bool EnableSpeechPreprocessing = true;

		// Per-rule preprocessing defaults — all on, matching the previous
		// all-or-nothing cleanup so existing users see no change.
		public const bool PreprocessRemoveCodeBlocks = true;
		public const bool PreprocessStripBoldItalicCode = true;
		public const bool PreprocessStripHeadingMarks = true;
		public const bool PreprocessShortenWebLinks = true;
		public const bool PreprocessStripBulletMarkers = true;
		public const bool PreprocessExpandAbbreviations = true;
		public const bool PreprocessNormaliseWhitespace = true;

		public const int SkipSentenceGraceWindowMs = 1500;
		public const int MinSkipSentenceGraceWindowMs = 250;
		public const int MaxSkipSentenceGraceWindowMs = 5000;
		public const int ResumeRewindWordCount = 5;
		public const int MinResumeRewindWordCount = 0;
		public const int MaxResumeRewindWordCount = 20;
		public const int ResumeRewindAfterPauseSeconds = 10;
		public const int MinResumeRewindAfterPauseSeconds = 0;
		public const int MaxResumeRewindAfterPauseSeconds = 120;

		public const bool AnnounceReadingTimeAtStart = true;
		public const int AnnounceReadingTimeMinimumMinutes = 1;
		public const int MinAnnounceReadingTimeMinimumMinutes = 0;
		public const int MaxAnnounceReadingTimeMinimumMinutes = 120;

		public const bool AnnounceProgressEnabled = true;
		public const int AnnounceProgressEveryPercent = 25;
		public const int MinAnnounceProgressEveryPercent = 5;
		public const int MaxAnnounceProgressEveryPercent = 50;
		public const int AnnounceProgressMinimumMinutes = 2;
		public const int MinAnnounceProgressMinimumMinutes = 0;
		public const int MaxAnnounceProgressMinimumMinutes = 120;
	}

	public static class Llm
	{
		public const int TimeoutSeconds = 60;
		public const int RetryCount = 3;
	}

	public static class MainWindowUi
	{
		public const int MaxTextBoxLineCount = 5;
		public const string DictationInsertPreference = "Paste";
	}
}
