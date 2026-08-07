using System;
using System.Drawing;

namespace CognitiveSupport;

public class Settings
{
	public AudioSettings? AudioSettings { get; set; }
	public AzureComputerVisionSettings? AzureComputerVisionSettings { get; set; }
        public SpeechToTextSettings? SpeechToTextSettings { get; set; }
	public ApiKeys? ApiKeys { get; set; }
	public LlmSettings? LlmSettings { get; set; }
	public TextToSpeechSettings? TextToSpeechSettings { get; set; }

	public List<TranscriptFormatRule> TranscriptFormatRules { get; set; } = new List<TranscriptFormatRule>();

	public MainWindowUiSettings MainWindowUiSettings { get; set; } = new MainWindowUiSettings();

	public HotKeyRouterSettings HotKeyRouterSettings { get; set; } = new HotKeyRouterSettings();

	public Settings()
	{
	}
}

public class AudioSettings
{
	private CustomBeepSettingsData? customBeepSettings;

	public string? ActiveCaptureDeviceFullName { get; set; }
	public string? MicrophoneToggleMuteHotKey { get; set; }
        // Allows users to disable microphone visualization to save CPU
        public bool EnableMicrophoneVisualization { get; set; } = true;

        // User-pinned Windows capture level (0–100) for the active microphone. When set,
        // Mutation re-asserts this level on record/dictate, mic selection, and app startup
        // so it stays consistent regardless of what other apps do. null means pinning is
        // disabled ("don't manage the level").
        public int? PinnedCaptureLevel { get; set; }

	// Playback speed multiplier for the recorded-audio file player (1.0 = normal).
	// Restored on launch and snapped to the nearest supported speed on load. Pitch
	// is preserved at every speed. See PlaybackSpeedOptions for the allowed values.
	public double PlaybackSpeed { get; set; } = PlaybackSpeedOptions.Default;
	public CustomBeepSettingsData? CustomBeepSettings { get => customBeepSettings; set => customBeepSettings = value; }

	public AudioSettings() { }

	public AudioSettings(string? microphoneToggleMuteHotKey)
	{
		MicrophoneToggleMuteHotKey = microphoneToggleMuteHotKey;
	}

	public class CustomBeepSettingsData
	{
		public bool UseCustomBeeps { get; set; } = false;
		public string? BeepSuccessFile { get; set; }
		public string? BeepFailureFile { get; set; }
		public string? BeepStartFile { get; set; }
		public string? BeepEndFile { get; set; }
		public string? BeepMuteFile { get; set; }
		public string? BeepUnmuteFile { get; set; }

		// Helper to resolve audio file paths. Relative paths are resolved against the
		// executable directory; absolute local paths (any drive/folder) are allowed.
		// Constraint: rejects UNC/network paths, returning string.Empty so callers'
		// existing File.Exists check fails gracefully into their "could not load" path.
		public string ResolveAudioFilePath(string path)
		{
			if (string.IsNullOrWhiteSpace(path))
				return path;

			if (path.StartsWith(@"\\", StringComparison.Ordinal) ||
				path.StartsWith("//", StringComparison.Ordinal))
			{
				System.Diagnostics.Debug.WriteLine($"ResolveAudioFilePath rejected UNC path: {path}");
				return string.Empty;
			}

			string baseDir = Path.GetFullPath(AppContext.BaseDirectory);
			if (!baseDir.EndsWith(Path.DirectorySeparatorChar))
				baseDir += Path.DirectorySeparatorChar;

			string combined = Path.IsPathRooted(path) ? path : Path.Combine(baseDir, path);

			try { return Path.GetFullPath(combined); }
			catch (Exception ex)
			{
				System.Diagnostics.Debug.WriteLine($"ResolveAudioFilePath GetFullPath failed for '{path}': {ex.Message}");
				return string.Empty;
			}
		}

	}
}

public class MainWindowUiSettings
{
        public Point WindowLocation { get; set; }
        public Size WindowSize { get; set; }
        public int MaxTextBoxLineCount { get; set; } = 5;
        public string? DictationInsertPreference { get; set; } = "Paste";

        public MainWindowUiSettings()
        {
        }

