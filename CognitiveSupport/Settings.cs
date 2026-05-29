using System.Drawing;

namespace CognitiveSupport;

public class Settings
{
	public AudioSettings? AudioSettings { get; set; }
	public AzureComputerVisionSettings? AzureComputerVisionSettings { get; set; }
        public SpeechToTextSettings? SpeechToTextSettings { get; set; }
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
	public long? MaxDocumentBytes { get; set; }

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

        // Strip silent gaps from recorded/imported audio before transcription.
        public bool EnableSilenceStripping { get; set; } = true;

        // RMS loudness floor (dBFS); audio at or below this counts as silence.
        public double SilenceThresholdDbFs { get; set; } = -40.0;

        // Inter-speech gaps longer than this are trimmed down to this length (seconds).
        public double MinSilenceSeconds { get; set; } = 1.0;

        // Audio preserved on each side of speech to avoid clipping word edges (milliseconds).
        public double SilenceGuardMilliseconds { get; set; } = 200.0;
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

public class LlmSettings
{
	public const string DefaultModel = "chat-latest";
	public const string DefaultSecondaryModel = "gpt-4.1";
	public const string DefaultAnthropicModel = "claude-sonnet-4-6";

	public string? OpenAiApiKey { get; set; }
	public string? AnthropicApiKey { get; set; }
	public List<LlmModelConfig> Models { get; set; }
	public string? ProcessWithLlmHotKey { get; set; }
	public List<LlmPrompt> Prompts { get; set; } = new List<LlmPrompt>();
	public int TimeoutSeconds { get; set; } = 60;


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
	public int Rate { get; set; } = 8;
	public int Volume { get; set; } = 100;
	public bool EnableSpeechPreprocessing { get; set; } = true;
	public string? VoiceName { get; set; }

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

		public HotKeyRouterMap(
			string? fromHotKey,
			string? toHotKey)
		{
			FromHotKey = fromHotKey ?? throw new ArgumentNullException(nameof(fromHotKey));
			ToHotKey = toHotKey ?? throw new ArgumentNullException(nameof(toHotKey));
		}
	}
}
