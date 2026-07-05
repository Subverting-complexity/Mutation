using CognitiveSupport;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Mutation.Ui.Core;
using Mutation.Ui.Services;
using Mutation.Ui.Views;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Runtime.InteropServices;
using Windows.System;
using Windows.Media.Core;
using Windows.Media.Playback;
using Windows.Storage;
using Windows.Storage.Pickers;
using Windows.Storage.Streams;
using WinRT.Interop;


namespace Mutation.Ui;

public sealed partial class MainWindow : Window, IDisposable
{
	private readonly ClipboardManager _clipboard;
	private readonly UiStateManager _uiStateManager;
	private readonly ISettingsManager _settingsManager;
	private readonly AudioDeviceManager _audioDeviceManager;
	private readonly OcrManager _ocrManager;
	private readonly ISpeechToTextService[] _speechServices;
	private readonly Mutation.Ui.Core.AudioSessionManager _audioSessionManager;
	private readonly Mutation.Ui.Core.MicrophoneLevelPinService _micLevelPinService;
	// Runs the mute toggle's COM writes, read-back verification, and any device
	// re-enumeration off the UI thread so a hotkey press during a device hot-plug
	// never freezes the UI (and the screen reader).
	private readonly Mutation.Ui.Core.MicrophoneMuteToggleCoordinator _muteToggleCoordinator;
	// Serializes the mic-level slider and pin-toggle COM writes onto the shared
	// background worker so a drag burst never runs the write (and its failure-path
	// device re-enumeration) on the UI thread, and never overlaps the record-start
	// re-assert on the same COM endpoint.
	private readonly Mutation.Ui.Core.MicrophoneLevelWriteCoordinator _micLevelWriteCoordinator;
	private readonly TranscriptFormatter _transcriptFormatter;
	private readonly ITextToSpeechService _textToSpeech;
	private readonly IWavFileSpeechExporter _wavFileSpeechExporter;
	private readonly Settings _settings;
	private HotkeyManager? _hotkeyManager;
	private MicrophoneVisualizationController? _microphoneVisualization;
	private PromptLibraryController? _promptLibrary;

	// Suppress auto-format/clipboard/beep when we change text programmatically or during record/transcribe
	private bool _suppressAutoActions = false;

	private ISpeechToTextService? _activeSpeechService;
	private CancellationTokenSource _formatDebounceCts = new();
	private CancellationTokenSource _promptDebounceCts = new();
	// Slider settings persistence is coalesced to this quiet period after the last
	// change, mirroring the existing prompt TextBox debounce.
	private static readonly TimeSpan SettingsSaveDebounceDelay = TimeSpan.FromMilliseconds(500);
	// Coalesces per-tick settings saves from the main-window sliders. A drag or a
	// held arrow key raises ValueChanged dozens of times per second, and saving on
	// every tick serialized the whole settings object and atomically replaced the
	// file (plus its .bak) on the UI thread — audible slider stutter and disk churn
	// (issue #172). Each handler applies its value to _settings immediately; only the
	// file write is deferred. The close handler saves _settings unconditionally, so a
	// pending write is never lost on shutdown.
	private readonly Debouncer _settingsSaveDebouncer;
	private readonly CancellationTokenSource _shutdownCts = new();
	private DictationInsertOption _insertOption = DictationInsertOption.Paste;
	private readonly DispatcherTimer _statusDismissTimer;
	// Shows modal dialogs one at a time; a dialog requested while another
	// is open is queued and shown when it closes, never dropped (issue #167).
	private readonly DialogQueue<ContentDialogResult> _dialogQueue = new();
	private bool _ttsControlsReady;
	private bool _micLevelControlsReady;
	private bool _playbackSpeedReady;
	// True once the mic-level controls have been set up at least once, so the
	// microphone-selection handler knows it is safe to re-sync them for a new device.
	private bool _micLevelInitialized;
	// Slider position used when pinning is toggled off and the device has no
	// readable level to fall back to, so the control still shows a sensible value.
	private const int DefaultMicLevel = 75;
	private const string DefaultVoiceLabel = "(System default)";

	private static readonly IReadOnlyDictionary<string, string> AudioMimeTypeMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
	{
		[".aac"] = "audio/aac",
		[".flac"] = "audio/flac",
		[".m4a"] = "audio/mp4",
		[".mp3"] = "audio/mpeg",
		[".ogg"] = "audio/ogg",
		[".opus"] = "audio/opus",
		[".wav"] = "audio/wav",
		[".wma"] = "audio/x-ms-wma",
	};

	private const string MicOnGlyph = "\uE720";
	// '\uE7C8' is the Segoe MDL2 Assets glyph for a circular record icon, chosen for its clear visual representation.
	// Previously, '\uE768' was used, but '\uE7C8' better matches the standard record symbol.
	private const string RecordGlyph = "\uE7C8";
	private const string StopGlyph = "\uE71A";
	private const string ProcessingGlyph = "\uE8A0";
	private const string PlayGlyph = "\uE768";
	private const string MagicGlyph = "\uE890";

	private const string DoNotInsertExplanation = "Keep the transcript inside Mutation without sending it anywhere.";
	private const string SendKeysExplanation = "Types the transcript into the active app as if you entered it yourself.";
	private const string PasteExplanation = "Copies the transcript and pastes it into the active application.";
	private const double ApproximateLineHeightMultiplier = 1.35;
	private const double MinimumLineHeightInDips = 1.0;

	[DllImport("user32.dll")]
	private static extern IntPtr GetForegroundWindow();

	[DllImport("user32.dll")]
	private static extern uint GetClipboardSequenceNumber();

	public MainWindow(
		ClipboardManager clipboard,
		UiStateManager uiStateManager,
		AudioDeviceManager audioDeviceManager,
		OcrManager ocrManager,
		ISpeechToTextService[] speechServices,
		ITextToSpeechService textToSpeech,
		IWavFileSpeechExporter wavFileSpeechExporter,
		TranscriptFormatter transcriptFormatter,

		ISettingsManager settingsManager,
		Settings settings,
		Mutation.Ui.Core.AudioSessionManager audioSessionManager,
		Mutation.Ui.Core.MicrophoneLevelPinService micLevelPinService,
		Mutation.Ui.Core.MicrophoneLevelWriteCoordinator micLevelWriteCoordinator)
	{
		_clipboard = clipboard;
		_uiStateManager = uiStateManager;
		_settingsManager = settingsManager;
		_audioDeviceManager = audioDeviceManager;
		_ocrManager = ocrManager;
		_speechServices = speechServices;
		_textToSpeech = textToSpeech;
		_wavFileSpeechExporter = wavFileSpeechExporter;
		_transcriptFormatter = transcriptFormatter;
		_settings = settings;

        _settingsSaveDebouncer = new Debouncer(
            SettingsSaveDebounceDelay,
            () => _settingsManager.SaveSettingsToFile(_settings));

        _audioSessionManager = audioSessionManager;
        _micLevelPinService = micLevelPinService;
        _micLevelWriteCoordinator = micLevelWriteCoordinator;
        _muteToggleCoordinator = new Mutation.Ui.Core.MicrophoneMuteToggleCoordinator(_audioDeviceManager.ToggleMute);
        _audioSessionManager.StateChanged += AudioSessionManager_StateChanged;
        _audioSessionManager.TranscriptReady += AudioSessionManager_TranscriptReady;
        _audioSessionManager.ErrorOccurred += AudioSessionManager_ErrorOccurred;
        _audioSessionManager.StatusMessage += AudioSessionManager_StatusMessage;
        _audioSessionManager.SelectedSessionChanged += AudioSessionManager_SelectedSessionChanged;
        _audioSessionManager.PlaybackStarted += AudioSessionManager_PlaybackStarted;
        _audioSessionManager.PlaybackStopped += AudioSessionManager_PlaybackStopped;

        InitializeComponent();

        if (Content is UIElement rootForKeys)
        {
            rootForKeys.KeyDown += RootContent_KeyDown;
        }

        _microphoneVisualization = new MicrophoneVisualizationController(
            DispatcherQueue,
            _audioDeviceManager,
            _settings,
            _settingsManager,
            MicWaveformPlot,
            MicWaveformOffLabel,
            MicLevelMeter,
            RmsLevelBar,
            MicPulseOverlay,
            ShowStatus);
        _microphoneVisualization.Initialize();
        SyncMicWaveToggleState();

        _audioSessionManager.RefreshSessions();
                UpdatePlaybackButtonVisuals("Play selected session", PlayGlyph);
                AutomationProperties.SetHelpText(BtnRetrySpeechToText, "Transcribe the selected session again.");
                AutomationProperties.SetHelpText(BtnUploadSpeechAudio, "Upload an audio file for transcription.");
                AutomationProperties.SetHelpText(BtnSessionNewer, "Switch to a newer session.");
                AutomationProperties.SetHelpText(BtnSessionOlder, "Switch to an older session.");

		ApplyMultiLineTextBoxPreferences();

		_statusDismissTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(6) };
		_statusDismissTimer.Tick += StatusDismissTimer_Tick;
		StatusInfoBar.CloseButtonClick += StatusInfoBar_CloseButtonClick;

		_audioDeviceManager.EnsureDefaultMicrophoneSelected();

		UpdateMicrophoneToggleVisuals();
		UpdateSpeechButtonVisuals("Record", RecordGlyph);
		var micList = _audioDeviceManager.CaptureDevices.ToList();
		CmbMicrophone.ItemsSource = micList;
		// DisplayMemberPath replaced by using custom CaptureDeviceComboItem if needed; keep for compatibility
		CmbMicrophone.DisplayMemberPath = nameof(CoreAudio.MMDevice.DeviceFriendlyName);

		RestorePersistedMicrophoneSelection(micList);
		_microphoneVisualization.StartCapture();

		CmbSpeechService.ItemsSource = _speechServices;
		CmbSpeechService.DisplayMemberPath = nameof(ISpeechToTextService.ServiceName);

		RestorePersistedSpeechServiceSelection();
		UpdateRecordingActionAvailability();

		// TxtFormatPrompt.Text = _settings.LlmSettings?.FormatTranscriptPrompt ?? string.Empty;

            _promptLibrary = new PromptLibraryController(
                _settings,
                _settingsManager,
                _transcriptFormatter,
                LstPrompts,
                ExecutePrompt,
                failures => _ = ShowHotkeyBindingFailuresAsync(failures));
            _promptLibrary.Initialize();

		var tooltipManager = new TooltipManager(_settings);
		tooltipManager.SetupTooltips(TxtRawTranscript, TxtFormatTranscript);

		var insertOptions = Enum.GetValues(typeof(DictationInsertOption)).Cast<DictationInsertOption>().ToList();
		CmbInsertOption.ItemsSource = insertOptions;
		var persistedInsertPreference = _settings.MainWindowUiSettings?.DictationInsertPreference;
		if (!string.IsNullOrWhiteSpace(persistedInsertPreference) && Enum.TryParse(persistedInsertPreference, true, out DictationInsertOption persistedOption))
		{
			_insertOption = persistedOption;
		}
		else
		{
			_insertOption = DictationInsertOption.Paste;
		}
		CmbInsertOption.SelectedItem = _insertOption;
		UpdateThirdPartyExplanation(_insertOption);

		// After initializing and restoring the active microphone, play a sound
		// representing the current state (mute/unmute) to reflect actual status.
		if (_audioDeviceManager.Microphone != null)
			BeepPlayer.Play(_audioDeviceManager.IsMuted ? BeepType.Mute : BeepType.Unmute);

		InitializeTextToSpeechControls();
		InitializePlaybackSpeedControl();
		InitializeMicrophoneLevelControls();
		InitializeHotkeyVisuals();