        public MainWindowUiSettings(Point windowLocation, Size windowSize, int maxTextBoxLineCount = 5, string? dictationInsertPreference = "Paste")
        {
                WindowLocation = windowLocation;
                WindowSize = windowSize;
                MaxTextBoxLineCount = maxTextBoxLineCount;
                DictationInsertPreference = dictationInsertPreference;
        }
}

public class AzureComputerVisionSettings
{
	public bool InvertScreenshot { get; set; }
	public string? ScreenshotHotKey { get; set; }
	public string? ScreenshotOcrHotKey { get; set; }
	public string? ScreenshotLeftToRightTopToBottomOcrHotKey { get; set; }
	public string? OcrHotKey { get; set; }
	public string? OcrLeftToRightTopToBottomHotKey { get; set; }

	// If this is not null, this hotkey will be sent to the system after an OCR operation completes.
	public string? SendHotkeyAfterOcrOperation { get; set; }

	public string? ApiKey { get; set; }
	public string? Endpoint { get; set; }
	public int TimeoutSeconds { get; set; } = 10;
	public bool UseFreeTier { get; set; } = true;
	public int FreeTierPageLimit { get; set; } = 2;
	public int MaxParallelDocuments { get; set; } = 2;
	public int MaxParallelRequests { get; set; } = 4;

	// Maximum size of a single file/page sent for OCR. Files larger than this are
	// skipped before the upload. null or <= 0 means no limit. Default 10 MB.
	public long? MaxDocumentBytes { get; set; } = 10L * 1024 * 1024;

	public AzureComputerVisionSettings()
	{
	}
}

public class SpeechToTextSettings
{
        public string? TempDirectory { get; set; }
        public string? SpeechToTextHotKey { get; set; }
        public string? SpeechToTextWithLlmProcessingHotKey { get; set; }

        // If this is not null, this hotkey will be sent to the system after a transcription operation completes.
        public string? SendHotkeyAfterTranscriptionOperation { get; set; }

        public SpeechToTextServiceSettings[]? Services { get; set; }
        public string? ActiveSpeechToTextService { get; set; }

        public int FileTranscriptionTimeoutSeconds { get; set; } = 300;

        // Number of past speech-to-text recording sessions kept on disk. Once the count
        // exceeds this, cleanup deletes the oldest first (never the active recording).
        // Bounded to [MinRetainedSessions, MaxRetainedSessionsLimit]; a minimum of 1
        // guarantees the most recent recording is never deleted.
        public const int MinRetainedSessions = 1;
        public const int MaxRetainedSessionsLimit = 500;
        public const int DefaultMaxRetainedSessions = 10;

        public int MaxRetainedSessions { get; set; } = DefaultMaxRetainedSessions;

        // Clamp a retained-session count into the supported range. Applied on load and at
        // cleanup time so a hand-edited JSON value outside the range cannot break cleanup.
        public static int ClampRetainedSessions(int value) =>
                Math.Clamp(value, MinRetainedSessions, MaxRetainedSessionsLimit);

        // Per-attempt timeout for the live record→transcribe path. Kept generous so the
        // first call after a reboot or app update can absorb cold-start latency (cold
        // DNS/TLS/cert-chain and cold JIT) without timing out and triggering retries.
        public int LiveTranscriptionTimeoutSeconds { get; set; } = 60;

        // Strip silent gaps from recorded/imported audio before transcription.
        public bool EnableSilenceStripping { get; set; } = true;

        // RMS loudness floor (dBFS); audio at or below this counts as silence.
        public double SilenceThresholdDbFs { get; set; } = -40.0;

        // Inter-speech gaps longer than this are trimmed down to this length (seconds).
        public double MinSilenceSeconds { get; set; } = 1.0;

        // Audio preserved on each side of speech to avoid clipping word edges (milliseconds).
        public double SilenceGuardMilliseconds { get; set; } = 200.0;

