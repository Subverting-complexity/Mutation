using System.Drawing;

namespace CognitiveSupport;

public class Settings
{
	public string? UserInstructions { get; set; }

	public AudioSettings? AudioSettings { get; set; }
	public AzureComputerVisionSettings? AzureComputerVisionSettings { get; set; }
        public SpeechToTextSettings? SpeechToTextSettings { get; set; }
	public LlmSettings? LlmSettings { get; set; }
	public TextToSpeechSettings? TextToSpeechSettings { get; set; }

	public MainWindowUiSettings MainWindowUiSettings { get; set; } = new MainWindowUiSettings();

	public HotKeyRouterSettings HotKeyRouterSettings { get; set; } = new HotKeyRouterSettings();

	public Settings()
	{
	}

        public Settings(string? userInstructions, AudioSettings? audioSettings, AzureComputerVisionSettings? azureComputerVisionSettings, SpeechToTextSettings? speechToTextSettings, LlmSettings? llmSettings, TextToSpeechSettings? textToSpeechSettings, MainWindowUiSettings mainWindowUiSettings, HotKeyRouterSettings hotKeyRouterSettings)
        {
                UserInstructions = userInstructions;
                AudioSettings = audioSettings;
                AzureComputerVisionSettings = azureComputerVisionSettings;
                SpeechToTextSettings = speechToTextSettings;
                LlmSettings = llmSettings;
                TextToSpeechSettings = textToSpeechSettings;
                MainWindowUiSettings = mainWindowUiSettings;
                HotKeyRouterSettings = hotKeyRouterSettings;
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

		// Helper to resolve audio file paths relative to the executable directory.
		// Constraints: rejects UNC paths and any path that escapes AppContext.BaseDirectory.
		// On violation returns string.Empty so callers' existing File.Exists check fails
		// gracefully into their normal "could not load" reporting path.
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

			string resolved;
			try { resolved = Path.GetFullPath(combined); }
			catch (Exception ex)
			{
				System.Diagnostics.Debug.WriteLine($"ResolveAudioFilePath GetFullPath failed for '{path}': {ex.Message}");
				return string.Empty;
			}

			if (!resolved.StartsWith(baseDir, StringComparison.OrdinalIgnoreCase))
			{
				System.Diagnostics.Debug.WriteLine($"ResolveAudioFilePath rejected out-of-base path: {path} -> {resolved}");
				return string.Empty;
			}

			return resolved;
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
        public string? SpeechToTextWithLlmFormattingHotKey { get; set; }

        // If this is not null, this hotkey will be sent to the system after a transcription operation completes.
        public string? SendHotkeyAfterTranscriptionOperation { get; set; }

        public SpeechToTextServiceSettings[]? Services { get; set; }
        public string? ActiveSpeechToTextService { get; set; }

        public int FileTranscriptionTimeoutSeconds { get; set; } = 300;
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
	public const string DefaultModel = "gpt-4.1";
	public const string DefaultSecondaryModel = "gpt-5.1";
	public const string DefaultAnthropicModel = "claude-sonnet-4-6";

	public string? ApiKey { get; set; }
	public string? AnthropicApiKey { get; set; }
	public List<string> Models { get; set; }
	public string? SelectedLlmModel { get; set; }
	public List<TranscriptFormatRule> TranscriptFormatRules { get; set; }
	public string? FormatTranscriptPrompt { get; set; }
	public string? FormatWithLlmHotKey { get; set; }
	public List<LlmPrompt> Prompts { get; set; } = new List<LlmPrompt>();
	public int TimeoutSeconds { get; set; } = 60;


	public LlmSettings()
	{
		Models = new List<string>();
		TranscriptFormatRules = new List<TranscriptFormatRule>();
		Prompts = new List<LlmPrompt>();
	}

	public LlmSettings(string? apiKey, List<string> models, List<TranscriptFormatRule> transcriptFormatRules, string? formatTranscriptPrompt)
	{
		ApiKey = apiKey;
		Models = models;
		TranscriptFormatRules = transcriptFormatRules;
		FormatTranscriptPrompt = formatTranscriptPrompt;
		Prompts = new List<LlmPrompt>();
	}

	public class LlmPrompt
	{
		public int Id { get; set; }
		public string Name { get; set; } = "Untitled";
		public string Content { get; set; } = "";
		public string? Hotkey { get; set; }
		public bool AutoRun { get; set; }

		public LlmPrompt() { }
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
}

public class TextToSpeechSettings
{
	public string? TextToSpeechHotKey { get; set; }

	public TextToSpeechSettings()
	{
	}

	public TextToSpeechSettings(string? textToSpeechHotKey)
	{
		TextToSpeechHotKey = textToSpeechHotKey;
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