		this.Closed += MainWindow_Closed;
		this.Activated += MainWindow_Activated;
	}

	// When Mutation returns to the foreground, re-sync the mic input-level slider to
	// the OS's real current level: another app (Windows Settings, Zoom, OBS, …) may
	// have changed it while Mutation was in the background, leaving the slider showing
	// a stale value. Deactivation is ignored — there is nothing to refresh on the way
	// out.
	private void MainWindow_Activated(object sender, WindowActivatedEventArgs args)
	{
		if (args.WindowActivationState == WindowActivationState.Deactivated)
			return;

		RefreshMicLevelDisplayFromOs();
	}

	// Reads the microphone's actual current level from the OS and moves the slider to
	// match. This is a display sync only: it never re-asserts the pin (that stays tied
	// to recording start) and never writes the level back — the _micLevelControlsReady
	// guard suppresses the SldMicLevel_ValueChanged side effect while the value is set
	// programmatically. When the level is unreadable (hardware-fixed device or a
	// transient failure) the slider is left as-is rather than reset to a misleading
	// default. Setting SldMicLevel.Value still raises the slider's UIA ValueChanged
	// automation event, so a screen-reader / ZoomText user is notified of the change.
	private void RefreshMicLevelDisplayFromOs()
	{
		// _micLevelControlsReady is only true on a supported, initialized device; on a
		// hardware-fixed device the control is disabled and there is nothing to sync.
		if (!_micLevelControlsReady)
			return;

		if (_micLevelPinService.ReadCurrentLevel() is not int level)
			return;

		bool wasReady = _micLevelControlsReady;
		_micLevelControlsReady = false;
		try
		{
			SldMicLevel.Value = level;
		}
		finally
		{
			_micLevelControlsReady = wasReady;
		}
	}

	private void ApplyMultiLineTextBoxPreferences()
	{
		int configuredMaxLines = _settings.MainWindowUiSettings?.MaxTextBoxLineCount ?? 5;
		if (configuredMaxLines <= 0)
			configuredMaxLines = 5;

		foreach (var textBox in GetMultiLineTextBoxes())
		{
			if (textBox is null)
				continue;

			double lineHeight = Math.Max(textBox.FontSize * ApproximateLineHeightMultiplier, MinimumLineHeightInDips);
			double padding = textBox.Padding.Top + textBox.Padding.Bottom;
			double desiredMaxHeight = (lineHeight * configuredMaxLines) + padding;

			if (double.IsNaN(desiredMaxHeight) || double.IsInfinity(desiredMaxHeight) || desiredMaxHeight <= 0)
				continue;

			textBox.MaxHeight = desiredMaxHeight;

			if (textBox.MinHeight > desiredMaxHeight)
				textBox.MinHeight = lineHeight + padding;
		}
	}

	private IEnumerable<TextBox> GetMultiLineTextBoxes()
	{
		yield return TxtRawTranscript;
		// yield return TxtFormatPrompt;
		yield return TxtFormatTranscript;
		yield return TxtOcr;
	}

	public IReadOnlyList<HotkeyManager.HotkeyBindingFailure> AttachHotkeyManager(HotkeyManager hotkeyManager)
	{
		_hotkeyManager = hotkeyManager;

		var failures = new List<HotkeyManager.HotkeyBindingFailure>();
		if (_promptLibrary is not null)
			failures.AddRange(_promptLibrary.AttachHotkeyManager(hotkeyManager));

		var routerResults = _hotkeyManager.RegisterRouterHotkeys();
		failures.AddRange(HotkeyManager.ToBindingFailures(routerResults));
		return failures;
	}

	internal IReadOnlyList<HotkeyManager.HotkeyBindingFailure> RegisterCoreHotkeys(HotkeyManager hk)
	{
		var ocr = _settings.AzureComputerVisionSettings;
		var stt = _settings.SpeechToTextSettings;
		var tts = _settings.TextToSpeechSettings;
		var aud = _settings.AudioSettings;

		var failures = new List<HotkeyManager.HotkeyBindingFailure>();

		if (!string.IsNullOrWhiteSpace(ocr?.ScreenshotHotKey))
			TryRegister(hk, failures, "Screenshot to clipboard", ocr.ScreenshotHotKey!, async () =>
			{
				try { await _ocrManager.TakeScreenshotToClipboardAsync(); }
				catch (Exception ex) { await ShowErrorDialog("Screenshot Error", ex); }
			});

		if (!string.IsNullOrWhiteSpace(ocr?.ScreenshotOcrHotKey))
			TryRegister(hk, failures, "Screenshot and OCR", ocr.ScreenshotOcrHotKey!, async () =>
			{
				try
				{
					if (!await EnsureOcrConfiguredAsync()) return;
					var result = await _ocrManager.TakeScreenshotAndExtractTextAsync(OcrReadingOrder.TopToBottomColumnAware);
					SetOcrText(result.Message);
					HotkeyManager.SendHotkeyAfterDelay(_settings.AzureComputerVisionSettings?.SendHotkeyAfterOcrOperation, result.Success ? Constants.SendHotkeyDelay : Constants.FailureSendHotkeyDelay);
				}
				catch (Exception ex) { await ShowErrorDialog("Screenshot + OCR Error", ex); }
			});

		if (!string.IsNullOrWhiteSpace(ocr?.ScreenshotLeftToRightTopToBottomOcrHotKey))
			TryRegister(hk, failures, "Screenshot and OCR (left-to-right)", ocr.ScreenshotLeftToRightTopToBottomOcrHotKey!, async () =>
			{
				try
				{
					if (!await EnsureOcrConfiguredAsync()) return;
					var result = await _ocrManager.TakeScreenshotAndExtractTextAsync(OcrReadingOrder.LeftToRightTopToBottom);
					SetOcrText(result.Message);
					HotkeyManager.SendHotkeyAfterDelay(_settings.AzureComputerVisionSettings?.SendHotkeyAfterOcrOperation, result.Success ? Constants.SendHotkeyDelay : Constants.FailureSendHotkeyDelay);
				}
				catch (Exception ex) { await ShowErrorDialog("Screenshot + OCR (LRTB) Error", ex); }
			});

		if (!string.IsNullOrWhiteSpace(ocr?.OcrHotKey))
			TryRegister(hk, failures, "OCR clipboard image", ocr.OcrHotKey!, async () =>
			{
				try
				{
					if (!await EnsureOcrConfiguredAsync()) return;
					var result = await _ocrManager.ExtractTextFromClipboardImageAsync(OcrReadingOrder.TopToBottomColumnAware);
					SetOcrText(result.Message);
					HotkeyManager.SendHotkeyAfterDelay(_settings.AzureComputerVisionSettings?.SendHotkeyAfterOcrOperation, result.Success ? Constants.SendHotkeyDelay : Constants.FailureSendHotkeyDelay);
				}
				catch (Exception ex) { await ShowErrorDialog("OCR Clipboard Error", ex); }
			});

		if (!string.IsNullOrWhiteSpace(ocr?.OcrLeftToRightTopToBottomHotKey))
			TryRegister(hk, failures, "OCR clipboard image (left-to-right)", ocr.OcrLeftToRightTopToBottomHotKey!, async () =>
			{
				try
				{
					if (!await EnsureOcrConfiguredAsync()) return;
					var result = await _ocrManager.ExtractTextFromClipboardImageAsync(OcrReadingOrder.LeftToRightTopToBottom);
					SetOcrText(result.Message);
					HotkeyManager.SendHotkeyAfterDelay(_settings.AzureComputerVisionSettings?.SendHotkeyAfterOcrOperation, result.Success ? Constants.SendHotkeyDelay : Constants.FailureSendHotkeyDelay);
				}
				catch (Exception ex) { await ShowErrorDialog("OCR Clipboard (LRTB) Error", ex); }
			});

		if (!string.IsNullOrWhiteSpace(aud?.MicrophoneToggleMuteHotKey))
			TryRegister(hk, failures, "Toggle microphone mute", aud.MicrophoneToggleMuteHotKey!, () =>
				DispatcherQueue.TryEnqueue(() => BtnToggleMic_Click(null!, null!)));

		if (!string.IsNullOrWhiteSpace(stt?.SpeechToTextHotKey))
			TryRegister(hk, failures, "Start/stop dictation", stt.SpeechToTextHotKey!, () =>
				DispatcherQueue.TryEnqueue(async () => await StartStopSpeechToTextAsync(false)));

		if (!string.IsNullOrWhiteSpace(stt?.SpeechToTextWithLlmProcessingHotKey))
			TryRegister(hk, failures, "Start/stop dictation with LLM processing", stt.SpeechToTextWithLlmProcessingHotKey!, () =>
				DispatcherQueue.TryEnqueue(async () => await StartStopSpeechToTextAsync(true)));

		if (!string.IsNullOrWhiteSpace(tts?.SpeakClipboard))
			TryRegister(hk, failures, "Speak clipboard", tts.SpeakClipboard!, () =>
				DispatcherQueue.TryEnqueue(() => BtnTextToSpeech_Click(null!, null!)));

		if (!string.IsNullOrWhiteSpace(tts?.SpeakSelectionHotKey))
			TryRegister(hk, failures, "Speak selection", tts.SpeakSelectionHotKey!, () =>
				DispatcherQueue.TryEnqueue(() => SpeakActiveSelectionAsync()));

		if (!string.IsNullOrWhiteSpace(tts?.RestartFromBeginningHotKey))
			TryRegister(hk, failures, "Restart speech from beginning", tts.RestartFromBeginningHotKey!, () =>
				DispatcherQueue.TryEnqueue(() => BtnRestartTts_Click(null!, null!)));

		if (!string.IsNullOrWhiteSpace(tts?.SkipSentenceBackwardHotKey))
			TryRegister(hk, failures, "Skip to previous sentence", tts.SkipSentenceBackwardHotKey!, () =>
				DispatcherQueue.TryEnqueue(() => BtnSkipSentenceBack_Click(null!, null!)));

		if (!string.IsNullOrWhiteSpace(tts?.SkipSentenceForwardHotKey))
			TryRegister(hk, failures, "Skip to next sentence", tts.SkipSentenceForwardHotKey!, () =>
				DispatcherQueue.TryEnqueue(() => BtnSkipSentenceForward_Click(null!, null!)));

		if (!string.IsNullOrWhiteSpace(tts?.SpeakToFileHotKey))
			TryRegister(hk, failures, "Speak to file", tts.SpeakToFileHotKey!, () =>
				DispatcherQueue.TryEnqueue(() => BtnSpeakToFile_Click(null!, null!)));

		if (!string.IsNullOrWhiteSpace(tts?.SpeakPositionHotKey))
			TryRegister(hk, failures, "Announce speech position", tts.SpeakPositionHotKey!, () =>
				DispatcherQueue.TryEnqueue(() => BtnSpeakPosition_Click(null!, null!)));

		if (!string.IsNullOrWhiteSpace(tts?.PauseResumeHotKey))
			TryRegister(hk, failures, "Pause/resume speech", tts.PauseResumeHotKey!, () =>
				DispatcherQueue.TryEnqueue(() => BtnPauseResume_Click(null!, null!)));

		return failures;
	}

	private static void TryRegister(HotkeyManager hk, List<HotkeyManager.HotkeyBindingFailure> failures, string description, string hotkey, Action callback)
	{
		var failure = hk.TryRegisterCore(description, hotkey, callback);
		if (failure is not null)
			failures.Add(failure);
	}

	private void RestorePersistedSpeechServiceSelection()
	{
		string? savedServiceName = _settings.SpeechToTextSettings?.ActiveSpeechToTextService;
		if (!string.IsNullOrWhiteSpace(savedServiceName))
		{
			var match = _speechServices.FirstOrDefault(s => s.ServiceName == savedServiceName);
			if (match != null)
			{
				CmbSpeechService.SelectedItem = match;
				_activeSpeechService = match;
			}
			else if (_speechServices.Length > 0)
			{
				CmbSpeechService.SelectedIndex = 0;
				_activeSpeechService = _speechServices[0];
			}
		}
		else if (_speechServices.Length > 0)
		{
			CmbSpeechService.SelectedIndex = 0;
			_activeSpeechService = _speechServices[0];
		}
	}

	private static string GetDeviceFriendlyName(CoreAudio.MMDevice device)
	{
#pragma warning disable CS0618
		var name = device.DeviceFriendlyName;
		if (string.IsNullOrWhiteSpace(name))
			name = device.FriendlyName;
#pragma warning restore CS0618
		return name ?? string.Empty;
	}

	private void RestorePersistedMicrophoneSelection(System.Collections.Generic.List<CoreAudio.MMDevice> micList)
	{
		string? savedMicFullName = _settings.AudioSettings?.ActiveCaptureDeviceFullName;
		if (!string.IsNullOrWhiteSpace(savedMicFullName))
		{
			var match = micList.FirstOrDefault(m => GetDeviceFriendlyName(m) == savedMicFullName);
			if (match != null)
				CmbMicrophone.SelectedItem = match;
			else if (_audioDeviceManager.Microphone != null)
				CmbMicrophone.SelectedItem = _audioDeviceManager.Microphone;
			else if (micList.Count > 0)
				CmbMicrophone.SelectedIndex = 0;
		}
		else if (_audioDeviceManager.Microphone != null)
			CmbMicrophone.SelectedItem = _audioDeviceManager.Microphone;
		else if (micList.Count > 0)
			CmbMicrophone.SelectedIndex = 0;
	}

	private void InitializeHotkeyVisuals()
	{
		ConfigureButtonHotkey(BtnToggleMic, null, _settings.AudioSettings?.MicrophoneToggleMuteHotKey, "Toggle microphone mute state");
		ConfigureButtonHotkey(BtnSpeechToText, null, _settings.SpeechToTextSettings?.SpeechToTextHotKey, "Start or stop speech capture");
		ConfigureButtonHotkey(BtnScreenshot, BtnScreenshotHotkey, _settings.AzureComputerVisionSettings?.ScreenshotHotKey, "Copy a screenshot directly to the clipboard");
		ConfigureButtonHotkey(BtnOcrClipboard, BtnOcrClipboardHotkey, _settings.AzureComputerVisionSettings?.OcrHotKey, "Run OCR on an image stored in the clipboard");
		ConfigureButtonHotkey(BtnOcrClipboardLrtb, BtnOcrClipboardLrtbHotkey, _settings.AzureComputerVisionSettings?.OcrLeftToRightTopToBottomHotKey, "Run OCR on an image stored in the clipboard using left-to-right reading order");
		ConfigureButtonHotkey(BtnScreenshotOcr, BtnScreenshotOcrHotkey, _settings.AzureComputerVisionSettings?.ScreenshotOcrHotKey, "Capture a screenshot and extract text automatically");
		ConfigureButtonHotkey(BtnScreenshotOcrLrtb, BtnScreenshotOcrLrtbHotkey, _settings.AzureComputerVisionSettings?.ScreenshotLeftToRightTopToBottomOcrHotKey, "Capture a screenshot and extract text using left-to-right reading order");
		ConfigureButtonHotkey(BtnTextToSpeech, null, _settings.TextToSpeechSettings?.SpeakClipboard, "Play the clipboard text using text-to-speech");
		ConfigureButtonHotkey(BtnRestartTts, null, _settings.TextToSpeechSettings?.RestartFromBeginningHotKey, "Speak the clipboard from the beginning, ignoring saved position");
		ConfigureButtonHotkey(BtnSkipSentenceBack, null, _settings.TextToSpeechSettings?.SkipSentenceBackwardHotKey, "Jump to the previous sentence");
		ConfigureButtonHotkey(BtnSkipSentenceForward, null, _settings.TextToSpeechSettings?.SkipSentenceForwardHotKey, "Jump to the next sentence");
		ConfigureButtonHotkey(BtnSpeakSelection, null, _settings.TextToSpeechSettings?.SpeakSelectionHotKey, "Copy the current selection from the active app and read it aloud");
		ConfigureButtonHotkey(BtnSpeakToFile, null, _settings.TextToSpeechSettings?.SpeakToFileHotKey, "Save the clipboard text as a spoken WAV audio file");
		ConfigureButtonHotkey(BtnSpeakPosition, null, _settings.TextToSpeechSettings?.SpeakPositionHotKey, "Speak the current reading position: sentence, percentage, and time remaining");
		ConfigureButtonHotkey(BtnProcessLlm, null, null, "Send transcript through the configured language model");
	}

	private async void MainWindow_Closed(object sender, WindowEventArgs args)
	{
		// Prevent auto actions during shutdown
		_suppressAutoActions = true;
		// Signal shutdown to any in-flight transcription HTTP requests so they
		// observe cancellation rather than running until their server timeout.
		try { _shutdownCts.Cancel(); } catch (ObjectDisposedException) { }
		try
		{
            await _audioSessionManager.EnsureStoppedAsync();
		}
		catch { }
		_uiStateManager.Save(this);

		if (_activeSpeechService != null)
		{
			_settings.SpeechToTextSettings!.ActiveSpeechToTextService = _activeSpeechService.ServiceName;
			var serviceSettings = _settings.SpeechToTextSettings?.Services?.FirstOrDefault(s => s.Name == _activeSpeechService.ServiceName);
			if (serviceSettings != null)
			{
				serviceSettings.SpeechToTextPrompt = TxtSpeechToTextPrompt.Text;
			}
		}
		// _settings.LlmSettings!.FormatTranscriptPrompt = TxtFormatPrompt.Text;

		_settingsManager.SaveSettingsToFile(_settings);

        _audioSessionManager.Dispose();

		BeepPlayer.DisposePlayers();
		Dispose();
	}

	public async void BtnToggleMic_Click(object? sender, RoutedEventArgs? e)
	{
		// The toggle runs on a background thread; a null result means another
		// toggle was already in flight and this press was coalesced away — leave
		// the UI as-is rather than reporting a stale state.
		MuteToggleResult? toggled = await _muteToggleCoordinator.ToggleAsync();
		if (toggled is not MuteToggleResult result)
			return;

		// Back on the UI thread (the await resumes on the captured context).
		// Drives the graphic off the confirmed state, so a failed mute never
		// shows the muted icon.
		UpdateMicrophoneToggleVisuals();

		if (result.Success)
		{
			ShowStatus("Microphone", result.IsMuted ? "Microphone muted." : "Microphone is live.",
				result.IsMuted ? InfoBarSeverity.Warning : InfoBarSeverity.Success);
			BeepPlayer.Play(result.IsMuted ? BeepType.Mute : BeepType.Unmute);
		}
		else
		{
			// The write failed or could not be confirmed — do not claim the mic
			// is muted. Warn the user with the failure beep and an error message.
			ShowStatus("Microphone",
				"Could not change the microphone mute state. The microphone may still be live — please try again.",
				InfoBarSeverity.Error);
			BeepPlayer.Play(BeepType.Failure);
		}
	}

	private async void BtnScreenshot_Click(object sender, RoutedEventArgs e)
	{
		try
		{
			await _ocrManager.TakeScreenshotToClipboardAsync();
			ShowStatus("Screenshot", "Screenshot copied to the clipboard.", InfoBarSeverity.Success);
		}
		catch (Exception ex)
		{
			ShowStatus("Screenshot", ex.Message, InfoBarSeverity.Error);
			await ShowErrorDialog("Screenshot Error", ex);
		}
	}

	private async void BtnScreenshotOcr_Click(object sender, RoutedEventArgs e)
	{
		try
		{
			if (!await EnsureOcrConfiguredAsync()) return;
			var result = await _ocrManager.TakeScreenshotAndExtractTextAsync(OcrReadingOrder.TopToBottomColumnAware);
			SetOcrText(result.Message);
			HotkeyManager.SendHotkeyAfterDelay(_settings.AzureComputerVisionSettings?.SendHotkeyAfterOcrOperation, result.Success ? Constants.SendHotkeyDelay : Constants.FailureSendHotkeyDelay);
			if (result.Success)
				ShowStatus("Screenshot & OCR", "Text captured from screenshot.", InfoBarSeverity.Success);
			else
				ShowStatus("Screenshot & OCR", result.Message, InfoBarSeverity.Error);
		}
		catch (Exception ex)
		{
			ShowStatus("Screenshot & OCR", ex.Message, InfoBarSeverity.Error);
			await ShowErrorDialog("Screenshot + OCR Error", ex);
		}
	}

	private async void BtnScreenshotOcrLrtb_Click(object sender, RoutedEventArgs e)
	{
		try
		{
			if (!await EnsureOcrConfiguredAsync()) return;
			var result = await _ocrManager.TakeScreenshotAndExtractTextAsync(OcrReadingOrder.LeftToRightTopToBottom);
			SetOcrText(result.Message);
			HotkeyManager.SendHotkeyAfterDelay(_settings.AzureComputerVisionSettings?.SendHotkeyAfterOcrOperation, result.Success ? Constants.SendHotkeyDelay : Constants.FailureSendHotkeyDelay);
			if (result.Success)
				ShowStatus("Screenshot & OCR (left-to-right)", "Text captured from screenshot using left-to-right reading order.", InfoBarSeverity.Success);
			else
				ShowStatus("Screenshot & OCR (left-to-right)", result.Message, InfoBarSeverity.Error);
		}
		catch (Exception ex)
		{
			ShowStatus("Screenshot & OCR (left-to-right)", ex.Message, InfoBarSeverity.Error);
			await ShowErrorDialog("Screenshot + OCR (LRTB) Error", ex);
		}
	}

	private async void BtnOcrClipboard_Click(object sender, RoutedEventArgs e)
	{
		try
		{
			if (!await EnsureOcrConfiguredAsync()) return;
			var result = await _ocrManager.ExtractTextFromClipboardImageAsync(OcrReadingOrder.TopToBottomColumnAware);
			SetOcrText(result.Message);
			HotkeyManager.SendHotkeyAfterDelay(_settings.AzureComputerVisionSettings?.SendHotkeyAfterOcrOperation ?? string.Empty, result.Success ? Constants.SendHotkeyDelay : Constants.FailureSendHotkeyDelay);
			if (result.Success)
				ShowStatus("OCR", "Clipboard image converted to text.", InfoBarSeverity.Success);
			else
				ShowStatus("OCR", result.Message, InfoBarSeverity.Warning);
		}
		catch (Exception ex)
		{
			ShowStatus("OCR", ex.Message, InfoBarSeverity.Error);
			await ShowErrorDialog("OCR Clipboard Error", ex);
		}
	}

	private async void BtnOcrDocuments_Click(object sender, RoutedEventArgs e)
	{
		try
		{
			if (!await EnsureOcrConfiguredAsync()) return;
			var picker = new FileOpenPicker
			{
				SuggestedStartLocation = PickerLocationId.DocumentsLibrary,
				ViewMode = PickerViewMode.List
			};
                        foreach (string extension in OcrManager.SupportedFileExtensions)
                        {
                                picker.FileTypeFilter.Add(extension);
                        }

			InitializeWithWindow.Initialize(picker, WindowNative.GetWindowHandle(this));
			IReadOnlyList<StorageFile>? files = await picker.PickMultipleFilesAsync();
			if (files == null || files.Count == 0)
				return;

                        BtnOcrDocuments.IsEnabled = false;
                        ShowStatus("OCR documents", $"Processing {files.Count} document(s)...", InfoBarSeverity.Informational);

                        OcrDocumentsProgressBar.Value = 0;
                        OcrDocumentsProgressBar.Maximum = 1;
                        OcrDocumentsProgressPanel.Visibility = Visibility.Visible;
                        OcrDocumentsProgressLabel.Text = "Preparing documents...";

                        var paths = files.Select(file => file.Path).ToList();
                        var progress = new Progress<OcrProcessingProgress>(info =>
                        {
                                OcrDocumentsProgressPanel.Visibility = Visibility.Visible;
                                OcrDocumentsProgressBar.Maximum = Math.Max(1, info.TotalSegments);
                                OcrDocumentsProgressBar.Value = info.ProcessedSegments;
                                OcrDocumentsProgressLabel.Text = $"{info.FileName} (Page {info.PageNumber} of {info.TotalPagesForFile})";
                        });
                        var result = await _ocrManager.ExtractTextFromFilesAsync(paths, OcrReadingOrder.TopToBottomColumnAware, CancellationToken.None, progress);
			SetOcrText(result.Text);

			if (result.SuccessCount == 0)
			{
				string failureDetails = result.Failures.Count > 0 ? string.Join("\n", result.Failures) : "Unable to extract text from the selected documents.";
				ShowStatus("OCR documents", failureDetails, InfoBarSeverity.Error);
			}
			else if (result.Success)
			{
				ShowStatus("OCR documents", $"Processed {result.SuccessCount} document(s). Results copied to the clipboard.", InfoBarSeverity.Success);
			}
			else
			{
				string failureSummary = BuildFailureSummary(result.Failures);
				string message = string.IsNullOrWhiteSpace(failureSummary)
					? $"Processed {result.SuccessCount} of {result.TotalCount} document(s)."
					: $"Processed {result.SuccessCount} of {result.TotalCount} document(s). Issues: {failureSummary}";
				ShowStatus("OCR documents", message, InfoBarSeverity.Warning);
			}
		}
		catch (Exception ex)
		{
			ShowStatus("OCR documents", ex.Message, InfoBarSeverity.Error);
			await ShowErrorDialog("OCR Documents Error", ex);
		}
                finally
                {
                        BtnOcrDocuments.IsEnabled = true;
                        OcrDocumentsProgressPanel.Visibility = Visibility.Collapsed;
                        OcrDocumentsProgressBar.Value = 0;
                        OcrDocumentsProgressBar.Maximum = 1;
                        OcrDocumentsProgressLabel.Text = string.Empty;
                }
        }

        private async void BtnDownloadOcrResults_Click(object sender, RoutedEventArgs e)
        {
                string text = TxtOcr.Text;
                if (string.IsNullOrWhiteSpace(text))
                {
                        ShowStatus("OCR documents", "No OCR results available to download.", InfoBarSeverity.Warning);
                        return;
                }

                try
                {
                        var picker = new FileSavePicker
                        {
                                SuggestedStartLocation = PickerLocationId.DocumentsLibrary,
                                SuggestedFileName = $"ocr-results-{DateTime.Now:yyyyMMdd-HHmmss}"
                        };
                        picker.FileTypeChoices.Add("Text Document", new List<string> { ".txt" });

                        InitializeWithWindow.Initialize(picker, WindowNative.GetWindowHandle(this));
                        StorageFile? file = await picker.PickSaveFileAsync();
                        if (file is null)
                                return;

                        await FileIO.WriteTextAsync(file, text);
                        ShowStatus("OCR documents", $"Saved OCR results to {file.Name}.", InfoBarSeverity.Success);
                }
                catch (Exception ex)
                {
                        ShowStatus("OCR documents", ex.Message, InfoBarSeverity.Error);
                        await ShowErrorDialog("Save OCR Result Error", ex);
                }
        }

	public async void BtnSpeechToText_Click(object? sender, RoutedEventArgs? e)
	{
		try
		{
			await StartStopSpeechToTextAsync(false);
		}
		catch (Exception ex)
		{
			ShowStatus("Speech to Text", ex.Message, InfoBarSeverity.Error);
			await ShowErrorDialog("Speech to Text Error", ex);
		}
	}

	public async void BtnSpeechToTextWithFormat_Click(object? sender, RoutedEventArgs? e)
	{
		try
		{
			await StartStopSpeechToTextAsync(true);
		}
		catch (Exception ex)
		{
			ShowStatus("Speech to Text", ex.Message, InfoBarSeverity.Error);
			await ShowErrorDialog("Speech to Text Error", ex);
		}
	}

        private async void BtnPlayLatestRecording_Click(object? sender, RoutedEventArgs? e)
        {
            await _audioSessionManager.PlaySelectedSessionAsync();
        }

        private async void BtnSessionNewer_Click(object? sender, RoutedEventArgs? e)
        {
            await _audioSessionManager.NavigateSessionsAsync(-1);
        }

        private async void BtnSessionOlder_Click(object? sender, RoutedEventArgs? e)
        {
            await _audioSessionManager.NavigateSessionsAsync(1);
        }

        private async void BtnRetrySpeechToText_Click(object? sender, RoutedEventArgs? e)
        {
            if (_activeSpeechService == null)
            {
                ShowStatus("Speech to Text", "Select a speech-to-text service to retry.", InfoBarSeverity.Warning);
                return;
            }
            await _audioSessionManager.RetryTranscriptionAsync(_activeSpeechService, GetActivePrompt(), _shutdownCts.Token);
        }

        private async void BtnUploadSpeechAudio_Click(object? sender, RoutedEventArgs? e)
        {
            if (_activeSpeechService == null)
            {
                ShowStatus("Speech to Text", "Select a speech-to-text service to transcribe audio.", InfoBarSeverity.Warning);
                return;
            }

            var picker = new FileOpenPicker
            {
                SuggestedStartLocation = PickerLocationId.MusicLibrary,
                ViewMode = PickerViewMode.List
            };
            picker.FileTypeFilter.Add(".mp3");
            picker.FileTypeFilter.Add(".wav");
            picker.FileTypeFilter.Add(".m4a");
            picker.FileTypeFilter.Add(".aac");
            picker.FileTypeFilter.Add(".flac");
            picker.FileTypeFilter.Add(".ogg");
            picker.FileTypeFilter.Add(".opus");
            picker.FileTypeFilter.Add(".wma");
            picker.FileTypeFilter.Add(".webm");
            picker.FileTypeFilter.Add(".mp4");
            picker.FileTypeFilter.Add(".avi");
            picker.FileTypeFilter.Add(".mkv");
            picker.FileTypeFilter.Add(".mov");
            picker.FileTypeFilter.Add(".wmv");
            picker.FileTypeFilter.Add(".m4v");

            InitializeWithWindow.Initialize(picker, WindowNative.GetWindowHandle(this));
            StorageFile? file = await picker.PickSingleFileAsync();
            if (file is null)
                return;

            await _audioSessionManager.ImportAudioAsync(file, _activeSpeechService, GetActivePrompt(), _shutdownCts.Token);
        }

	private async void BtnOcrClipboardLrtb_Click(object sender, RoutedEventArgs e)
	{
		try
		{
			if (!await EnsureOcrConfiguredAsync()) return;
			var result = await _ocrManager.ExtractTextFromClipboardImageAsync(OcrReadingOrder.LeftToRightTopToBottom);
			SetOcrText(result.Message);
			HotkeyManager.SendHotkeyAfterDelay(_settings.AzureComputerVisionSettings?.SendHotkeyAfterOcrOperation ?? string.Empty, result.Success ? Constants.SendHotkeyDelay : Constants.FailureSendHotkeyDelay);
			if (result.Success)
				ShowStatus("OCR (left-to-right)", "Clipboard image converted using left-to-right reading order.", InfoBarSeverity.Success);
			else
				ShowStatus("OCR (left-to-right)", result.Message, InfoBarSeverity.Warning);
		}
		catch (Exception ex)
		{
			ShowStatus("OCR (left-to-right)", ex.Message, InfoBarSeverity.Error);
			await ShowErrorDialog("OCR Clipboard (LRTB) Error", ex);
		}
	}

	public async Task StartStopSpeechToTextAsync(bool useLlmProcessing = false)
	{
		try
		{
            if (_activeSpeechService == null)
            {
				var dlg = new ContentDialog
				{
					Title = "Warning",
					Content = new TextBlock { Text = "No speech-to-text service selected.", TextWrapping = TextWrapping.Wrap },
					CloseButtonText = "OK",
					XamlRoot = this.Content.XamlRoot
				};
				AutomationProperties.SetName(dlg, "Warning");
				AutomationProperties.SetHelpText(dlg, "No speech-to-text service selected.");
				ShowStatus("Speech to Text", "Select a speech-to-text service to begin.", InfoBarSeverity.Warning);
				await ShowDialogAsync(dlg);
				return;
            }
            
            LlmSettings.LlmPrompt? autoRunPrompt = _promptLibrary?.GetAutoRunPrompt();
            await _audioSessionManager.StartStopRecordingAsync(_activeSpeechService, useLlmProcessing, GetActivePrompt(), autoRunPrompt, _shutdownCts.Token);
		}
		catch (Exception ex)
		{
			ShowStatus("Speech to Text", ex.Message, InfoBarSeverity.Error);
			await ShowErrorDialog("Speech to Text Error", ex);
		}
	}

	private async void ShowMessage(string title, string message)
	{
		var dialog = new ContentDialog
		{
			Title = title,
			Content = new TextBlock { Text = message, TextWrapping = Microsoft.UI.Xaml.TextWrapping.Wrap },
			CloseButtonText = "OK",
			XamlRoot = this.Content.XamlRoot // important in WinUI 3
		};
		AutomationProperties.SetName(dialog, title);
		AutomationProperties.SetHelpText(dialog, message);

		await ShowDialogAsync(dialog);
	}

	public async void BtnTextToSpeech_Click(object? sender, RoutedEventArgs? e)
	{
		var tts = _settings.TextToSpeechSettings ?? new TextToSpeechSettings();
		var (kind, clipboardText) = await _clipboard.InspectAsync();
		string trimmed = (clipboardText ?? string.Empty).Trim();

		if (_textToSpeech.IsSpeaking)
		{
			string? wasSpeaking = _textToSpeech.CurrentText;
			_textToSpeech.Stop();

			bool clipboardChanged = kind == ClipboardKind.Text
				&& trimmed.Length > 0
				&& !string.Equals(trimmed, wasSpeaking, StringComparison.Ordinal);

			if (clipboardChanged)
			{
				_textToSpeech.Speak(trimmed, tts.Rate, tts.Volume, tts.VoiceName,
					resumeIfSame: false, preprocess: tts.EnableSpeechPreprocessing,
					options: SpeechPreprocessingOptions.FromSettings(tts));
				ShowStatus("Text to Speech", "Speaking new clipboard text.", InfoBarSeverity.Informational);
			}
			else
			{
				ShowStatus("Text to Speech", "Stopped.", InfoBarSeverity.Informational);
			}
			return;
		}

		if (kind == ClipboardKind.Text && trimmed.Length > 0)
		{
			_textToSpeech.Speak(trimmed, tts.Rate, tts.Volume, tts.VoiceName,
				resumeIfSame: true, preprocess: tts.EnableSpeechPreprocessing,
				resumeRewindWordCount: tts.ResumeRewindWordCount,
				options: SpeechPreprocessingOptions.FromSettings(tts));
			ShowStatus("Text to Speech", "Speaking…", InfoBarSeverity.Informational);
			return;
		}

		AnnounceUnreadableClipboard(kind, tts);
	}

	public async void BtnRestartTts_Click(object? sender, RoutedEventArgs? e)
	{
		await ReadClipboardFreshAsync();
	}

	public void BtnSkipSentenceBack_Click(object? sender, RoutedEventArgs? e)
	{
		var tts = _settings.TextToSpeechSettings ?? new TextToSpeechSettings();
		_textToSpeech.SkipSentence(-1, tts.Rate, tts.Volume, tts.VoiceName, tts.SkipSentenceGraceWindowMs);
	}

	public void BtnSkipSentenceForward_Click(object? sender, RoutedEventArgs? e)
	{
		var tts = _settings.TextToSpeechSettings ?? new TextToSpeechSettings();
		_textToSpeech.SkipSentence(1, tts.Rate, tts.Volume, tts.VoiceName, tts.SkipSentenceGraceWindowMs);
	}

	public void BtnSpeakPosition_Click(object? sender, RoutedEventArgs? e)
	{
		var tts = _settings.TextToSpeechSettings ?? new TextToSpeechSettings();
		ReadingPosition position = _textToSpeech.GetReadingPosition();
		string announcement = ReadingAnnouncements.Position(position);
		_textToSpeech.SpeakAnnouncement(announcement, tts.Rate, tts.Volume, tts.VoiceName);
		ShowStatus("Text to Speech", announcement, InfoBarSeverity.Informational);
	}

	// Toggle pause/resume on its own hotkey, distinct from Stop. While speaking, freeze the
	// read in place (the service speaks a brief "Paused" cue). While paused, resume it. When
	// nothing is playing, announce that there is nothing to resume — this never starts a fresh
	// read; that remains the job of the speak hotkey.
	public void BtnPauseResume_Click(object? sender, RoutedEventArgs? e)
	{
		var tts = _settings.TextToSpeechSettings ?? new TextToSpeechSettings();
		if (_textToSpeech.IsPaused)
		{
			_textToSpeech.Resume(tts.Rate, tts.Volume, tts.VoiceName,
				tts.ResumeRewindWordCount, tts.ResumeRewindAfterPauseSeconds);
			ShowStatus("Text to Speech", "Resuming.", InfoBarSeverity.Informational);
		}
		else if (_textToSpeech.IsSpeaking)
		{
			_textToSpeech.Pause(tts.Rate, tts.Volume, tts.VoiceName);
			ShowStatus("Text to Speech", "Paused.", InfoBarSeverity.Informational);
		}
		else
		{
			_textToSpeech.SpeakAnnouncement("Nothing to resume.", tts.Rate, tts.Volume, tts.VoiceName);
			ShowStatus("Text to Speech", "Nothing to resume.", InfoBarSeverity.Informational);
		}
	}

	public async void BtnSpeakToFile_Click(object? sender, RoutedEventArgs? e)
	{
		var tts = _settings.TextToSpeechSettings ?? new TextToSpeechSettings();

		var (kind, clipboardText) = await _clipboard.InspectAsync();
		if (kind != ClipboardKind.Text)
		{
			AnnounceUnreadableClipboard(kind, tts);
			return;
		}

		if (!TtsFileExport.TryResolveExportText(clipboardText, out string text, out string resolveError))
		{
			_textToSpeech.SpeakAnnouncement(resolveError, tts.Rate, tts.Volume, tts.VoiceName);
			BeepPlayer.Play(BeepType.Failure);
			ShowStatus("Speak to file", resolveError, InfoBarSeverity.Warning);
			return;
		}

		StorageFile? file;
		try
		{
			var picker = new FileSavePicker
			{
				SuggestedStartLocation = PickerLocationId.MusicLibrary,
				SuggestedFileName = TtsFileExport.BuildSuggestedFileName(DateTime.Now)
			};
			picker.FileTypeChoices.Add("Audio (WAV)", new List<string> { ".wav" });

			InitializeWithWindow.Initialize(picker, WindowNative.GetWindowHandle(this));
			file = await picker.PickSaveFileAsync();
		}
		catch (Exception ex)
		{
			ShowStatus("Speak to file", ex.Message, InfoBarSeverity.Error);
			await ShowErrorDialog("Speak to File Error", ex);
			return;
		}

		if (file is null)
			return; // user cancelled the picker

		BtnSpeakToFile.IsEnabled = false;
		ShowStatus("Speak to file", $"Saving audio to {file.Name}…", InfoBarSeverity.Informational);
		try
		{
			// Synthesize off the UI thread so a long article does not freeze the window.
			// A dedicated exporter writes to the file, leaving any live read untouched.
			await Task.Run(() => _wavFileSpeechExporter.ExportToWavFile(
				text, file.Path, tts.Rate, tts.Volume, tts.VoiceName, tts.EnableSpeechPreprocessing,
				SpeechPreprocessingOptions.FromSettings(tts)));
			ShowStatus("Speak to file", $"Saved audio to {file.Name}.", InfoBarSeverity.Success);
		}
		catch (Exception ex)
		{
			ShowStatus("Speak to file", ex.Message, InfoBarSeverity.Error);
			await ShowErrorDialog("Speak to File Error", ex);
		}
		finally
		{
			BtnSpeakToFile.IsEnabled = true;
		}
	}

	public async void SpeakActiveSelectionAsync()
	{
		var tts = _settings.TextToSpeechSettings ?? new TextToSpeechSettings();

		var ownHwnd = WindowNative.GetWindowHandle(this);
		if (ownHwnd != IntPtr.Zero && GetForegroundWindow() == ownHwnd)
		{
			AnnounceSelectionUnavailable(
				"Read selection only works when another application is active with text selected.",
				tts);
			return;
		}

		// Save whatever the user has on the clipboard before Ctrl+C destroys it.
		ClipboardSnapshot? saved = null;
		try { saved = await _clipboard.TryCaptureSnapshotAsync(); }
		catch { /* snapshot is best-effort; reading the selection still works without it */ }

		uint before = GetClipboardSequenceNumber();
		try { await Task.Run(() => HotkeyManager.SendHotkey("Ctrl+C")); }
		catch { /* sending may fail if focus is on a non-input control */ }

		const int timeoutMs = 2000;
		const int pollMs = 30;
		int elapsed = 0;
		while (GetClipboardSequenceNumber() == before && elapsed < timeoutMs)
		{
			await Task.Delay(pollMs);
			elapsed += pollMs;
		}

		if (GetClipboardSequenceNumber() == before)
		{
			AnnounceSelectionUnavailable(
				"No text was selected, or the active application did not copy anything.",
				tts);
			return;
		}

		// Read the copied selection now, so the clipboard can be restored
		// before speaking rather than after.
		var (kind, selectionText) = await _clipboard.InspectAsync();
		string trimmed = (selectionText ?? string.Empty).Trim();

		if (saved is not null)
		{
			bool restored = false;
			try { restored = await _clipboard.TryRestoreSnapshotAsync(saved); }
			catch { /* fall through to the failure announcement */ }

			if (!restored)
			{
				BeepPlayer.Play(BeepType.Failure);
				ShowStatus("Text to Speech",
					"Could not restore your previous clipboard content; the clipboard now holds the copied selection.",
					InfoBarSeverity.Warning);
			}
		}

		if (kind != ClipboardKind.Text || trimmed.Length == 0)
		{
			AnnounceUnreadableClipboard(kind, tts);
			return;
		}

		if (_textToSpeech.IsSpeaking)
			_textToSpeech.Stop();

		_textToSpeech.Speak(trimmed, tts.Rate, tts.Volume, tts.VoiceName,
			resumeIfSame: false, preprocess: tts.EnableSpeechPreprocessing,
			options: SpeechPreprocessingOptions.FromSettings(tts));
		ShowStatus("Text to Speech", "Speaking…", InfoBarSeverity.Informational);
	}

	private void AnnounceSelectionUnavailable(string message, TextToSpeechSettings tts)
	{
		_textToSpeech.SpeakAnnouncement(message, tts.Rate, tts.Volume, tts.VoiceName);
		BeepPlayer.Play(BeepType.Failure);
		ShowStatus("Text to Speech", message, InfoBarSeverity.Warning);
	}

	public void BtnSpeakSelection_Click(object? sender, RoutedEventArgs? e) => SpeakActiveSelectionAsync();

	private async Task ReadClipboardFreshAsync()
	{
		var tts = _settings.TextToSpeechSettings ?? new TextToSpeechSettings();
		var (kind, clipboardText) = await _clipboard.InspectAsync();
		string trimmed = (clipboardText ?? string.Empty).Trim();

		if (kind == ClipboardKind.Text && trimmed.Length > 0)
		{
			_textToSpeech.Speak(trimmed, tts.Rate, tts.Volume, tts.VoiceName,
				resumeIfSame: false, preprocess: tts.EnableSpeechPreprocessing,
				options: SpeechPreprocessingOptions.FromSettings(tts));
			ShowStatus("Text to Speech", "Speaking…", InfoBarSeverity.Informational);
			return;
		}

		AnnounceUnreadableClipboard(kind, tts);
	}

	private void AnnounceUnreadableClipboard(ClipboardKind kind, TextToSpeechSettings tts)
	{
		string message = kind switch
		{
			ClipboardKind.Image => "The clipboard contains an image, not text. Use OCR to extract text first.",
			ClipboardKind.Unsupported => "The clipboard does not contain readable text.",
			ClipboardKind.Unavailable => "The clipboard is in use by another application. Try again in a moment.",
			_ => "No text on the clipboard.",
		};

		_textToSpeech.SpeakAnnouncement(message, tts.Rate, tts.Volume, tts.VoiceName);
		BeepPlayer.Play(BeepType.Failure);
		ShowStatus("Text to Speech", message, InfoBarSeverity.Warning);
	}

	private void InitializeTextToSpeechControls()
	{
		var settings = _settings.TextToSpeechSettings ?? new TextToSpeechSettings();

		var voiceItems = new List<string> { DefaultVoiceLabel };
		try { voiceItems.AddRange(_textToSpeech.GetVoiceNames()); }
		catch { /* leave default-only list if voice enumeration fails */ }

		CmbTtsVoice.ItemsSource = voiceItems;
		string? configuredVoice = settings.VoiceName;
		string selectedVoice = !string.IsNullOrWhiteSpace(configuredVoice) && voiceItems.Contains(configuredVoice)
			? configuredVoice!
			: DefaultVoiceLabel;
		CmbTtsVoice.SelectedItem = selectedVoice;

		int rate = settings.Rate;
		if (rate < -10) rate = -10;
		else if (rate > 10) rate = 10;
		SldTtsRate.Value = rate;

		int volume = settings.Volume;
		if (volume < 0) volume = 0;
		else if (volume > 100) volume = 100;
		SldTtsVolume.Value = volume;

		ToggleAnnounceReadingTime.IsOn = settings.AnnounceReadingTimeAtStart;
		ToggleAnnounceProgress.IsOn = settings.AnnounceProgressEnabled;

		_ttsControlsReady = true;
		PushTtsAnnouncementOptions();
	}

	// Populates the playback-speed dropdown and selects the saved speed, applying
	// it to the player so the first playback honours the persisted preference.
	private void InitializePlaybackSpeedControl()
	{
		var speeds = PlaybackSpeedOptions.Speeds;
		CmbPlaybackSpeed.ItemsSource = speeds.Select(FormatPlaybackSpeed).ToList();

		double saved = _settings.AudioSettings?.PlaybackSpeed ?? PlaybackSpeedOptions.Default;
		double normalized = PlaybackSpeedOptions.Normalize(saved);
		int index = speeds.ToList().IndexOf(normalized);
		if (index < 0)
			index = speeds.ToList().IndexOf(PlaybackSpeedOptions.Default);
		CmbPlaybackSpeed.SelectedIndex = index;

		_audioSessionManager.PlaybackSpeed = normalized;
		_playbackSpeedReady = true;
	}

	// Labels a speed value for the dropdown, e.g. "1.0 (Normal)", "1.5", "0.75".
	private static string FormatPlaybackSpeed(double speed)
	{
		string number = speed.ToString("0.0#", System.Globalization.CultureInfo.InvariantCulture);
		return speed == PlaybackSpeedOptions.Default ? $"{number} (Normal)" : number;
	}

	private void CmbPlaybackSpeed_SelectionChanged(object sender, SelectionChangedEventArgs e)
	{
		if (!_playbackSpeedReady) return;

		int index = CmbPlaybackSpeed.SelectedIndex;
		if (index < 0 || index >= PlaybackSpeedOptions.Speeds.Count) return;

		double speed = PlaybackSpeedOptions.Speeds[index];
		_settings.AudioSettings ??= new AudioSettings();
		if (_settings.AudioSettings.PlaybackSpeed == speed) return;

		_settings.AudioSettings.PlaybackSpeed = speed;
		_settingsManager.SaveSettingsToFile(_settings);

		// Apply live so a change while a recording is playing takes effect at once.
		_audioSessionManager.PlaybackSpeed = speed;
	}

	// Sets up the "Pin input level" toggle and the input-level slider on the main
	// window. On a device that doesn't support software level control, both are
	// disabled with an explanatory tooltip and nothing is written. Otherwise the
	// slider shows the pinned value (or the current hardware level), the toggle
	// reflects whether pinning is enabled, and any pinned level is re-asserted now
	// (app startup).
	private void InitializeMicrophoneLevelControls()
	{
		_micLevelControlsReady = false;

		bool supported = _micLevelPinService.IsLevelControlSupported;
		TglPinMicLevel.IsEnabled = supported;
		SldMicLevel.IsEnabled = supported;

		if (!supported)
		{
			TglPinMicLevel.IsOn = false;
			const string unsupported = "This microphone does not support software level control.";
			ToolTipService.SetToolTip(TglPinMicLevel, unsupported);
			ToolTipService.SetToolTip(SldMicLevel, unsupported);
			_micLevelInitialized = true;
			return;
		}

		int? pinned = _settings.AudioSettings?.PinnedCaptureLevel;
		TglPinMicLevel.IsOn = pinned.HasValue;
		SldMicLevel.Value = pinned ?? ReadCurrentMicLevelOrDefault();

		_micLevelControlsReady = true;
		_micLevelInitialized = true;

		// Re-assert now (app startup, or after a mic change): correct the level if
		// another app changed it. Route it through the shared off-thread write worker
		// rather than writing on the UI thread — the same guarantee the slider, pin
		// toggle, and record-start paths already have. On the mic-change path this
		// runs mid-session, where the write's failure path re-enumerates the device
		// and would otherwise briefly freeze the window and the screen reader.
		ReassertPinnedLevelOffThread(pinned);
	}

	// Nudges the pinned capture level back onto the active device through the shared
	// off-thread write coordinator, without blocking the caller. Used by the startup
	// and mic-change re-assert (InitializeMicrophoneLevelControls), where the write
	// must not run on the UI thread — its failure path re-enumerates the device,
	// which would freeze the UI and, with it, the screen reader. Routing through the
	// coordinator also serializes this write with the slider and pin-toggle writes,
	// so two threads never touch the same COM level endpoint at once, and a burst
	// coalesces to the latest value. This is a fire-and-forget background correction:
	// the re-assert only nudges the level back to the pinned value, so the outcome is
	// not surfaced (matching the previous synchronous call, whose result was already
	// discarded). A null pin means pinning is disabled — nothing to write.
	private void ReassertPinnedLevelOffThread(int? pinnedLevel)
	{
		if (pinnedLevel is not int level)
			return;

		_ = _micLevelWriteCoordinator.RequestLatestAsync(level);
	}

	// Current Windows capture level as a 0–100 value, or a sensible default when it
	// cannot be read. Used to position the slider when no pinned value exists.
	private int ReadCurrentMicLevelOrDefault() =>
		_micLevelPinService.ReadCurrentLevel() ?? DefaultMicLevel;

	// Toggle pinning on/off. Turning it on captures the slider's current value as the
	// pinned target and applies it immediately; turning it off stops re-asserting but
	// leaves the live level where it is. Persists the change.
	private async void TglPinMicLevel_Toggled(object sender, RoutedEventArgs e)
	{
		if (!_micLevelControlsReady) return;

		_settings.AudioSettings ??= new AudioSettings();

		int? target = null;
		if (TglPinMicLevel.IsOn)
		{
			target = (int)Math.Round(SldMicLevel.Value);
			_settings.AudioSettings.PinnedCaptureLevel = target;
		}
		else
		{
			_settings.AudioSettings.PinnedCaptureLevel = null;
		}

		// Persist the pin state synchronously; it must be saved regardless of the async
		// write below.
		_settingsManager.SaveSettingsToFile(_settings);

		// Turning the pin on applies the current slider level through the shared
		// off-thread worker, so it neither blocks the UI nor overlaps a trailing slider
		// write on the same COM endpoint. Turning it off writes nothing.
		if (target is int level)
		{
			var result = await _micLevelWriteCoordinator.RequestLatestAsync(level);
			if (result is { } outcome)
				ReportIfLevelWriteFailed(outcome);
		}
	}

	// Dragging the slider sets the actual Windows capture level immediately (instant
	// feedback, even when not recording). When pinning is enabled, the same value
	// becomes the stored pinned target so it is re-asserted later.
	private async void SldMicLevel_ValueChanged(object sender, RangeBaseValueChangedEventArgs e)
	{
		if (!_micLevelControlsReady) return;

		int level = (int)Math.Round(e.NewValue);

		// Record the intent first, synchronously on the UI thread. The COM write below is
		// coalesced, so a tick that gets superseded never reaches the device — but its
		// value must still become the stored pin. Only the file write is debounced, so a
		// drag or held arrow key persists once it settles (issue #172).
		if (TglPinMicLevel.IsOn)
		{
			_settings.AudioSettings ??= new AudioSettings();
			if (_settings.AudioSettings.PinnedCaptureLevel != level)
			{
				_settings.AudioSettings.PinnedCaptureLevel = level;
				_settingsSaveDebouncer.Trigger();
			}
		}

		// Coalesce-to-latest off-thread write: a burst from a drag or held arrow key
		// collapses to its most recent value, so the write (and its failure-path device
		// re-enumeration) never runs on the UI thread. Superseded ticks return null and
		// are dropped, so only the write that reaches the device can raise a failure beep.
		var result = await _micLevelWriteCoordinator.RequestLatestAsync(level);
		if (result is { } outcome)
			ReportIfLevelWriteFailed(outcome);
	}

	// When a level write could not be applied and verified, warn the user with the
	// failure beep and a status message rather than leaving the slider showing a
	// value the hardware never accepted. Other outcomes (applied, unchanged, or an
	// unsupported hardware-fixed device) need no signal.
	private void ReportIfLevelWriteFailed(Mutation.Ui.Core.CaptureLevelResult result)
	{
		if (!result.Failed)
			return;

		BeepPlayer.Play(BeepType.Failure);
		ShowStatus("Microphone level",
			"Could not set the microphone level — the device may be busy or disconnected. Please try again.",
			InfoBarSeverity.Error);
	}

	// Mirror the on-screen toggle states from the current settings. Called after the
	// settings dialog closes so a change made there shows on the main window too.
	private void RefreshTtsAnnouncementToggles()
	{
		var settings = _settings.TextToSpeechSettings ?? new TextToSpeechSettings();
		bool wasReady = _ttsControlsReady;
		_ttsControlsReady = false;
		try
		{
			ToggleAnnounceReadingTime.IsOn = settings.AnnounceReadingTimeAtStart;
			ToggleAnnounceProgress.IsOn = settings.AnnounceProgressEnabled;
		}
		finally { _ttsControlsReady = wasReady; }
	}

	// Push the configurable announcement settings into the speech service so the next
	// read picks them up.
	private void PushTtsAnnouncementOptions()
	{
		var settings = _settings.TextToSpeechSettings ?? new TextToSpeechSettings();
		_textToSpeech.SetAnnouncementOptions(
			settings.AnnounceReadingTimeAtStart,
			settings.AnnounceReadingTimeMinimumMinutes,
			settings.AnnounceProgressEnabled,
			settings.AnnounceProgressEveryPercent,
			settings.AnnounceProgressMinimumMinutes);
	}

	private void ToggleAnnounceReadingTime_Toggled(object sender, RoutedEventArgs e)
	{
		if (!_ttsControlsReady) return;
		_settings.TextToSpeechSettings ??= new TextToSpeechSettings();
		_settings.TextToSpeechSettings.AnnounceReadingTimeAtStart = ToggleAnnounceReadingTime.IsOn;
		_settingsManager.SaveSettingsToFile(_settings);
		PushTtsAnnouncementOptions();
	}

	private void ToggleAnnounceProgress_Toggled(object sender, RoutedEventArgs e)
	{
		if (!_ttsControlsReady) return;
		_settings.TextToSpeechSettings ??= new TextToSpeechSettings();
		_settings.TextToSpeechSettings.AnnounceProgressEnabled = ToggleAnnounceProgress.IsOn;
		_settingsManager.SaveSettingsToFile(_settings);
		PushTtsAnnouncementOptions();
	}

	private void CmbTtsVoice_SelectionChanged(object sender, SelectionChangedEventArgs e)
	{
		if (!_ttsControlsReady) return;

		string? selected = CmbTtsVoice.SelectedItem as string;
		string? voiceName = string.IsNullOrEmpty(selected) || selected == DefaultVoiceLabel
			? null
			: selected;

		_settings.TextToSpeechSettings ??= new TextToSpeechSettings();
		if (string.Equals(_settings.TextToSpeechSettings.VoiceName, voiceName, StringComparison.Ordinal))
			return;

		_settings.TextToSpeechSettings.VoiceName = voiceName;
		_settingsManager.SaveSettingsToFile(_settings);

		string sampleSubject = voiceName ?? "system default voice";
		_textToSpeech.SpeakAnnouncement(
			$"Currently selected {sampleSubject}.",
			_settings.TextToSpeechSettings.Rate,
			_settings.TextToSpeechSettings.Volume,
			voiceName);
	}

	private void SldTtsRate_ValueChanged(object sender, RangeBaseValueChangedEventArgs e)
	{
		if (!_ttsControlsReady) return;

		int rate = (int)Math.Round(e.NewValue);
		_settings.TextToSpeechSettings ??= new TextToSpeechSettings();
		if (_settings.TextToSpeechSettings.Rate == rate) return;

		_settings.TextToSpeechSettings.Rate = rate;
		_settingsSaveDebouncer.Trigger();
	}

	private void SldTtsVolume_ValueChanged(object sender, RangeBaseValueChangedEventArgs e)
	{
		if (!_ttsControlsReady) return;

		int volume = (int)Math.Round(e.NewValue);
		_settings.TextToSpeechSettings ??= new TextToSpeechSettings();
		if (_settings.TextToSpeechSettings.Volume == volume) return;

		_settings.TextToSpeechSettings.Volume = volume;
		_settingsSaveDebouncer.Trigger();
	}

	public async void BtnFormatTranscript_Click(object? sender, RoutedEventArgs? e)
	{
		string raw = TxtRawTranscript.Text;
		string formatted = _transcriptFormatter.ApplyRules(raw, false);
		TxtFormatTranscript.Text = formatted;
		bool copied = await _clipboard.TrySetTextAsync(formatted);
		bool inserted = await TryInsertIntoActiveApplicationAsync(formatted, clipboardAvailable: copied);
		if (copied && inserted)
		{
			BeepPlayer.Play(BeepType.Success);
			ShowStatus("Formatting", "Transcript formatted and copied.", InfoBarSeverity.Success);
		}
		else
		{
			BeepPlayer.Play(BeepType.Failure);
			ShowStatus("Formatting",
				"The clipboard is in use by another application; the formatted transcript could not be copied. It is available in the Mutation window.",
				InfoBarSeverity.Error);
		}
	}

	public async void BtnProcessLlm_Click(object? sender, RoutedEventArgs? e)
	{
		try
		{
            // Use the prompt marked as AutoRun, or the first available one, or prompt user?
            // For now, let's use the AutoRun prompt if available.
            var prompt = _settings.LlmSettings?.Prompts.FirstOrDefault(p => p.AutoRun)
                         ?? _settings.LlmSettings?.Prompts.FirstOrDefault();

            if (prompt == null)
            {
                ShowStatus("Processing", "No prompts configured.", InfoBarSeverity.Warning);
                return;
            }

            // If triggered manually, maybe we want to run the specific prompt logic directly
            ExecutePrompt(prompt);
		}
		catch (Exception ex)
		{
			ErrorLogger.LogError("Process with LLM", ex);
			ShowStatus("Processing", ex.Message, InfoBarSeverity.Error);
			await ShowErrorDialog("Process with LLM Error", ex);
		}
	}

    private async void ExecutePrompt(LlmSettings.LlmPrompt prompt)
    {
        try
        {
             // Marshaling to UI thread if called from hotkey background thread
             if (!DispatcherQueue.HasThreadAccess)
             {
                 DispatcherQueue.TryEnqueue(() => ExecutePrompt(prompt));
                 return;
             }
        
			BeepPlayer.Play(BeepType.Start);
			TxtFormatTranscript.Text = "Processing...";
			string raw = await _clipboard.GetTextAsync();
			if (string.IsNullOrWhiteSpace(raw))
			{
				ShowStatus("Processing", "Clipboard is empty.", InfoBarSeverity.Warning);
				TxtFormatTranscript.Text = string.Empty;
				return;
			}

			string modelName = !string.IsNullOrWhiteSpace(prompt.ModelName) ? prompt.ModelName : LlmSettings.DefaultModel;
			string processed = await _transcriptFormatter.ProcessWithLlmAsync(raw, prompt.Content, modelName);

			TxtFormatTranscript.Text = processed;
			bool copied = await _clipboard.TrySetTextAsync(processed);
			bool inserted = await TryInsertIntoActiveApplicationAsync(processed, clipboardAvailable: copied);
			if (copied && inserted)
			{
				BeepPlayer.Play(BeepType.Success);
				ShowStatus("Processing", $"Applied prompt '{prompt.Name}' with the language model.", InfoBarSeverity.Success);
				HotkeyManager.SendHotkeyAfterDelay(_settings.SpeechToTextSettings?.SendHotkeyAfterTranscriptionOperation, Constants.SendHotkeyDelay);
			}
			else
			{
				BeepPlayer.Play(BeepType.Failure);
				ShowStatus("Processing",
					"The clipboard is in use by another application; the processed text could not be delivered. It is available in the Mutation window.",
					InfoBarSeverity.Error);
			}
        }
        catch (Exception ex)
        {
             ErrorLogger.LogError("Process with LLM", ex);
             ShowStatus("Processing Failed", ex.Message, InfoBarSeverity.Error);
             await ShowErrorDialog($"Error executing prompt '{prompt.Name}'", ex);
        }
    }

	private void UpdateMicrophoneToggleVisuals()
	{
		bool muted = _audioDeviceManager.IsMuted;
		string labelText = muted ? "Unmute microphone" : "Mute microphone";
		BtnToggleMicIcon.Glyph = MicOnGlyph;
		BtnToggleMicSlash.Visibility = muted ? Visibility.Visible : Visibility.Collapsed;
		AutomationProperties.SetName(BtnToggleMic, labelText);
		ConfigureButtonHotkey(BtnToggleMic, null, _settings.AudioSettings?.MicrophoneToggleMuteHotKey, labelText);
		MicStatusIcon.Glyph = MicOnGlyph;
		MicStatusIconSlash.Visibility = muted ? Visibility.Visible : Visibility.Collapsed;
		MicStatusIcon.Foreground = ResolveBrush(muted ? "TextFillColorSecondaryBrush" : "TextFillColorPrimaryBrush");
		ToolTipService.SetToolTip(MicStatusIcon, muted ? "Microphone muted" : "Microphone live");
		AutomationProperties.SetName(MicStatusIcon, muted ? "Microphone muted" : "Microphone live");
	}

	private static Brush ResolveBrush(string resourceKey)
	{
		if (Application.Current.Resources.TryGetValue(resourceKey, out var value) && value is Brush brush)
			return brush;

		// Fallback to a neutral gray if the requested resource isn't found. In WinUI 3 the Colors struct lives under Microsoft.UI.
		return Application.Current.Resources["TextFillColorSecondaryBrush"] as Brush
			?? new SolidColorBrush(Microsoft.UI.Colors.Gray);
	}

	private void UpdateSpeechButtonVisuals(string label, string glyph, bool isEnabled = true)
	{
		if (label == "Record")
		{
			// Idle state
			BtnSpeechToTextIcon.Glyph = RecordGlyph;
			BtnSpeechToText.IsEnabled = true;
			AutomationProperties.SetName(BtnSpeechToText, "Record");
			ConfigureButtonHotkey(BtnSpeechToText, null, _settings.SpeechToTextSettings?.SpeechToTextHotKey, "Record");

			BtnSpeechToTextWithFormatIcon.Glyph = MagicGlyph;
			BtnSpeechToTextWithFormat.IsEnabled = true;
			AutomationProperties.SetName(BtnSpeechToTextWithFormat, "Record and Format");
			ConfigureButtonHotkey(BtnSpeechToTextWithFormat, null, _settings.SpeechToTextSettings?.SpeechToTextWithLlmProcessingHotKey, "Record and Format");
		}
		else if (label == "Stop")
		{
			// Recording state
			BtnSpeechToTextIcon.Glyph = StopGlyph;
			BtnSpeechToText.IsEnabled = true;
			AutomationProperties.SetName(BtnSpeechToText, "Stop");
			ConfigureButtonHotkey(BtnSpeechToText, null, _settings.SpeechToTextSettings?.SpeechToTextHotKey, "Stop");

			BtnSpeechToTextWithFormatIcon.Glyph = StopGlyph;
			BtnSpeechToTextWithFormat.IsEnabled = true;
			AutomationProperties.SetName(BtnSpeechToTextWithFormat, "Stop and Format");
			ConfigureButtonHotkey(BtnSpeechToTextWithFormat, null, _settings.SpeechToTextSettings?.SpeechToTextWithLlmProcessingHotKey, "Stop and Format");
		}
		else
		{
			// Transcribing / Processing
			BtnSpeechToTextIcon.Glyph = glyph;
			BtnSpeechToText.IsEnabled = isEnabled;
			AutomationProperties.SetName(BtnSpeechToText, label);

			BtnSpeechToTextWithFormatIcon.Glyph = glyph;
			BtnSpeechToTextWithFormat.IsEnabled = isEnabled;
			AutomationProperties.SetName(BtnSpeechToTextWithFormat, label);
		}
	}

        private void UpdatePlaybackButtonVisuals(string automationName, string glyph)
        {
                BtnPlayLatestRecordingIcon.Glyph = glyph;
                string tooltip = automationName == "Play selected session"
                                  ? "Play the selected session"
                                  : "Stop playing the selected session";
                ToolTipService.SetToolTip(BtnPlayLatestRecording, tooltip);
                AutomationProperties.SetName(BtnPlayLatestRecording, automationName);
                AutomationProperties.SetHelpText(BtnPlayLatestRecording, tooltip);
        }

    private void UpdateSessionNavigationAvailability()
    {
        bool hasSessions = _audioSessionManager.SessionHistory.Count > 0;
        int index = _audioSessionManager.SelectedSession != null ? _audioSessionManager.SessionHistory.IndexOf(_audioSessionManager.SelectedSession) : -1;
        
        bool canMoveNewer = hasSessions && index > 0;
        bool canMoveOlder = hasSessions && index >= 0 && index < _audioSessionManager.SessionHistory.Count - 1;
        bool busy = _audioSessionManager.IsRecording || _audioSessionManager.IsTranscribing;

        BtnSessionNewer.IsEnabled = canMoveNewer && !busy;
        BtnSessionOlder.IsEnabled = canMoveOlder && !busy;

        string newerTooltip = canMoveNewer ? "Switch to a newer session" : "No newer sessions available";
        string olderTooltip = canMoveOlder ? "Switch to an older session" : "No older sessions available";
        ToolTipService.SetToolTip(BtnSessionNewer, newerTooltip);
        ToolTipService.SetToolTip(BtnSessionOlder, olderTooltip);
        AutomationProperties.SetHelpText(BtnSessionNewer, newerTooltip);
        AutomationProperties.SetHelpText(BtnSessionOlder, olderTooltip);
    }

    private void UpdateRecordingActionAvailability()
    {
        var session = _audioSessionManager.SelectedSession;
        bool hasRecording = session != null && File.Exists(session.FilePath);
        bool busy = _audioSessionManager.IsRecording || _audioSessionManager.IsTranscribing;
        bool isPlaying = _audioSessionManager.IsPlaying;

        BtnPlayLatestRecording.IsEnabled = isPlaying || (hasRecording && !busy);
        BtnRetrySpeechToText.IsEnabled = session != null && _activeSpeechService != null && !busy && !isPlaying;
        BtnUploadSpeechAudio.IsEnabled = !busy && !isPlaying;
        UpdateSessionNavigationAvailability();
    }

        private void ScheduleSessionCleanup()
        {
                var cleanupTask = _audioSessionManager.CleanupSessionsAsync();
                cleanupTask.ContinueWith(_ =>
                {
                        DispatcherQueue.TryEnqueue(() =>
                        {
                                _audioSessionManager.RefreshSessions(preferredSelection: _audioSessionManager.SelectedSession);
                                UpdateRecordingActionAvailability();
                        });
                }, TaskScheduler.Default);
        }

	private async void FinalizeTranscript(string rawText, string successMessage, string? formattedText = null)
	{
		string formatted = formattedText ?? _transcriptFormatter.ApplyRules(rawText, false);

		TxtRawTranscript.Text = rawText;
		TxtFormatTranscript.Text = formatted;

		bool copied = await _clipboard.TrySetTextAsync(formatted);
		bool inserted = await TryInsertIntoActiveApplicationAsync(formatted, clipboardAvailable: copied);

		if (copied && inserted)
		{
			BeepPlayer.Play(BeepType.Success);
			ShowStatus("Speech to Text", successMessage, InfoBarSeverity.Success);
			HotkeyManager.SendHotkeyAfterDelay(_settings.SpeechToTextSettings?.SendHotkeyAfterTranscriptionOperation, Constants.SendHotkeyDelay);
		}
		else
		{
			BeepPlayer.Play(BeepType.Failure);
			ShowStatus("Speech to Text",
				"The clipboard is in use by another application; the transcript could not be delivered. It is available in the Mutation window.",
				InfoBarSeverity.Error);
		}

		TxtRawTranscript.IsReadOnly = false;
		_suppressAutoActions = false;
		UpdateRecordingActionAvailability();
		ScheduleSessionCleanup();
	}

    private void AudioSessionManager_StateChanged(object? sender, EventArgs e)
    {
        DispatcherQueue.TryEnqueue(() =>
        {
            UpdateRecordingActionAvailability();
            if (_audioSessionManager.IsRecording)
            {
                UpdateSpeechButtonVisuals("Stop", StopGlyph);
                TxtRawTranscript.IsReadOnly = true;
                TxtRawTranscript.Text = "Recording...";
                BeepPlayer.Play(BeepType.Start);
            }
            else if (_audioSessionManager.IsTranscribing)
            {
                UpdateSpeechButtonVisuals("Transcribing...", ProcessingGlyph, false);
                TxtRawTranscript.IsReadOnly = true;
                TxtRawTranscript.Text = "Transcribing...";
            }
            else
            {
                UpdateSpeechButtonVisuals("Record", RecordGlyph);
                TxtRawTranscript.IsReadOnly = false;
            }
        });
    }

    private void AudioSessionManager_TranscriptReady(object? sender, TranscriptResult result)
    {
        DispatcherQueue.TryEnqueue(() =>
        {
            FinalizeTranscript(result.RawText, "Transcript ready.", result.FormattedText);
        });
    }

    private void AudioSessionManager_ErrorOccurred(object? sender, string message)
    {
        DispatcherQueue.TryEnqueue(async () =>
        {
            ShowStatus("Error", message, InfoBarSeverity.Error);
            await ShowErrorDialog("Error", new Exception(message));
            UpdateRecordingActionAvailability();
        });
    }

    private void AudioSessionManager_StatusMessage(object? sender, string message)
    {
        DispatcherQueue.TryEnqueue(() =>
        {
            ShowStatus("Status", message, InfoBarSeverity.Informational);
        });
    }

    private void AudioSessionManager_SelectedSessionChanged(object? sender, EventArgs e)
    {
        DispatcherQueue.TryEnqueue(() =>
        {
            UpdateSessionNavigationAvailability();
            UpdateRecordingActionAvailability();
        });
    }

    private void AudioSessionManager_PlaybackStarted(object? sender, EventArgs e)
    {
        DispatcherQueue.TryEnqueue(() =>
        {
            UpdatePlaybackButtonVisuals("Stop playing", StopGlyph);
            UpdateRecordingActionAvailability();
        });
    }

    private void AudioSessionManager_PlaybackStopped(object? sender, EventArgs e)
    {
        DispatcherQueue.TryEnqueue(() =>
        {
            UpdatePlaybackButtonVisuals("Play selected session", PlayGlyph);
            UpdateRecordingActionAvailability();
        });
    }

	private readonly Dictionary<Button, string> _buttonBaseNames = new();

	private void ConfigureButtonHotkey(Button button, TextBlock? hotkeyTextBlock, string? hotkey, string baseTooltip)
	{
		if (!_buttonBaseNames.TryGetValue(button, out var baseName))
		{
			baseName = AutomationProperties.GetName(button);
			if (string.IsNullOrWhiteSpace(baseName))
				baseName = baseTooltip;
			_buttonBaseNames[button] = baseName;
		}

		string composedName = string.IsNullOrWhiteSpace(hotkey)
			? baseName
			: $"{baseName}, {hotkey}";
		AutomationProperties.SetName(button, composedName);

		string tooltip = ComposeTooltip(baseTooltip, hotkey);
		ToolTipService.SetToolTip(button, tooltip);
		AutomationProperties.SetHelpText(button, tooltip);
		AutomationProperties.SetAcceleratorKey(button, string.IsNullOrWhiteSpace(hotkey) ? string.Empty : hotkey);
		UpdateHotkeyText(hotkeyTextBlock, hotkey);
	}

	private static void UpdateHotkeyText(TextBlock? hotkeyTextBlock, string? hotkey)
	{
		if (hotkeyTextBlock == null)
			return;

		if (string.IsNullOrWhiteSpace(hotkey))
		{
			hotkeyTextBlock.Visibility = Visibility.Collapsed;
		}
		else
		{
			hotkeyTextBlock.Text = $"Hotkey: {hotkey}";
			hotkeyTextBlock.Visibility = Visibility.Visible;
		}
	}

        private static string ComposeTooltip(string baseTooltip, string? hotkey) =>
                          string.IsNullOrWhiteSpace(hotkey) ? baseTooltip : $"{baseTooltip} (Hotkey: {hotkey})";



        private void ShowStatus(string title, string message, InfoBarSeverity severity)
        {
                void Update()
                {
			StatusInfoBar.Title = title;
			StatusInfoBar.Message = message;
			StatusInfoBar.Severity = severity;
			StatusInfoBar.IsOpen = true;
			AutomationProperties.SetName(StatusInfoBar, $"{title} status");
			AutomationProperties.SetHelpText(StatusInfoBar, message);
			AnnounceStatus(title, message, severity);
			_statusDismissTimer.Stop();
			_statusDismissTimer.Start();
		}

		if (DispatcherQueue.HasThreadAccess)
			Update();
		else
			DispatcherQueue.TryEnqueue(Update);
	}

	private void AnnounceStatus(string title, string message, InfoBarSeverity severity)
	{
		// WinUI announces the InfoBar only on its closed→open transition; the
		// bar stays open 6 s between updates, so raise an explicit UIA
		// notification for every status change (issue #164).
		string announcement = StatusAnnouncement.ComposeText(title, message);
		if (announcement.Length == 0)
			return;

		// CreatePeerForElement (not FromElement) so the announcement also
		// fires when no peer exists yet, e.g. the very first status.
		var peer = Microsoft.UI.Xaml.Automation.Peers.FrameworkElementAutomationPeer.CreatePeerForElement(StatusInfoBar);
		peer?.RaiseNotificationEvent(
			StatusAnnouncement.GetKind(severity),
			StatusAnnouncement.GetProcessing(severity),
			announcement,
			StatusAnnouncement.ActivityId);
	}

	private void StatusDismissTimer_Tick(object? sender, object e)
	{
		_statusDismissTimer.Stop();
		StatusInfoBar.IsOpen = false;
	}

	private void StatusInfoBar_CloseButtonClick(InfoBar sender, object args)
	{
		_statusDismissTimer.Stop();
		StatusInfoBar.IsOpen = false;
	}




	public async Task ShowErrorDialog(string title, Exception ex)
	{
		string message = $"An error occurred:\n{ex.Message}\n\n{ex}";
		var dialog = new ContentDialog
		{
			Title = title,
			Content = new TextBlock { Text = message, TextWrapping = Microsoft.UI.Xaml.TextWrapping.Wrap },
			CloseButtonText = "OK",
			XamlRoot = (this.Content as FrameworkElement)?.XamlRoot
		};
		AutomationProperties.SetName(dialog, title);
		AutomationProperties.SetHelpText(dialog, message);

		// If a dialog is already open this error is queued behind it. Beep now
		// so a screen-reader user hears the failure immediately rather than
		// waiting in silence until the queued dialog surfaces (issue #167).
		if (_dialogQueue.IsBusy)
			BeepPlayer.Play(BeepType.Failure);

		await ShowDialogAsync(dialog);
	}

    private async Task<ContentDialogResult> ShowDialogAsync(ContentDialog dialog)
    {
        // A dialog requested while another is open is announced now and queued;
        // the queue shows it when the current dialog closes rather than
        // dropping it (issue #167).
        if (_dialogQueue.IsBusy)
            AnnouncePendingDialog(dialog);

        try
        {
            return await _dialogQueue.EnqueueAsync(async () => await dialog.ShowAsync());
        }
        catch (Exception ex)
        {
            // Fallback safety if something else goes wrong with the dialog
            ShowStatus("Dialog Error", $"Failed to show dialog: {ex.Message}", InfoBarSeverity.Error);
            return ContentDialogResult.None;
        }
    }

    /// <summary>
    /// Raises a UIA notification for a dialog that is being queued behind an
    /// already-open one, so a screen-reader user learns about it immediately
    /// instead of only when it eventually appears. The dialog is not on screen
    /// yet, so the notification is raised on the always-present status bar
    /// (the same channel as <see cref="AnnounceStatus"/>).
    /// </summary>
    private void AnnouncePendingDialog(ContentDialog dialog)
    {
        string title = AutomationProperties.GetName(dialog);
        if (string.IsNullOrWhiteSpace(title))
            title = dialog.Title?.ToString() ?? string.Empty;
        string message = AutomationProperties.GetHelpText(dialog);

        string announcement = StatusAnnouncement.ComposeText(title, message);
        if (announcement.Length == 0)
            return;

        var peer = Microsoft.UI.Xaml.Automation.Peers.FrameworkElementAutomationPeer.CreatePeerForElement(StatusInfoBar);
        peer?.RaiseNotificationEvent(
            Microsoft.UI.Xaml.Automation.Peers.AutomationNotificationKind.Other,
            Microsoft.UI.Xaml.Automation.Peers.AutomationNotificationProcessing.ImportantMostRecent,
            announcement,
            StatusAnnouncement.ActivityId);
    }

	private void CmbMicrophone_SelectionChanged(object sender, SelectionChangedEventArgs e)
	{
		if (CmbMicrophone.SelectedItem is CoreAudio.MMDevice device)
		{
			_audioDeviceManager.SelectMicrophone(device);
			if (_settings.AudioSettings != null)
			{
				_settings.AudioSettings.ActiveCaptureDeviceFullName = GetDeviceFriendlyName(device);
				_settingsManager.SaveSettingsToFile(_settings);
			}
			_microphoneVisualization?.RestartCapture();

			// Re-sync the level controls to the newly-selected device (support and
			// current level may differ) and re-assert the pinned level on it.
			if (_micLevelInitialized)
				InitializeMicrophoneLevelControls();
		}
		else
		{
			_microphoneVisualization?.StopCapture();
		}
	}

	private void MicWaveToggle_Click(object sender, RoutedEventArgs e)
	{
		// The ToggleButton has already flipped IsChecked to the user's intended
		// state; drive the setting to match so the control stays authoritative.
		_microphoneVisualization?.SetEnabled(MicWaveToggle.IsChecked == true);
	}

	// Reflects the persisted visualization state on the in-place toggle. Setting
	// IsChecked programmatically does not raise Click, so this never re-persists.
	private void SyncMicWaveToggleState()
	{
		MicWaveToggle.IsChecked = _settings.AudioSettings?.EnableMicrophoneVisualization != false;
	}

	private string GetActivePrompt()
	{
		if (_activeSpeechService == null) return string.Empty;
		var serviceSettings = _settings.SpeechToTextSettings?.Services?.FirstOrDefault(s => s.Name == _activeSpeechService.ServiceName);
		return serviceSettings?.SpeechToTextPrompt ?? string.Empty;
	}

	private void CmbSpeechService_SelectionChanged(object sender, SelectionChangedEventArgs e)
	{
		if (CmbSpeechService.SelectedItem is ISpeechToTextService svc)
		{
			_activeSpeechService = svc;

			// Persist the selection immediately so it survives app restart
			if (_settings.SpeechToTextSettings != null &&
				_settings.SpeechToTextSettings.ActiveSpeechToTextService != svc.ServiceName)
			{
				_settings.SpeechToTextSettings.ActiveSpeechToTextService = svc.ServiceName;
				_settingsManager.SaveSettingsToFile(_settings);
			}

			var serviceSettings = _settings.SpeechToTextSettings?.Services?.FirstOrDefault(s => s.Name == svc.ServiceName);
			if (serviceSettings != null)
			{
				// Temporarily unsubscribe to avoid triggering the save logic
				TxtSpeechToTextPrompt.TextChanged -= TxtSpeechToTextPrompt_TextChanged;
				TxtSpeechToTextPrompt.Text = serviceSettings.SpeechToTextPrompt ?? string.Empty;
				TxtSpeechToTextPrompt.TextChanged += TxtSpeechToTextPrompt_TextChanged;
			}
		}
		UpdateRecordingActionAvailability();
	}

	private async void TxtSpeechToTextPrompt_TextChanged(object sender, TextChangedEventArgs e)
	{
		if (_activeSpeechService == null) return;

		var serviceSettings = _settings.SpeechToTextSettings?.Services?.FirstOrDefault(s => s.Name == _activeSpeechService.ServiceName);
		if (serviceSettings != null)
		{
			serviceSettings.SpeechToTextPrompt = TxtSpeechToTextPrompt.Text;

			_promptDebounceCts.Cancel();
			_promptDebounceCts = new CancellationTokenSource();
			var token = _promptDebounceCts.Token;
			try
			{
				await Task.Delay(1000, token);
				if (!token.IsCancellationRequested)
				{
					_settingsManager.SaveSettingsToFile(_settings);
				}
			}
			catch (TaskCanceledException) { }
		}
	}

	private void CmbInsertOption_SelectionChanged(object sender, SelectionChangedEventArgs e)
	{
		if (CmbInsertOption.SelectedItem is DictationInsertOption opt)
		{
			_insertOption = opt;
			UpdateThirdPartyExplanation(opt);
			var persistedValue = opt.ToString();
			if (_settings.MainWindowUiSettings != null && _settings.MainWindowUiSettings.DictationInsertPreference != persistedValue)
			{
				_settings.MainWindowUiSettings.DictationInsertPreference = persistedValue;
				_settingsManager.SaveSettingsToFile(_settings);
			}
		}
	}

	private void UpdateThirdPartyExplanation(DictationInsertOption option)
	{
		string explanation = option switch
		{
			DictationInsertOption.DoNotInsert => DoNotInsertExplanation,
			DictationInsertOption.SendKeys => SendKeysExplanation,
			DictationInsertOption.Paste => PasteExplanation,
			_ => string.Empty
		};

		ThirdPartyExplanationText.Text = explanation;
	}

	// Returns false only when a paste-mode insert could not proceed because the
	// clipboard stayed unavailable; every path that needs no insert returns true.
	private async Task<bool> TryInsertIntoActiveApplicationAsync(string text, bool clipboardAvailable = true)
	{
		if (string.IsNullOrWhiteSpace(text))
			return true;

		var windowHandle = WindowNative.GetWindowHandle(this);
		if (windowHandle != IntPtr.Zero)
		{
			var foregroundWindow = GetForegroundWindow();
			if (foregroundWindow == windowHandle)
				return true;
		}

		switch (_insertOption)
		{
			case DictationInsertOption.SendKeys:
				BeepPlayer.Play(BeepType.Start);
				// Off the UI thread: SendInput can stall on the foreground app
				// and must never block or pump messages mid-finalization.
				_ = Task.Run(() => HotkeyManager.SendText(text));
				return true;
			case DictationInsertOption.Paste:
				// Pasting sends Ctrl+V, so the text must actually be on the
				// clipboard; retry the write here if the earlier copy failed.
				if (!clipboardAvailable && !await _clipboard.TrySetTextAsync(text))
					return false;
				// "Ctrl+V" (not "^v"): Hotkey.Parse has no caret syntax, so the
				// literal would throw and drop to the SendKeys.SendWait fallback.
				_ = Task.Run(() => HotkeyManager.SendHotkey("Ctrl+V"));
				return true;
		}

		return true;
	}

	private async void TxtSpeechToText_TextChanged(object sender, TextChangedEventArgs e)
	{
		// Avoid auto actions during programmatic updates or while recording/transcribing
		if (_suppressAutoActions || TxtRawTranscript.IsReadOnly || _audioSessionManager.IsRecording || _audioSessionManager.IsTranscribing)
			return;

		_formatDebounceCts.Cancel();
		_formatDebounceCts = new CancellationTokenSource();
		var token = _formatDebounceCts.Token;
		try
		{
			await Task.Delay(300, token);
			if (!token.IsCancellationRequested)
			{
				string raw = TxtRawTranscript.Text;
				string formatted = _transcriptFormatter.ApplyRules(raw, false);
				TxtFormatTranscript.Text = formatted;
				// Intentionally do not call _clipboard.SetText or InsertIntoActiveApplication here.
				// Insertion/clipboard updates happen on transcription completion to avoid duplicates.
			}
		}
		catch (TaskCanceledException) { }
	}

	private static string BuildFailureSummary(IReadOnlyList<string> failures)
	{
		if (failures.Count == 0)
			return string.Empty;

		var sample = failures.Take(3).ToList();
		string summary = string.Join("; ", sample);
		if (failures.Count > sample.Count)
			summary += "; ...";

		return summary;
	}

	internal void SetOcrText(string message)
	{
		string safeMessage = message ?? string.Empty;
		TxtOcr.Text = safeMessage;
		if (BtnDownloadOcrResults is not null)
		{
			BtnDownloadOcrResults.IsEnabled = !string.IsNullOrWhiteSpace(safeMessage);
		}
	}

	// Guards an OCR action: returns true when Azure OCR is configured, otherwise warns
	// the user and opens Settings on the OCR tab, returning false so the caller skips
	// the attempt. Mirrors the speech-to-text missing-key flow, but routes to the OCR
	// tab because the Azure key and endpoint live there, not on the API keys tab.
	private async Task<bool> EnsureOcrConfiguredAsync()
	{
		if (_ocrManager.IsOcrConfigured(out string message))
			return true;

		await ShowOcrNotConfiguredWarningAsync(message);
		return false;
	}

	private async Task ShowOcrNotConfiguredWarningAsync(string detail)
	{
		const string title = "OCR Not Configured";
		string message =
			(string.IsNullOrWhiteSpace(detail) ? "Azure Computer Vision is not configured." : detail) +
			"\n\nThe Settings window will now open on the Screen capture & OCR tab so you can add the " +
			"Azure Computer Vision API key and endpoint.";

		if (Content is FrameworkElement rootElement && rootElement.XamlRoot is not null)
		{
			var dialog = new ContentDialog
			{
				Title = title,
				Content = new TextBlock { Text = message, TextWrapping = TextWrapping.Wrap },
				CloseButtonText = "Continue",
				XamlRoot = rootElement.XamlRoot,
				RequestedTheme = rootElement.ActualTheme
			};
			AutomationProperties.SetName(dialog, title);
			AutomationProperties.SetHelpText(dialog, message);

			await ShowDialogAsync(dialog);
		}
		else
		{
			System.Windows.Forms.MessageBox.Show(
				message,
				title,
				System.Windows.Forms.MessageBoxButtons.OK,
				System.Windows.Forms.MessageBoxIcon.Warning);
		}

		// Yield a dispatcher turn so the warning dialog finishes closing before the
		// Settings dialog opens (WinUI allows only one ContentDialog at a time).
		await YieldToDispatcherAsync();
		await ShowSettingsDialogAsync("ocr");
	}

	private void RootContent_KeyDown(object sender, KeyRoutedEventArgs e)
	{
		if (e.Handled)
			return;
		// VK_OEM_COMMA (0xBC) — no named VirtualKey enum member exists for ','
		if (e.Key != (VirtualKey)0xBC)
			return;

		var ctrlState = Microsoft.UI.Input.InputKeyboardSource
			.GetKeyStateForCurrentThread(VirtualKey.Control);
		if ((ctrlState & Windows.UI.Core.CoreVirtualKeyStates.Down) != Windows.UI.Core.CoreVirtualKeyStates.Down)
			return;

		e.Handled = true;
		SettingsMenuItem_Click(this, new RoutedEventArgs());
	}

	private async void SettingsMenuItem_Click(object sender, RoutedEventArgs e)
	{
		await ShowSettingsDialogAsync();
	}

	internal void ApplyLiveSettings()
	{
		try
		{
			_microphoneVisualization?.ApplyEnabledStateFromSettings();
			SyncMicWaveToggleState();
		}
		catch (Exception ex)
		{
			System.Diagnostics.Debug.WriteLine($"ApplyLiveSettings (mic viz) failed: {ex.Message}");
		}

		try
		{
			BeepPlayer.Initialize(_settings);
		}
		catch (Exception ex)
		{
			System.Diagnostics.Debug.WriteLine($"ApplyLiveSettings (beeps) failed: {ex.Message}");
		}

		try
		{
			RefreshTtsAnnouncementToggles();
			PushTtsAnnouncementOptions();
			InitializeHotkeyVisuals();
		}
		catch (Exception ex)
		{
			System.Diagnostics.Debug.WriteLine($"ApplyLiveSettings (tts) failed: {ex.Message}");
		}

		var hotkeyFailures = new List<HotkeyManager.HotkeyBindingFailure>();
		try
		{
			if (_hotkeyManager is not null)
			{
				_hotkeyManager.ClearAllForRebind();
				hotkeyFailures.AddRange(RegisterCoreHotkeys(_hotkeyManager));
				hotkeyFailures.AddRange(HotkeyManager.ToBindingFailures(_hotkeyManager.RegisterRouterHotkeys()));
				hotkeyFailures.AddRange(_hotkeyManager.RegisterPromptHotkeys(
					_settings.LlmSettings?.Prompts ?? Enumerable.Empty<LlmSettings.LlmPrompt>(),
					ExecutePrompt));
			}
		}
		catch (Exception ex)
		{
			System.Diagnostics.Debug.WriteLine($"ApplyLiveSettings (hotkeys) failed: {ex.Message}");
		}

		if (hotkeyFailures.Count > 0)
			_ = ShowHotkeyBindingFailuresAsync(hotkeyFailures);
	}

	// Surfaces hotkey binding failures (core, router, and prompt) the same way: a failure beep
	// plus an accessible dialog listing each unbound hotkey and why it could not be registered.
	internal async Task ShowHotkeyBindingFailuresAsync(IReadOnlyList<HotkeyManager.HotkeyBindingFailure> failures)
	{
		if (failures is null || failures.Count == 0)
			return;

		try { BeepPlayer.Play(BeepType.Failure); }
		catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"Hotkey failure beep failed: {ex.Message}"); }

		try
		{
			if (Content is not FrameworkElement rootElement || rootElement.XamlRoot is null)
				return;

			const string title = "Some hotkeys could not be registered";
			string message = HotkeyManager.BuildFailureMessage(failures);

			var dialog = new ContentDialog
			{
				Title = title,
				Content = new TextBlock
				{
					Text = message,
					TextWrapping = TextWrapping.Wrap,
				},
				CloseButtonText = "OK",
				XamlRoot = rootElement.XamlRoot,
				RequestedTheme = rootElement.ActualTheme,
			};
			Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(dialog, title);
			Microsoft.UI.Xaml.Automation.AutomationProperties.SetHelpText(dialog, message);

			await ShowDialogAsync(dialog);
		}
		catch (Exception ex)
		{
			System.Diagnostics.Debug.WriteLine($"ShowHotkeyBindingFailures failed: {ex.Message}");
		}
	}

	// Debug crash simulation handlers - for testing global exception handling
	private void DebugSimulateUiCrash_Click(object sender, RoutedEventArgs e)
	{
		throw new InvalidOperationException("Simulated UI thread crash for debugging purposes.");
	}

	private void DebugSimulateBackgroundCrash_Click(object sender, RoutedEventArgs e)
	{
		System.Threading.ThreadPool.QueueUserWorkItem(_ =>
		{
			throw new InvalidOperationException("Simulated background thread crash for debugging purposes.");
		});
	}

	private void DebugSimulateTaskCrash_Click(object sender, RoutedEventArgs e)
	{
	}

    private void BtnAddPrompt_Click(object sender, RoutedEventArgs e) =>
        _promptLibrary?.OpenAddDialog();

    private void BtnEditPrompt_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is LlmSettings.LlmPrompt prompt)
            _promptLibrary?.OpenEditDialog(prompt);
    }

    private async void BtnDeletePrompt_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button btn || btn.Tag is not LlmSettings.LlmPrompt prompt || _promptLibrary is null)
            return;

        string name = prompt.Name;
        string confirmation = PromptDeletionMessages.BuildConfirmation(name);

        var dialog = new ContentDialog
        {
            Title = PromptDeletionMessages.ConfirmationTitle,
            Content = new TextBlock { Text = confirmation, TextWrapping = TextWrapping.Wrap },
            PrimaryButtonText = "Delete",
            CloseButtonText = "Cancel",
            // Cancel is the safe default so a stray Enter never deletes a prompt.
            DefaultButton = ContentDialogButton.Close,
            XamlRoot = (this.Content as FrameworkElement)?.XamlRoot,
        };
        AutomationProperties.SetName(dialog, PromptDeletionMessages.ConfirmationTitle);
        AutomationProperties.SetHelpText(dialog, confirmation);

        var result = await ShowDialogAsync(dialog);
        if (result != ContentDialogResult.Primary)
        {
            // Covers an explicit Cancel or a dialog that failed to show: nothing
            // is deleted, and the outcome is announced for the screen reader.
            // (A confirmation requested while another dialog is open is now
            // queued and shown when that one closes, so deletion still requires
            // an explicit Delete click.)
            ShowStatus(PromptDeletionMessages.ConfirmationTitle, PromptDeletionMessages.BuildCancelled(name), InfoBarSeverity.Informational);
            return;
        }

        try
        {
            if (_promptLibrary.DeletePrompt(prompt))
            {
                ShowStatus(PromptDeletionMessages.ConfirmationTitle, PromptDeletionMessages.BuildDeleted(name), InfoBarSeverity.Success);
                BeepPlayer.Play(BeepType.Success);
            }
            else
            {
                // The prompt was already gone (removed elsewhere between the
                // click and the confirmation). Announce it rather than stay
                // silent after the user confirmed a deletion.
                ShowStatus(PromptDeletionMessages.ConfirmationTitle, PromptDeletionMessages.BuildFailed(name, "the prompt was no longer in the library"), InfoBarSeverity.Warning);
                BeepPlayer.Play(BeepType.Failure);
            }
        }
        catch (Exception ex)
        {
            // Persisting the removal can fail (e.g. the settings file cannot be
            // written). Fail loudly rather than let the async void swallow it.
            ShowStatus(PromptDeletionMessages.ConfirmationTitle, PromptDeletionMessages.BuildFailed(name, ex.Message), InfoBarSeverity.Error);
            BeepPlayer.Play(BeepType.Failure);
        }
    }

    private void BtnRunPrompt_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is LlmSettings.LlmPrompt prompt)
            ExecutePrompt(prompt);
    }

	public void Dispose()
	{
		_audioSessionManager?.Dispose();
		_microphoneVisualization?.Dispose();
		_formatDebounceCts?.Dispose();
		_promptDebounceCts?.Dispose();
		_settingsSaveDebouncer?.Dispose();
		_shutdownCts.Dispose();
		_statusDismissTimer?.Stop();
	}
}