	// Largest audio file sent in a single transcription request. Anything bigger is
	// split into chunks that are transcribed in order and stitched back together.
	//
	// The default sits just under OpenAI's 25 MB request limit; the margin absorbs the
	// multipart envelope and leaves room for a chunk that encodes slightly denser than
	// planned. 0 disables splitting entirely, so the whole file is always sent as-is.
	public const long DefaultMaxTranscriptionUploadBytes = 24L * 1024 * 1024;
	public const long MinTranscriptionUploadBytes = 1L * 1024 * 1024;
	public const long MaxTranscriptionUploadBytesLimit = 1000L * 1024 * 1024;

	public long MaxTranscriptionUploadBytes { get; set; } = DefaultMaxTranscriptionUploadBytes;

	// Clamp an upload limit into the supported range. Applied on load and again before
	// use so a hand-edited JSON value cannot produce chunks no service would accept.
	// 0 or less is preserved as "no limit" rather than clamped up, matching the OCR cap.
	public static long ClampTranscriptionUploadBytes(long value) =>
		value <= 0 ? 0 : Math.Clamp(value, MinTranscriptionUploadBytes, MaxTranscriptionUploadBytesLimit);
}

public class SpeechToTextServiceSettings
{
        public string? Name { get; set; }
	public SpeechToTextProviders Provider { get; set; }
	public string? ApiKey { get; set; }
	public string? BaseDomain { get; set; }
	public string? ModelId { get; set; }
	public string? SpeechToTextPrompt { get; set; }
	public int TimeoutSeconds { get; set; } = 10;
}

// Central store for provider API keys. These are the primary keys used across the
// app (LLM, speech-to-text, etc.); they are not LLM-specific. A speech service's
// per-service ApiKey acts as an optional override that takes precedence when set.
public class ApiKeys
{
	public string? OpenAiApiKey { get; set; }
	public string? AnthropicApiKey { get; set; }
	public string? DeepgramApiKey { get; set; }

	public ApiKeys()
	{
	}
}

public class LlmSettings
{
	public const string DefaultModel = "chat-latest";
	public const string DefaultSecondaryModel = "gpt-4.1";
	public const string DefaultAnthropicModel = "claude-sonnet-4-6";

	public List<LlmModelConfig> Models { get; set; }
	public List<LlmPrompt> Prompts { get; set; } = new List<LlmPrompt>();
	public int TimeoutSeconds { get; set; } = 60;
	public int RetryCount { get; set; } = 3;


	public LlmSettings()
	{
		Models = new List<LlmModelConfig>();
		Prompts = new List<LlmPrompt>();
	}

	public class LlmPrompt
	{
		public int Id { get; set; }
		public string Name { get; set; } = "Untitled";
		public string Content { get; set; } = "";
		public string? Hotkey { get; set; }
		public bool AutoRun { get; set; }
		public string? ModelName { get; set; }

		/// <summary>
		/// Run this prompt's model at premium inference speed. Same model and same
		/// output quality, roughly twice the token price, so it is off by default and
		/// settings files written before this existed load as false.
		/// </summary>
		public bool FastMode { get; set; }

		public LlmPrompt() { }
	}
}

public class TranscriptFormatRule
{
	public string? Find { get; set; }
	public string? ReplaceWith { get; set; }
	public bool CaseSensitive { get; set; }
	public MatchTypeEnum MatchType { get; set; }

	public TranscriptFormatRule()
	{
	}

	public TranscriptFormatRule(string? find, string? replaceWith, bool caseSensitive, MatchTypeEnum matchType)
	{
		Find = find;
		ReplaceWith = replaceWith;
		CaseSensitive = caseSensitive;
		MatchType = matchType;
	}

	public enum MatchTypeEnum
	{
		Plain = 1,
		RegEx = 2,
		Smart = 3,
	}
}

public class TextToSpeechSettings
{
	public string? SpeakClipboard { get; set; }
	public string? SpeakSelectionHotKey { get; set; }
	public string? RestartFromBeginningHotKey { get; set; }
	public string? SkipSentenceBackwardHotKey { get; set; }
	public string? SkipSentenceForwardHotKey { get; set; }
	public string? SpeakToFileHotKey { get; set; }
	public string? SpeakPositionHotKey { get; set; }

	// Toggles a true pause/resume of the current read, separate from Stop. Default
	// Ctrl+Shift+Space. One press freezes playback in place; the next resumes it.
	public string? PauseResumeHotKey { get; set; }

	public int Rate { get; set; } = 8;
	public int Volume { get; set; } = 100;
	public bool EnableSpeechPreprocessing { get; set; } = true;

	// Per-rule speech-preprocessing switches. Each gates one cleanup rule applied
	// before text is spoken; all default true so existing behaviour is unchanged.
	// The master EnableSpeechPreprocessing switch above gates all of them at once.
	public bool PreprocessRemoveCodeBlocks { get; set; } = true;
	public bool PreprocessStripBoldItalicCode { get; set; } = true;
	public bool PreprocessStripHeadingMarks { get; set; } = true;
	public bool PreprocessShortenWebLinks { get; set; } = true;
	public bool PreprocessStripBulletMarkers { get; set; } = true;
	public bool PreprocessExpandAbbreviations { get; set; } = true;
	public bool PreprocessNormaliseWhitespace { get; set; } = true;

	public string? VoiceName { get; set; }

	// How long (milliseconds) after a sentence starts that a "skip backward" press
	// counts as "still at the start" and therefore steps to the previous sentence.
	// After this window a back-press restarts the current sentence (media-player
	// style). A larger value makes stepping back easier; a smaller value favours
	// re-reading the current sentence.
	public int SkipSentenceGraceWindowMs { get; set; } = 1500;

	// When resuming playback (pressing the speak hotkey again after a pause), rewind
	// this many words before where playback stopped so the listener regains context
	// of where they are. 0 resumes exactly where it stopped (no rewind).
	public int ResumeRewindWordCount { get; set; } = 5;

	// When resuming after a Pause, rewind for context (by ResumeRewindWordCount words)
	// only if the pause lasted longer than this many seconds; a quicker pause resumes
	// seamlessly from the exact word. 0 means always rewind on resume, however brief
	// the pause.
	public int ResumeRewindAfterPauseSeconds { get; set; } = 10;

	// Master on/off for the startup "Reading approximately N minutes" announcement.
	// Default true to preserve the previous behaviour.
	public bool AnnounceReadingTimeAtStart { get; set; } = true;

	// The startup announcement plays only when the estimated read time exceeds this
	// many minutes. Replaces the old hardcoded 5000-character cutoff.
	public int AnnounceReadingTimeMinimumMinutes { get; set; } = 1;

	// Master on/off for the periodic progress announcements spoken while reading.
	public bool AnnounceProgressEnabled { get; set; } = true;

	// Announce progress at each multiple of this percentage (e.g. 25 -> 25/50/75%).
	public int AnnounceProgressEveryPercent { get; set; } = 25;

	// Periodic progress is announced only when the estimated total read time exceeds
	// this many minutes; shorter reads stay silent.
	public int AnnounceProgressMinimumMinutes { get; set; } = 2;

	public TextToSpeechSettings()
	{
	}

	public TextToSpeechSettings(string? speakClipboard)
	{
		SpeakClipboard = speakClipboard;
	}
}

public class HotKeyRouterSettings
{
	public List<HotKeyRouterMap> Mappings { get; set; } = new List<HotKeyRouterMap>();

	public HotKeyRouterSettings()
	{
	}

	public HotKeyRouterSettings(List<HotKeyRouterMap> mappings)
	{
		Mappings = mappings;
	}

	public class HotKeyRouterMap
	{
		public string? FromHotKey { get; set; }
		public string? ToHotKey { get; set; }

		// A half-finished mapping is a normal state, not a fault: the Hotkeys page
		// adds a blank row and lets the user fill in the two sides one at a time,
		// and the router simply treats an incomplete row as inactive. The
		// constructor used to throw on null anyway, which meant a settings file
		// with "FromHotKey": null took the whole load down with it — every other
		// setting lost to one unfinished mapping (issue #247). Null is accepted
		// here and repaired to blank on load.
		public HotKeyRouterMap()
		{
		}

		public HotKeyRouterMap(
			string? fromHotKey,
			string? toHotKey)
		{
			FromHotKey = fromHotKey;
			ToHotKey = toHotKey;
		}
	}
}
