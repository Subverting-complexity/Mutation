using CognitiveSupport;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Mutation.Ui.Core;
using Mutation.Ui.Services;
using Mutation.Ui.Views;
using Mutation.Ui.Views.SettingsUi;
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
	private readonly Mutation.Ui.Services.FastModeNoticeTracker _fastModeNotices;
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
	// Runs the close steps in an order that cannot lose user data, and exposes the
	// completion signal App's shutdown handler waits on before ending the process.
	private readonly Mutation.Ui.Core.ApplicationCloseSequence _closeSequence;
	private readonly CancellationTokenSource _shutdownCts = new();
	// Cancellation lifetime of the batch OCR run, linked to shutdown so closing the window
	// stops it, and cancellable on its own from the Cancel button (issue #227).
	private readonly OcrDocumentsRunController _ocrDocumentsRun;
	// Cancellation lifetime of a prompt run started from the prompt library — its button
	// or its hotkey. Separate from the one AudioSessionManager owns for the dictation
	// flow, because the two are started by different keys and each cancels its own
	// (issue #256).
	private readonly Mutation.Ui.Core.LlmOperationState _promptLlmOperation = new();
	// Which prompt the in-flight run belongs to, so a cancel can name it.
	private string? _runningPromptName;
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
	// Set before the level probe awaits, so a mic change during startup re-enters the
	// setup instead of being skipped.
	private bool _micLevelInitialized;
	// Identifies the in-flight level probe. A mic change starts a newer one, and the
	// older result must be discarded rather than pairing one device's controls with
	// another device's level. UI thread only.
	private int _micLevelInitGeneration;
	// Set while the microphone combo is being rebuilt programmatically, so the
	// selection handler does not treat the resulting event as a user choice.
	private bool _suppressMicrophoneSelection;
	// Runs microphone switches — resolving the device over winmm and restarting
	// waveform capture — on a background worker, so a device that is slow to respond
	// cannot freeze the window and the screen reader with it (issue #267). Also the
	// ordering authority: the latest selection supersedes any older one, in flight or
	// queued, so the level controls can never end up describing a different device
	// than the one the user chose.
	private readonly Mutation.Ui.Core.MicrophoneSwitchCoordinator _microphoneSwitch;
	// The microphone the user has most recently asked for, which is not the same as
	// the one the device manager has settled on: the switch is applied on a background
	// worker and lags. Comparing a new selection against the manager's would let the
	// user pick B, change their mind back to the still-current-looking A, and be
	// silently skipped — leaving the combo and the live device permanently disagreeing.
	// UI thread only.
	private string? _requestedMicrophoneId;
	// Set when the constructor skips its inline capture start because restoring the
	// persisted microphone queued a switch. If that switch then fails, nothing is
	// capturing at all and the waveform would stay dead for the session — unlike a
	// mid-session failure, which leaves the previous device's capture running
	// untouched and must not be disturbed. Cleared by the first switch that lands.
	// UI thread only.
	private bool _startupCapturePending;
	// Set as soon as the window starts closing, so async continuations that resume
	// afterwards do not touch torn-down controls.
	private bool _isClosing;
	// Completes when the audio stack has been enumerated and the microphone combo has
	// been filled in from it — or when that failed and the user has been told. Never
	// faults: AdoptMicrophonesWhenReadyAsync reports its own trouble, and a waiter on the
	// dictation path must not be handed an exception instead of an answer.
	private readonly Task _audioDevicesReady;
	// True while a dictation start is parked waiting for the microphone to settle.
	// Nothing else disables the shortcut for that stretch — the session is neither
	// recording nor transcribing yet — so without this a second press would queue a
	// second start and the two would resume together (issue #312). UI thread only.
	private bool _dictationStartWaiting;
	// How long a dictation start waits for the microphone before giving up. A winmm call
	// already inside a wedged driver cannot be cancelled, so an unbounded wait would
	// leave every later press queued behind one that never completes — a hotkey that is
	// silently dead for the rest of the session.
	private static readonly TimeSpan MicrophoneReadyTimeout = TimeSpan.FromSeconds(8);
	private const string DetectingMicrophonesPlaceholder = "Finding microphones...";
	private const string SelectMicrophonePlaceholder = "Select a microphone";
	private const string NoMicrophonesPlaceholder = "No microphones available";
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
		Mutation.Ui.Core.MicrophoneLevelWriteCoordinator micLevelWriteCoordinator,
		Mutation.Ui.Services.FastModeNoticeTracker fastModeNotices)
	{
		_fastModeNotices = fastModeNotices;
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
		_ocrDocumentsRun = new OcrDocumentsRunController(_shutdownCts.Token);

        _settingsSaveDebouncer = new Debouncer(
            SettingsSaveDebounceDelay,
            () => _settingsManager.SaveSettingsToFile(_settings),
            onError: ReportSettingsSaveFailure);

        _audioSessionManager = audioSessionManager;
        _micLevelWriteCoordinator = micLevelWriteCoordinator;
        _muteToggleCoordinator = new Mutation.Ui.Core.MicrophoneMuteToggleCoordinator(_audioDeviceManager.ToggleMute);
        _audioSessionManager.StateChanged += AudioSessionManager_StateChanged;
        _audioSessionManager.TranscriptReady += AudioSessionManager_TranscriptReady;
        _audioSessionManager.ErrorOccurred += AudioSessionManager_ErrorOccurred;
        _audioSessionManager.StatusMessage += AudioSessionManager_StatusMessage;
        _audioSessionManager.SelectedSessionChanged += AudioSessionManager_SelectedSessionChanged;
        _audioSessionManager.PlaybackStarted += AudioSessionManager_PlaybackStarted;
        _audioSessionManager.PlaybackStopped += AudioSessionManager_PlaybackStopped;
        _textToSpeech.SpeakFailed += TextToSpeech_SpeakFailed;

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

        // Built before anything can select a microphone — restoring the persisted
        // choice below raises SelectionChanged, and that handler goes through here.
        _microphoneSwitch = new Mutation.Ui.Core.MicrophoneSwitchCoordinator(
            _audioDeviceManager.SelectMicrophoneById,
            _microphoneVisualization.RestartCapture,
            _microphoneVisualization.StopCapture,
            ex => ErrorLogger.LogError("Selecting the microphone failed", ex));

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

		UpdateMicrophoneToggleVisuals();
		UpdateSpeechButtonVisuals("Record", RecordGlyph);
		// The combo binds immutable descriptions, not live MMDevice wrappers: the device
		// list is re-enumerated on every hot-plug and on the mute and level retry paths,
		// and the superseded wrappers are disposed — so live devices held here go stale
		// and even the display binding ends up reading a dead COM proxy (issue #264).
		//
		// Bound empty here and filled in when the audio stack answers. The control itself
		// is in the tree, enabled and focusable, from the moment the window opens — what
		// used to hold the window back was the enumeration behind it, not the combo
		// (issue #308). Its placeholder says so, so a screen reader landing on it before
		// the devices arrive gets an explanation rather than an empty list.
		CmbMicrophone.ItemsSource = Array.Empty<Mutation.Ui.Core.CaptureDeviceInfo>();
		CmbMicrophone.DisplayMemberPath = nameof(Mutation.Ui.Core.CaptureDeviceInfo.FriendlyName);
		CmbMicrophone.PlaceholderText = DetectingMicrophonesPlaceholder;
		AutomationProperties.SetHelpText(CmbMicrophone, DetectingMicrophonesPlaceholder);

		_audioDeviceManager.CaptureDeviceListChanged += AudioDeviceManager_CaptureDeviceListChanged;

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
                failures => _ = ShowHotkeyBindingFailuresAsync(failures),
                _shutdownCts.Token);
            _promptLibrary.Initialize();

		var tooltipManager = new TooltipManager(_settings);
		tooltipManager.SetupTooltips(TxtRawTranscript, TxtFormatTranscript);

		// Bound to the described options rather than the bare enum, so the reader announces
		// "Paste into 3rd party application" instead of the identifier "Paste" (issue #243).
		var insertOptions = DictationInsertOptionItem.All();
		CmbInsertOption.ItemsSource = insertOptions;
		CmbInsertOption.DisplayMemberPath = nameof(DictationInsertOptionItem.Description);
		var persistedInsertPreference = _settings.MainWindowUiSettings?.DictationInsertPreference;
		if (!string.IsNullOrWhiteSpace(persistedInsertPreference) && Enum.TryParse(persistedInsertPreference, true, out DictationInsertOption persistedOption))
		{
			_insertOption = persistedOption;
		}
		else
		{
			_insertOption = DictationInsertOption.Paste;
		}
		CmbInsertOption.SelectedItem = insertOptions.FirstOrDefault(item => item.Option == _insertOption);
		UpdateThirdPartyExplanation(_insertOption);

		// Only from here on is a change to the explanation something the user did. Restoring
		// the saved preference is not news, and announcing it would talk over whatever the
		// screen reader says as the window opens.
		_announceThirdPartyExplanation = true;

		InitializeTextToSpeechControls();
		InitializePlaybackSpeedControl();
		InitializeHotkeyVisuals();

		// Everything that has to ask the audio hardware a question — the enumeration, the
		// OS default, the winmm index, the mute state, and the capture handle — happens
		// from here, off the UI thread, and lands back on it when it answers (issue #308).
		// Kept as a task rather than fired and forgotten: a dictation press that arrives
		// while it is still running waits on it, so the recorder cannot open device
		// index -1 (issue #312).
		_audioDevicesReady = AdoptMicrophonesWhenReadyAsync();

		_closeSequence = new Mutation.Ui.Core.ApplicationCloseSequence(
			PersistClosingState,
			_audioSessionManager.EnsureStoppedAsync,
			ReleaseClosingResources,
			(step, ex) => ErrorLogger.LogError($"Window close: {step} failed", ex));

		this.Closed += MainWindow_Closed;
		this.Activated += MainWindow_Activated;
	}

	/// <summary>
	/// Completes once the close sequence has persisted settings and window state and
	/// released the window's resources. App's <c>Window.Closed</c> handler waits on
	/// this before shutting the process down, so <c>Environment.Exit</c> cannot fire
	/// while a save is still in flight (issue #223).
	/// </summary>
	public Task ClosedCompletion => _closeSequence.Completion;

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
	//
	// The read itself is a COM call that can stall on a slow or failing device, so it
	// goes through the shared off-thread coordinator rather than running inline —
	// activation must never freeze the window and the screen reader with it. The await
	// resumes on the UI thread, where the slider is then set.
	private async void RefreshMicLevelDisplayFromOs()
	{
		// _micLevelControlsReady is only true on a supported, initialized device; on a
		// hardware-fixed device the control is disabled and there is nothing to sync.
		if (!_micLevelControlsReady)
			return;

		int generation = _micLevelInitGeneration;

		if (await _micLevelWriteCoordinator.ReadCurrentLevelAsync() is not int level)
			return;

		// Re-check after the await: the window may have closed (MainWindow_Closed clears
		// the flag, so a read still running then does not resume onto a torn-down
		// window), and a microphone change may have started a newer probe — painting
		// this level would then show one device's level against another device.
		if (!_micLevelControlsReady || generation != _micLevelInitGeneration)
			return;

		_micLevelControlsReady = false;
		try
		{
			SldMicLevel.Value = level;
		}
		catch (Exception ex)
		{
			// This is an async void continuation, so an escaping exception would have no
			// handler to reach.
			ErrorLogger.LogError("Refreshing the microphone level display failed", ex);
		}
		finally
		{
			_micLevelControlsReady = true;
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
				// The hotkey path announced nothing at all, leaving only a beep to
				// distinguish a captured region from a cancelled one.
				try
				{
					AnnounceScreenshotOutcome(await _ocrManager.TakeScreenshotToClipboardAsync());
				}
				catch (Exception ex) { await ShowErrorDialog("Screenshot Error", ex); }
			});

		if (!string.IsNullOrWhiteSpace(ocr?.ScreenshotOcrHotKey))
			TryRegister(hk, failures, "Screenshot and OCR", ocr.ScreenshotOcrHotKey!, async () =>
			{
				try
				{
					if (!await EnsureOcrConfiguredAsync()) return;
					var result = await _ocrManager.TakeScreenshotAndExtractTextAsync(OcrReadingOrder.TopToBottomColumnAware);
					PublishOcrResult(result);
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
					PublishOcrResult(result);
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
					PublishOcrResult(result);
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
					PublishOcrResult(result);
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
				DispatcherQueue.TryEnqueue(() => BtnSpeakSelection_Click(null!, null!)));

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

	/// <summary>
	/// Waits for the audio stack to answer, then fills in the microphone combo, restores
	/// the persisted choice, opens capture, and seeds the mute beep and the level
	/// controls — everything the constructor used to do inline, on the UI thread, behind
	/// COM and winmm calls that can take seconds on a machine with a microphone that is
	/// slow to enumerate (issue #308).
	/// </summary>
	/// <remarks>
	/// The order below is the constructor's old order, kept deliberately. The persisted
	/// choice is restored before the beep so the beep describes the device the user is
	/// actually on, and the level controls are probed last so their generation counter is
	/// claimed after any switch the restore queued.
	///
	/// Never throws — not just around the await, but over the whole body. It is the task
	/// the dictation path waits on, and that path only ever reads
	/// <see cref="Task.IsCompleted"/>: a fault here would be observed by nobody, leaving
	/// no log, no message, and a half-built microphone card.
	/// </remarks>
	private async Task AdoptMicrophonesWhenReadyAsync()
	{
		try
		{
			await _audioDeviceManager.InitializeAsync();

			// The window closed while the audio stack was waking up.
			if (_isClosing)
				return;

			AdoptMicrophones();
		}
		catch (Exception ex)
		{
			ErrorLogger.LogError("Setting up the audio devices failed", ex);
			if (_isClosing)
				return;

			SetMicrophonePlaceholder(NoMicrophonesPlaceholder);
			ShowStatus("Microphone",
				"Could not read the microphones on this computer. Reconnect your microphone and restart Mutation.",
				InfoBarSeverity.Error);
		}
		finally
		{
			// Run on every path, including the ones that found no device. It is the only
			// thing that sets _micLevelInitialized, and the microphone-selection handler
			// re-probes the level controls only when that flag is set — so skipping it
			// here left the level slider and the pin switch enabled, announced, and
			// permanently inert for a user who started Mutation with their microphone
			// unplugged and then plugged it in.
			if (!_isClosing)
				InitializeMicrophoneLevelControls();
		}
	}

	// The UI-thread half of startup, once the devices have answered. Split out so the
	// method above is a plain try/catch/finally over it.
	private void AdoptMicrophones()
	{
		var micList = _audioDeviceManager.CaptureDeviceInfos;

		// Suppressed: filling an empty combo raises SelectionChanged, and the selection
		// that matters is the one RestorePersistedMicrophoneSelection makes just below.
		bool wasSuppressed = _suppressMicrophoneSelection;
		_suppressMicrophoneSelection = true;
		try
		{
			CmbMicrophone.ItemsSource = micList;
		}
		finally
		{
			_suppressMicrophoneSelection = wasSuppressed;
		}

		// The mute glyph and its label were built from an unread state while the devices
		// were still being enumerated; now there is a real one behind them. Ahead of the
		// empty-list return, because "no microphones" is a real state for them to show
		// too.
		UpdateMicrophoneToggleVisuals();

		if (micList.Count == 0)
		{
			SetMicrophonePlaceholder(NoMicrophonesPlaceholder);
			ShowStatus("Microphone", "No microphones are available.", InfoBarSeverity.Warning);
			return;
		}

		SetMicrophonePlaceholder(SelectMicrophonePlaceholder);

		// Assigned outside the suppression on purpose: the selection handler is what
		// resolves the device over winmm, opens capture on it, and re-probes the level
		// controls, all on the switch worker.
		RestorePersistedMicrophoneSelection(micList);

		// Skipped when restoring the persisted choice queued a switch: that switch starts
		// capture on the device the user actually chose, and opening one here first would
		// briefly run the OS default instead — an activity light on a microphone they are
		// not using, and an error message naming it if it will not open. When the
		// persisted choice is already the default there is no switch to wait for, and
		// this is what gets the waveform going. Queued on the switch worker rather than
		// called inline, because waveInOpen is the same kind of blocking call as the rest.
		if (_requestedMicrophoneId is null)
			_ = _microphoneSwitch.RestartCaptureAsync();
		else
			_startupCapturePending = true;

		// Play a sound representing the current mute state, as the constructor used to
		// once the active microphone had been restored. Mute is an aggregate across every
		// capture device in this app, so this answers "are you live?" rather than naming
		// one endpoint — and it is seeded from the adopted device, not from whichever
		// endpoint happened to enumerate first.
		if (_audioDeviceManager.SelectedMicrophone is not null)
			BeepPlayer.Play(_audioDeviceManager.IsMuted ? BeepType.Mute : BeepType.Unmute);

		// The combo changed under the user after the window was already up, so say what
		// they ended up on rather than letting it happen in silence. The screen reader
		// has read an empty combo by this point on a slow machine, and on a fast one this
		// is simply the app naming the microphone it opened.
		if (CmbMicrophone.SelectedItem is Mutation.Ui.Core.CaptureDeviceInfo adopted)
			ShowStatus("Microphone", $"Using {adopted.FriendlyName}.", InfoBarSeverity.Informational);
	}

	// The placeholder is what a screen reader reads on a combo with nothing selected, and
	// the help text is what it reads as the control's description. They are set together
	// so a branch cannot leave one of them describing a state the app has left.
	private void SetMicrophonePlaceholder(string placeholder)
	{
		CmbMicrophone.PlaceholderText = placeholder;
		AutomationProperties.SetHelpText(CmbMicrophone, placeholder);
	}

	private void RestorePersistedMicrophoneSelection(IReadOnlyList<Mutation.Ui.Core.CaptureDeviceInfo> micList)
	{
		var match = FindPersistedMicrophone(micList);
		if (match is not null)
			CmbMicrophone.SelectedItem = match;
		else if (micList.Count > 0)
			CmbMicrophone.SelectedIndex = 0;
	}

	// The saved microphone, matched by the name the user saw when they chose it, then
	// falling back to whatever the device manager already settled on (the OS default).
	private Mutation.Ui.Core.CaptureDeviceInfo? FindPersistedMicrophone(
		IReadOnlyList<Mutation.Ui.Core.CaptureDeviceInfo> micList)
	{
		string? savedMicFullName = _settings.AudioSettings?.ActiveCaptureDeviceFullName;
		if (!string.IsNullOrWhiteSpace(savedMicFullName))
		{
			var saved = micList.FirstOrDefault(m => m.FriendlyName == savedMicFullName);
			if (saved is not null)
				return saved;
		}

		string? selectedId = _audioDeviceManager.SelectedMicrophone?.Id;
		return selectedId is null
			? null
			: micList.FirstOrDefault(m => m.Id == selectedId);
	}

	// The OS added or removed a capture device. Arrives on the device-notification
	// thread, so hop to the UI thread to rebuild the combo — its entries otherwise name
	// devices that no longer exist and omit ones that have just appeared.
	private void AudioDeviceManager_CaptureDeviceListChanged(object? sender, EventArgs e)
	{
		DispatcherQueue.TryEnqueue(RefreshMicrophoneList);
	}

	// Rebuilds the microphone combo from the current device set, preserving the user's
	// selection. The rebuild is silent by design: swapping ItemsSource re-raises
	// SelectionChanged, and re-running the whole selection pipeline (settings save,
	// capture restart, level re-probe) for a device that did not actually change would
	// interrupt the user and talk over their screen reader. A hot-plug elsewhere in the
	// list — a headset connecting, a dock, a webcam — changes nothing about the user's
	// audio, so it is not announced at all. Only losing the selected microphone is.
	private void RefreshMicrophoneList()
	{
		if (_isClosing)
			return;

		var devices = _audioDeviceManager.CaptureDeviceInfos;
		string? selectedId = (CmbMicrophone.SelectedItem as Mutation.Ui.Core.CaptureDeviceInfo)?.Id
			?? _audioDeviceManager.SelectedMicrophone?.Id;

		var update = Mutation.Ui.Core.CaptureDeviceSelectionPlanner.Plan(devices, selectedId);
		bool preserved = update.Outcome == Mutation.Ui.Core.CaptureDeviceListOutcome.SelectionPreserved;

		bool wasSuppressed = _suppressMicrophoneSelection;
		_suppressMicrophoneSelection = true;
		try
		{
			CmbMicrophone.ItemsSource = devices;
			if (preserved)
				CmbMicrophone.SelectedItem = update.Device;
		}
		finally
		{
			_suppressMicrophoneSelection = wasSuppressed;
		}

		switch (update.Outcome)
		{
			case Mutation.Ui.Core.CaptureDeviceListOutcome.SelectionPreserved:
				// Nothing about the user's audio changed. Say nothing.
				break;

			case Mutation.Ui.Core.CaptureDeviceListOutcome.SelectionReplaced:
			case Mutation.Ui.Core.CaptureDeviceListOutcome.SelectionAdopted:
				// A device is about to be selected again, so undo the empty-list wording.
				SetMicrophonePlaceholder(SelectMicrophonePlaceholder);

				// Announced before the assignment: selecting runs the handler, which
				// reports its own failure if the device turns out to be unusable, and
				// that more specific message should be the one left standing.
				ShowStatus("Microphone",
					update.Outcome == Mutation.Ui.Core.CaptureDeviceListOutcome.SelectionReplaced
						? $"The selected microphone was disconnected. Now using {update.Device!.FriendlyName}."
						: $"Microphone connected. Now using {update.Device!.FriendlyName}.",
					InfoBarSeverity.Warning);

				// Assigned outside the suppression so the normal selection path runs and
				// capture and the level controls follow the device that is actually live.
				CmbMicrophone.SelectedIndex = 0;
				break;

			default:
				// Queued on the switch worker rather than closed here: it is the same
				// winmm teardown, and going through the worker also orders it against a
				// switch that may still be in flight.
				_requestedMicrophoneId = null;
				_ = _microphoneSwitch.ReleaseAsync();
				// The combo is empty, so its placeholder is what a screen reader landing
				// on it will read. "Select a microphone" would be an instruction the user
				// cannot follow.
				SetMicrophonePlaceholder(NoMicrophonesPlaceholder);
				ShowStatus("Microphone", "No microphones are available.", InfoBarSeverity.Warning);
				break;
		}
	}

	private void InitializeHotkeyVisuals()
	{
		ConfigureButtonHotkey(BtnToggleMic, null, _settings.AudioSettings?.MicrophoneToggleMuteHotKey, "Toggle microphone mute state");
		ConfigureButtonHotkey(BtnSpeechToText, null, _settings.SpeechToTextSettings?.SpeechToTextHotKey, "Start or stop speech capture");
		// Omitting this one left the button announcing the previous accelerator until the
		// next record/stop transition, every time its hotkey was changed in Settings.
		ConfigureButtonHotkey(BtnSpeechToTextWithFormat, null, _settings.SpeechToTextSettings?.SpeechToTextWithLlmProcessingHotKey, "Start or stop speech capture, then process the transcript with the LLM");
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
		// Stop the mic-level controls responding too: a level read or a device-list
		// rebuild may still be in flight, and neither continuation may touch the
		// controls once the window is torn down.
		_isClosing = true;
		_micLevelControlsReady = false;
		// Unsubscribed here rather than in Dispose(): Dispose only runs after the close
		// sequence has stopped the recorder, and a device change arriving in that window
		// would reach for this window's DispatcherQueue after it has been closed.
		_audioDeviceManager.CaptureDeviceListChanged -= AudioDeviceManager_CaptureDeviceListChanged;
		// Same reasoning for a read that is still running: its failure would otherwise
		// arrive here and reach for a DispatcherQueue that no longer has a window.
		_textToSpeech.SpeakFailed -= TextToSpeech_SpeakFailed;
		// Signal shutdown to any in-flight transcription HTTP requests so they
		// observe cancellation rather than running until their server timeout.
		try { _shutdownCts.Cancel(); } catch (ObjectDisposedException) { }

		// The sequence persists everything before its first await; stopping a live
		// recording only happens afterwards. See ApplicationCloseSequence for why the
		// order matters.
		await _closeSequence.RunAsync();
	}

	// Writes what the user would otherwise lose on close: window position and size, the
	// active service's edited prompt, and any slider change still sitting in the save
	// debouncer (SaveSettingsToFile writes the whole settings object, so a pending
	// debounced write is subsumed rather than lost). Runs as the close sequence's first
	// step, synchronously, before anything that can yield.
	private void PersistClosingState()
	{
		try
		{
			_uiStateManager.Save(this);
		}
		catch (Exception ex)
		{
			// A window-state failure must not also cost the user their settings, so the
			// save below still runs.
			ErrorLogger.LogError("Window close: saving UI state failed", ex);
		}

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
	}

	// Final teardown, run by the close sequence even when stopping the recorder threw,
	// so the audio session is never left undisposed. Dispose() releases the audio
	// session manager along with the window's other disposables.
	private void ReleaseClosingResources()
	{
		// Guarded separately: a failure releasing the beep players must not cost us
		// Dispose(), which is what actually releases the audio session manager — the
		// leak issue #223 was filed for.
		try
		{
			BeepPlayer.DisposePlayers();
		}
		catch (Exception ex)
		{
			ErrorLogger.LogError("Window close: disposing the beep players failed", ex);
		}

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
			// Cancelling the region overlay used to be announced as a success.
			AnnounceScreenshotOutcome(await _ocrManager.TakeScreenshotToClipboardAsync());
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
			PublishOcrResult(result, "Screenshot & OCR", "Text captured from screenshot.");
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
			PublishOcrResult(result, "Screenshot & OCR (left-to-right)", "Text captured from screenshot using left-to-right reading order.");
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
			PublishOcrResult(result, "OCR", "Clipboard image converted to text.", InfoBarSeverity.Warning);
		}
		catch (Exception ex)
		{
			ShowStatus("OCR", ex.Message, InfoBarSeverity.Error);
			await ShowErrorDialog("OCR Clipboard Error", ex);
		}
	}

	private async void BtnOcrDocuments_Click(object sender, RoutedEventArgs e)
	{
		// Claimed here, before the first await, and not by BtnOcrDocuments.IsEnabled — that
		// only lands after the configuration check and the file picker, so two fast clicks
		// would both get past it. The second handler would then reach its finally and end
		// the first one's run, severing an in-flight batch from cancellation entirely.
		// Check and Begin sit together with nothing awaited between them, so on the UI
		// thread nothing can interleave (issue #227).
		if (_ocrDocumentsRun.IsRunning)
			return;

		// Declared out here so the cancellation message can report how far the run got.
		OcrBatchProgressNarrator? narrator = null;
		OcrDocumentsRun? run = null;
		try
		{
			// Inside the try: Begin throws on a disposed controller, and this is an
			// async void handler, so an escaping exception would take the process down.
			run = _ocrDocumentsRun.Begin();

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

                        ShowStatus("OCR documents", $"Processing {files.Count} document(s)...", InfoBarSeverity.Informational);

                        OcrDocumentsProgressBar.Value = 0;
                        OcrDocumentsProgressBar.Maximum = 1;
                        OcrDocumentsProgressPanel.Visibility = Visibility.Visible;
                        OcrDocumentsProgressLabel.Text = "Preparing documents...";
                        BtnCancelOcrDocuments.IsEnabled = true;

                        // Focus moves to Cancel before the OCR button is disabled, not after:
                        // disabling the focused element destroys focus first, and if the move
                        // then failed the user would be left adrift with no way back. Cancel is
                        // where they would want to be anyway — it is the only control the run
                        // offers, and hunting for it by Tab is the wrong thing to ask.
                        MoveFocusToCancelOcrDocuments();

                        BtnOcrDocuments.IsEnabled = false;

                        var paths = files.Select(file => file.Path).ToList();
                        narrator = new OcrBatchProgressNarrator(paths.Count);
                        var progress = new Progress<OcrProcessingProgress>(info =>
                        {
                                OcrDocumentsProgressPanel.Visibility = Visibility.Visible;
                                OcrDocumentsProgressBar.Maximum = Math.Max(1, info.TotalSegments);
                                OcrDocumentsProgressBar.Value = info.ProcessedSegments;
                                OcrDocumentsProgressLabel.Text = OcrBatchProgressNarrator.ComposeLabel(info);

                                // The label above updates every page, for the eye. Speech is not
                                // free that way — a forty-page batch would leave the reader talking
                                // for minutes — so the narrator decides what is worth saying, which
                                // is one announcement per finished document (issue #228).
                                string? announcement = narrator.TryComposeAnnouncement(info);
                                if (announcement is not null)
                                        AnnounceOcrDocumentsProgress(announcement);
                        });

                        var result = await _ocrManager.ExtractTextFromFilesAsync(paths, OcrReadingOrder.TopToBottomColumnAware, run.Token, progress);
			SetOcrText(result.Text);

			// The one OCR path that never sent the configured shortcut, though it is the
			// path where the user has waited longest and most wants to hear the answer.
			// Sent here rather than after the branches below, and only once the run has
			// reached a result: a cancelled batch never gets this far, so the shortcut is
			// not aimed at whatever the OCR box still held from last time (issue #335).
			//
			// The paste asks the same question the other eight paths ask and normally
			// answers no here, because a batch is started from a picker in this window and
			// this window still has the keyboard when it ends. That is the answer worth
			// having: forty pages arriving in whatever control happens to be focused is not
			// what the setting is for.
			bool paste = ShouldPasteOcrText(result.Success, result.ClipboardCopyFailed, result.Text);
			HotkeyManager.SendHotkeyAfterDelay(
				PostOperationHotkey.AfterOcr(paste, _settings.AzureComputerVisionSettings?.SendHotkeyAfterOcrOperation),
				PostOperationHotkey.OcrDelay(result.Success));

			if (result.SuccessCount == 0)
			{
				string failureDetails = result.Failures.Count > 0 ? string.Join("\n", result.Failures) : "Unable to extract text from the selected documents.";
				ShowStatus("OCR documents", failureDetails, InfoBarSeverity.Error);
			}
			else if (result.Success)
			{
				// The clipboard half of this sentence is not a foregone conclusion: something
				// else can hold the clipboard open through every retry, and telling the user
				// their forty pages are on it when they are not is worse than saying nothing
				// (issue #341). The text is in the OCR box either way.
				(string clipboard, InfoBarSeverity severity) = result.ClipboardCopyFailed
					? ("but they could not be copied to the clipboard. They are in the OCR results box.", InfoBarSeverity.Warning)
					: ("Results copied to the clipboard.", InfoBarSeverity.Success);

				ShowStatus("OCR documents", $"Processed {result.SuccessCount} document(s){(result.ClipboardCopyFailed ? ", " : ". ")}{clipboard}", severity);
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
		catch (OperationCanceledException)
		{
			// The confirmation that cancelling took effect. Cancelling is something the
			// user asked for, not a failure, so it does not raise the error dialog — but
			// it must still be said out loud, since the only other signal that the batch
			// stopped is the progress bar vanishing (issue #227).
			ShowStatus(
				"OCR documents",
				$"Cancelled. {narrator?.DocumentsCompleted ?? 0} document(s) finished before the batch stopped; no results were copied.",
				InfoBarSeverity.Informational);
		}
		catch (Exception ex)
		{
			ShowStatus("OCR documents", ex.Message, InfoBarSeverity.Error);
			await ShowErrorDialog("OCR Documents Error", ex);
		}
                finally
                {
                        _ocrDocumentsRun.End(run);
                        BtnOcrDocuments.IsEnabled = true;

                        // The Cancel button is about to disappear. If the user is standing on it
                        // — which they are if they pressed it, or if focus never moved off it —
                        // put them back on the button they started from rather than letting
                        // focus fall to wherever WinUI decides.
                        RestoreFocusFromCancelOcrDocuments();

                        BtnCancelOcrDocuments.IsEnabled = false;
                        OcrDocumentsProgressPanel.Visibility = Visibility.Collapsed;
                        OcrDocumentsProgressBar.Value = 0;
                        OcrDocumentsProgressBar.Maximum = 1;
                        OcrDocumentsProgressLabel.Text = string.Empty;
                }
        }

	private void BtnCancelOcrDocuments_Click(object sender, RoutedEventArgs e)
	{
		// The button is deliberately not disabled here: doing that would destroy focus on
		// the control the user is standing on, and leave a screen-reader user with no idea
		// where they are (issue #227).
		if (_ocrDocumentsRun.Cancel())
		{
			// The batch does not end here — it unwinds as the running OCR calls observe
			// the token — so this only reports that the request landed. The
			// OperationCanceledException handler confirms it took effect.
			ShowStatus("OCR documents", "Cancelling the batch...", InfoBarSeverity.Informational);
			return;
		}

		// A repeat press on a stop already asked for. Answered rather than ignored: an
		// enabled, focused button that produces silence reads as one that did not register.
		if (_ocrDocumentsRun.IsRunning)
			ShowStatus("OCR documents", "Already stopping.", InfoBarSeverity.Informational);
	}

	/// <summary>
	/// Puts focus on the Cancel button as the run starts. The button was collapsed until
	/// a moment ago, and WinUI refuses focus to an element it has not laid out yet, so a
	/// refusal is retried once the layout pass has run — and only if the run is still
	/// going, so a batch that finished in the meantime does not snatch focus back.
	/// </summary>
	private void MoveFocusToCancelOcrDocuments()
	{
		try
		{
			if (BtnCancelOcrDocuments.Focus(FocusState.Programmatic))
				return;

			DispatcherQueue?.TryEnqueue(() =>
			{
				try
				{
					if (OcrDocumentsProgressPanel.Visibility == Visibility.Visible && BtnCancelOcrDocuments.IsEnabled)
						BtnCancelOcrDocuments.Focus(FocusState.Programmatic);
				}
				catch (Exception ex)
				{
					System.Diagnostics.Debug.WriteLine($"MoveFocusToCancelOcrDocuments (retry) failed: {ex.Message}");
				}
			});
		}
		catch (Exception ex)
		{
			System.Diagnostics.Debug.WriteLine($"MoveFocusToCancelOcrDocuments failed: {ex.Message}");
		}
	}

	/// <summary>
	/// Moves focus off the Cancel button before it is hidden, but only when focus is
	/// actually on it — the user may have wandered elsewhere during a long batch, and
	/// yanking them back would be worse than leaving them be.
	/// </summary>
	private void RestoreFocusFromCancelOcrDocuments()
	{
		try
		{
			if (Content?.XamlRoot is null)
				return;

			object? focused = FocusManager.GetFocusedElement(Content.XamlRoot);
			if (ReferenceEquals(focused, BtnCancelOcrDocuments))
				BtnOcrDocuments.Focus(FocusState.Programmatic);
		}
		catch (Exception ex)
		{
			System.Diagnostics.Debug.WriteLine($"RestoreFocusFromCancelOcrDocuments failed: {ex.Message}");
		}
	}

	/// <summary>
	/// Announces batch OCR progress to the screen reader. Raised on the progress label
	/// itself so the announcement carries that context. "Other" rather than an important
	/// kind, and MostRecent, so a slow reader hears where the run is now instead of
	/// working through a backlog of documents that finished minutes ago (issue #228).
	/// </summary>
	private void AnnounceOcrDocumentsProgress(string announcement)
	{
		if (string.IsNullOrWhiteSpace(announcement))
			return;

		var peer = Microsoft.UI.Xaml.Automation.Peers.FrameworkElementAutomationPeer.CreatePeerForElement(OcrDocumentsProgressLabel);
		peer?.RaiseNotificationEvent(
			Microsoft.UI.Xaml.Automation.Peers.AutomationNotificationKind.Other,
			Microsoft.UI.Xaml.Automation.Peers.AutomationNotificationProcessing.MostRecent,
			announcement,
			OcrBatchProgressNarrator.AnnouncementActivityId);
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
			PublishOcrResult(result, "OCR (left-to-right)", "Clipboard image converted using left-to-right reading order.", InfoBarSeverity.Warning);
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
            
			// Only a start waits. Asked of the session, through the same planner the
			// recorder itself uses, because a stop must never be held up by a device that
			// is slow to open: the recording is already running on a device that opened
			// fine, and the user has finished speaking.
			if (_audioSessionManager.NextPressStartsRecording && !await WaitForMicrophoneAsync())
				return;

            LlmSettings.LlmPrompt? autoRunPrompt = _promptLibrary?.GetAutoRunPrompt();
            await _audioSessionManager.StartStopRecordingAsync(_activeSpeechService, useLlmProcessing, GetActivePrompt(), autoRunPrompt, _shutdownCts.Token);
		}
		catch (Exception ex)
		{
			ShowStatus("Speech to Text", ex.Message, InfoBarSeverity.Error);
			await ShowErrorDialog("Speech to Text Error", ex);
		}
	}

	/// <summary>
	/// Holds a dictation start until the microphone the user last chose is the one the
	/// recorder will open. Returns false when the caller must not go on to start.
	/// </summary>
	/// <remarks>
	/// <para>
	/// Since #267 the microphone switch runs on a background worker, so
	/// <c>MicrophoneDeviceIndex</c> does not move until the switch lands. Arrow onto a
	/// Bluetooth headset, hear the combo announce it, press the dictation hotkey while it
	/// is still opening, and the session recorded from the previous microphone — with
	/// nothing in the UI to contradict what had just been announced (issue #312). Before
	/// #267 the frozen message pump held the press back; the freeze was the bug, but it
	/// was also what made this ordering safe.
	/// </para>
	/// <para>
	/// Three things make the wait safe to have on the dictation path. It is re-entrant-
	/// guarded, so a second press cannot queue a second start behind the first. It is
	/// bounded, because a wedged device is a hang rather than an exception and would
	/// otherwise leave the shortcut permanently dead and silent. And it is audible from
	/// the first beat, so the press never reads as one that did not register.
	/// </para>
	/// </remarks>
	private async Task<bool> WaitForMicrophoneAsync()
	{
		// Checked before the settle test, not after. Between the switch landing and the
		// parked press's continuation being dispatched, the microphone reads as settled
		// while a start is still pending — and a second press taken as a fresh start
		// there is the double entry this guard exists to stop.
		//
		// A second press is answered rather than ignored, and never queued: two waits
		// resume together and either start two recordings or instantly stop one that has
		// barely begun. No second beep — the first press already gave the audible
		// acknowledgement, and nothing has changed since.
		if (_dictationStartWaiting)
		{
			ShowStatus("Speech to Text",
				"Still waiting for the microphone. Recording will start as soon as it is ready.",
				InfoBarSeverity.Informational);
			return false;
		}

		if (!MicrophoneIsSettling())
			return true;

		_dictationStartWaiting = true;
		UpdateRecordingActionAvailability();
		try
		{
			// Within a beat of the press, whatever the device is doing. The beep is the
			// half that carries when Mutation is in the background and the shortcut was
			// pressed from another app, where a status message is never read out.
			BeepPlayer.Play(BeepType.Waiting);
			ShowStatus("Speech to Text",
				"Waiting for the microphone to be ready — recording will start in a moment.",
				InfoBarSeverity.Informational);

			if (!await MicrophoneSettledAsync(MicrophoneReadyTimeout))
			{
				if (_isClosing)
					return false;

				return FallBackToTheLiveMicrophone();
			}
		}
		finally
		{
			_dictationStartWaiting = false;
			if (!_isClosing)
				UpdateRecordingActionAvailability();
		}

		// The window closed while the device was opening; teardown would dispose the
		// recording as fast as it started.
		return !_isClosing;
	}

	/// <summary>
	/// What a dictation start does once the wait has run out of budget. Returns whether
	/// the caller should still go on to start.
	/// </summary>
	/// <remarks>
	/// Refusing outright was the obvious answer and it is the wrong one. A winmm call
	/// wedged inside a driver never returns, so the switch worker never goes idle and the
	/// microphone never stops "settling" — every later press would then cost the full
	/// budget and refuse, which is the permanently dead hotkey issue #312 set out to
	/// prevent, reached the long way round. Worse, the obvious remedy would not work
	/// either: choosing another microphone queues behind the same wedged worker.
	///
	/// So when there is a device that is actually open, the recording goes ahead on it and
	/// the user is told which one, by name — a bounded wait and a plain sentence, rather
	/// than the silent wrong-microphone recording that was the bug. Only when nothing is
	/// live at all is there no recording to make.
	/// </remarks>
	private bool FallBackToTheLiveMicrophone()
	{
		BeepPlayer.Play(BeepType.Failure);

		if (_audioDeviceManager.SelectedMicrophone is not { } live)
		{
			ShowStatus("Speech to Text",
				"The microphone is still not ready, so nothing was recorded. Try again, or choose another microphone.",
				InfoBarSeverity.Warning);
			return false;
		}

		ShowStatus("Speech to Text",
			$"The microphone you chose is still not ready. Recording from {live.FriendlyName} instead.",
			InfoBarSeverity.Warning);
		return true;
	}

	// Whether the microphone the recorder would open may still be about to change:
	// startup has not finished adopting a device, or a switch is in flight or queued.
	private bool MicrophoneIsSettling() =>
		!_audioDevicesReady.IsCompleted || _microphoneSwitch.IsSwitching;

	// Waits out the settling, giving up once the budget is spent. The loop is what covers
	// a user who changes their mind mid-wait: a switch that lands only for a newer one to
	// start means the recording still has to follow the last choice, not the first. The
	// budget spans the whole loop, so re-arming cannot extend the wait indefinitely.
	private async Task<bool> MicrophoneSettledAsync(TimeSpan budget)
	{
		var spent = System.Diagnostics.Stopwatch.StartNew();

		while (!_isClosing && MicrophoneIsSettling())
		{
			TimeSpan remaining = budget - spent.Elapsed;
			if (remaining <= TimeSpan.Zero)
				return false;

			var settled = Task.WhenAll(_audioDevicesReady, _microphoneSwitch.WaitForIdleAsync());

			// Linked to the shutdown token so the timer does not outlive the window, and
			// cancelled the moment the race is decided so a settled microphone does not
			// leave a timer ticking for the rest of the budget.
			using var expiry = CancellationTokenSource.CreateLinkedTokenSource(_shutdownCts.Token);
			var finished = await Task.WhenAny(settled, Task.Delay(remaining, expiry.Token));
			expiry.Cancel();

			if (finished != settled)
				return false;
		}

		return !_isClosing;
	}

	/// <summary>
	/// Reports a text-to-speech action that threw. Every one of these can be fired from a
	/// global hotkey as well as a button, and a hotkey press has no other feedback at all:
	/// an escaping exception reaches App.OnUnhandledException, which logs it and marks it
	/// handled, so the press just does nothing and there is no way to find out why
	/// (issue #235). The beep comes first because it is the only part that lands
	/// immediately, whatever else the app is doing.
	/// </summary>
	private async Task ReportTextToSpeechFailureAsync(string action, Exception ex)
	{
		BeepPlayer.Play(BeepType.Failure);
		ShowStatus("Text to Speech", ComposeTextToSpeechFailureMessage(ex), InfoBarSeverity.Error);
		await ShowErrorDialog($"{action} Error", ex);
	}

	/// <summary>
	/// The engine's message, plus a pointer to the voice picker when the configured voice
	/// has gone missing — the usual cause, and one the raw exception never names.
	/// </summary>
	private string ComposeTextToSpeechFailureMessage(Exception ex)
	{
		var tts = _settings.TextToSpeechSettings ?? new TextToSpeechSettings();

		IReadOnlyList<string> voices;
		try { voices = _textToSpeech.GetVoiceNames(); }
		catch { voices = Array.Empty<string>(); }

		return TextToSpeechFailureMessage.Compose(ex.Message, tts.VoiceName, voices);
	}

	/// <summary>
	/// A read that failed after Speak had already returned. The work runs on a background
	/// thread, so this arrives off the UI thread and has to be marshalled before anything
	/// here touches a control (issue #236).
	/// </summary>
	private void TextToSpeech_SpeakFailed(object? sender, Exception ex)
	{
		DispatcherQueue?.TryEnqueue(async () =>
		{
			try { await ReportTextToSpeechFailureAsync("Speak", ex); }
			catch (Exception reportFailure)
			{
				System.Diagnostics.Debug.WriteLine($"Reporting a speech failure failed: {reportFailure.Message}");
			}
		});
	}

	public async void BtnTextToSpeech_Click(object? sender, RoutedEventArgs? e)
	{
		try
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
		catch (Exception ex)
		{
			await ReportTextToSpeechFailureAsync("Speak Clipboard", ex);
		}
	}

	public async void BtnRestartTts_Click(object? sender, RoutedEventArgs? e)
	{
		try
		{
			await ReadClipboardFreshAsync();
		}
		catch (Exception ex)
		{
			await ReportTextToSpeechFailureAsync("Restart Speech", ex);
		}
	}

	public async void BtnSkipSentenceBack_Click(object? sender, RoutedEventArgs? e)
	{
		try
		{
			var tts = _settings.TextToSpeechSettings ?? new TextToSpeechSettings();
			_textToSpeech.SkipSentence(-1, tts.Rate, tts.Volume, tts.VoiceName, tts.SkipSentenceGraceWindowMs);
		}
		catch (Exception ex)
		{
			await ReportTextToSpeechFailureAsync("Skip Sentence", ex);
		}
	}

	public async void BtnSkipSentenceForward_Click(object? sender, RoutedEventArgs? e)
	{
		try
		{
			var tts = _settings.TextToSpeechSettings ?? new TextToSpeechSettings();
			_textToSpeech.SkipSentence(1, tts.Rate, tts.Volume, tts.VoiceName, tts.SkipSentenceGraceWindowMs);
		}
		catch (Exception ex)
		{
			await ReportTextToSpeechFailureAsync("Skip Sentence", ex);
		}
	}

	public async void BtnSpeakPosition_Click(object? sender, RoutedEventArgs? e)
	{
		try
		{
			var tts = _settings.TextToSpeechSettings ?? new TextToSpeechSettings();
			ReadingPosition position = _textToSpeech.GetReadingPosition();
			string announcement = ReadingAnnouncements.Position(position);
			_textToSpeech.SpeakAnnouncement(announcement, tts.Rate, tts.Volume, tts.VoiceName);
			ShowStatus("Text to Speech", announcement, InfoBarSeverity.Informational);
		}
		catch (Exception ex)
		{
			await ReportTextToSpeechFailureAsync("Announce Position", ex);
		}
	}

	// Toggle pause/resume on its own hotkey, distinct from Stop. While speaking, freeze the
	// read in place (the service speaks a brief "Paused" cue). While paused, resume it. When
	// nothing is playing, announce that there is nothing to resume — this never starts a fresh
	// read; that remains the job of the speak hotkey.
	public async void BtnPauseResume_Click(object? sender, RoutedEventArgs? e)
	{
		try
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
		catch (Exception ex)
		{
			await ReportTextToSpeechFailureAsync("Pause or Resume", ex);
		}
	}

	public async void BtnSpeakToFile_Click(object? sender, RoutedEventArgs? e)
	{
		var tts = _settings.TextToSpeechSettings ?? new TextToSpeechSettings();

		string text;
		StorageFile? file;
		try
		{
			var (kind, clipboardText) = await _clipboard.InspectAsync();
			if (kind != ClipboardKind.Text)
			{
				AnnounceUnreadableClipboard(kind, tts);
				return;
			}

			if (!TtsFileExport.TryResolveExportText(clipboardText, out text, out string resolveError))
			{
				_textToSpeech.SpeakAnnouncement(resolveError, tts.Rate, tts.Volume, tts.VoiceName);
				BeepPlayer.Play(BeepType.Failure);
				ShowStatus("Speak to file", resolveError, InfoBarSeverity.Warning);
				return;
			}

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
			await ReportTextToSpeechFailureAsync("Speak to File", ex);
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
			await ReportTextToSpeechFailureAsync("Speak to File", ex);
		}
		finally
		{
			BtnSpeakToFile.IsEnabled = true;
		}
	}

	// Awaitable rather than async void: this is not an event handler, and an async void
	// method that throws takes the process down with it instead of merely failing the
	// action. Every caller is a handler that owns the try/catch (issue #235).
	private async Task SpeakActiveSelectionAsync()
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

	public async void BtnSpeakSelection_Click(object? sender, RoutedEventArgs? e)
	{
		try
		{
			await SpeakActiveSelectionAsync();
		}
		catch (Exception ex)
		{
			await ReportTextToSpeechFailureAsync("Speak Selection", ex);
		}
	}

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
	// reflects whether pinning is enabled, and any pinned level is re-asserted now.
	//
	// Runs at app startup and again after every microphone change. Both facts it needs
	// — whether the device supports software level control, and its current level — are
	// COM reads, so they go through the shared off-thread coordinator rather than
	// running inline. The mic-change path is the one most likely to meet a slow or
	// failing device, because one was just swapped in, and a stall there would freeze
	// the window and the screen reader with it (issue #263).
	private async void InitializeMicrophoneLevelControls()
	{
		// Everything up to the first await happens synchronously, so a mic change during
		// startup re-enters here rather than being skipped by the _micLevelInitialized
		// gate, and no stale probe can win a race against a newer one.
		_micLevelInitialized = true;
		_micLevelControlsReady = false;
		int generation = ++_micLevelInitGeneration;

		// The controls stay enabled while the probe runs. Disabling them takes them out
		// of the keyboard tab order, so a screen-reader user tabbing through simply does
		// not meet them and then finds them reappear — and a disabled control cannot take
		// focus, so any explanation attached to it is announced to nobody.
		// _micLevelControlsReady already makes them inert, which is the part that
		// matters; HelpText is what carries the reason, because a screen reader reads it
		// on focus.
		SetMicLevelHelpText("Checking whether this microphone supports software level control.");

		try
		{
			var state = await _micLevelWriteCoordinator.ReadLevelStateAsync();

			// A newer probe (another mic change) or the window closing supersedes this
			// one; applying a stale result would pair one device's controls with
			// another's level.
			if (generation != _micLevelInitGeneration || _isClosing)
				return;

			ApplyMicrophoneLevelState(state);
		}
		catch (Exception ex)
		{
			// async void: nothing downstream can observe this, and leaving the controls
			// mid-probe would strand them inert with a stale explanation.
			ErrorLogger.LogError("Setting up the microphone level controls failed", ex);
			if (generation == _micLevelInitGeneration && !_isClosing)
				ApplyMicrophoneLevelState(new Mutation.Ui.Core.CaptureLevelState(IsSupported: false, Level: null));
		}
	}

	// Settles the level controls on what the probe found.
	private void ApplyMicrophoneLevelState(Mutation.Ui.Core.CaptureLevelState state)
	{
		TglPinMicLevel.IsEnabled = state.IsSupported;
		SldMicLevel.IsEnabled = state.IsSupported;

		if (!state.IsSupported)
		{
			TglPinMicLevel.IsOn = false;
			SetMicLevelHelpText("This microphone does not support software level control.");
			return;
		}

		SetMicLevelHelpText(null);

		int? pinned = _settings.AudioSettings?.PinnedCaptureLevel;
		TglPinMicLevel.IsOn = pinned.HasValue;

		// A supported device whose level could not be read right now leaves the slider
		// where it is. Snapping it to a default would report a level the device is not
		// actually at — the very symptom this work is meant to remove.
		if ((pinned ?? state.Level) is int level)
			SldMicLevel.Value = level;

		_micLevelControlsReady = true;

		// Re-assert now (app startup, or after a mic change): correct the level if
		// another app changed it. Route it through the shared off-thread write worker
		// rather than writing on the UI thread — the same guarantee the slider, pin
		// toggle, and record-start paths already have. On the mic-change path this
		// runs mid-session, where the write's failure path re-enumerates the device
		// and would otherwise briefly freeze the window and the screen reader.
		ReassertPinnedLevelOffThread(pinned);
	}

	// Explains the level controls' current state where a screen reader will actually
	// read it: HelpText is announced on focus, unlike a tooltip. Null restores the
	// descriptions the XAML already carries in its tooltips.
	private void SetMicLevelHelpText(string? helpText)
	{
		AutomationProperties.SetHelpText(TglPinMicLevel, helpText ?? string.Empty);
		AutomationProperties.SetHelpText(SldMicLevel, helpText ?? string.Empty);
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
		var outcome = await TryInsertIntoActiveApplicationAsync(formatted, clipboardAvailable: copied);

		// Through the planner like the other two delivery sites. This one kept its own
		// copy of the beep-and-message half and had no idea the shortcut was the third
		// part of the same decision, so formatting a transcript delivered the text and
		// then did nothing the user had asked for afterwards (issue #335).
		var plan = TranscriptCompletionPlanner.Plan(copied, outcome, "formatted transcript");

		// Before the beep and the status, either of which can throw — a status builds an
		// automation peer and starts a timer (issue #234). The text has already landed by
		// this point, so the shortcut that acts on it should not be the thing that is lost.
		//
		// This button is the one delivery site with no hotkey of its own, so Mutation is
		// always the foreground window when it runs — which is why the insert above
		// deliberately typed nothing. The shortcut is still sent, on purpose: it is normally
		// a screen-reader command, which is global and wants running wherever the user is.
		// A shortcut that only means something in another application will land here instead,
		// and that is the same bargain the eight OCR buttons already make (PR #339).
		if (plan.SendConfiguredHotkey)
			HotkeyManager.SendHotkeyAfterDelay(
				_settings.SpeechToTextSettings?.SendHotkeyAfterTranscriptionOperation,
				PostOperationHotkey.SuccessDelayMs);

		BeepPlayer.Play(plan.Beep);
		ShowStatus("Formatting",
			plan.FailureMessage ?? "Transcript formatted and copied.",
			plan.Succeeded ? InfoBarSeverity.Success : InfoBarSeverity.Error);
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
			// ShowErrorDialog logs it; a second call here would duplicate the entry.
			ShowStatus("Processing", ex.Message, InfoBarSeverity.Error);
			await ShowErrorDialog("Process with LLM Error", ex);
		}
	}

    private async void ExecutePrompt(LlmSettings.LlmPrompt prompt)
    {
        // Declared out here so the catch filters below can tell the three ways this can end
        // in an OperationCanceledException apart: the user's own cancel, the window
        // closing, and a provider that never answered.
        CancellationToken llmToken = default;
        try
        {
             // Marshaling to UI thread if called from hotkey background thread
             if (!DispatcherQueue.HasThreadAccess)
             {
                 DispatcherQueue.TryEnqueue(() => ExecutePrompt(prompt));
                 return;
             }

			// Nothing to run into, and nothing left to announce it on.
			if (_isClosing) return;

			// The dictation flow owns its own slot, and both write the Formatted Transcript
			// box and paste into the focused app. Starting a prompt on top of a dictation's
			// model call gave two concurrent requests fighting over both. Refused rather
			// than cancelled: that call belongs to a different flow the user started
			// deliberately, and the record buttons are right there offering to stop it.
			if (_audioSessionManager.IsProcessingWithLlm)
			{
				ShowStatus("Processing", "Still finishing the dictation's LLM step — try again shortly.", InfoBarSeverity.Warning);
				return;
			}

			// A press while a request is in flight means "stop", not "run another". A model
			// call can take minutes — the retry ladder escalates its timeout on every
			// attempt — so without this the user had no way out of it (issue #256). Checked
			// before the start beep so a cancel never sounds like a fresh start.
			//
			// One slot for the whole library, so a *different* prompt's press stops the one
			// that is running rather than queueing behind it. That is the honest reading of
			// the press — two model calls at once is not what anybody asked for — but it is
			// only honest if the message names what stopped, or the user is left wondering
			// why prompt B did nothing.
			switch (CancellablePressPlanner.For(_promptLlmOperation.Running, _promptLlmOperation.CancelRequested))
			{
				case CancellablePressAction.AlreadyStopping:
					// A repeat press on a stop already asked for. Answered rather than
					// ignored: silence from a shortcut reads as one that did not register.
					// No second beep — the first press already acknowledged audibly.
					ShowStatus("Processing", CancellationMessages.AlreadyStopping, InfoBarSeverity.Informational);
					return;

				case CancellablePressAction.Cancel:
					_promptLlmOperation.Cancel();
					BeepPlayer.Play(BeepType.Failure);
					ShowStatus("Processing", ComposePromptCancelMessage(), InfoBarSeverity.Informational);
					return;
			}

			BeepPlayer.Play(BeepType.Start);
			TxtFormatTranscript.Text = ProcessingPlaceholder;

			// Claimed here, before the first await, so two presses in quick succession
			// cannot both get past the guard above and then fight over the slot — the
			// first to unwind used to release the claim the second one was relying on,
			// and both calls died. The handle releases the slot only while this run still
			// owns it, and it is linked to the shutdown token so closing the window
			// abandons the request instead of leaving it climbing the retry ladder with
			// nobody to read it.
			using var run = _promptLlmOperation.Begin(_shutdownCts.Token);
			llmToken = run.Token;
			_runningPromptName = prompt.Name;

			string raw = await _clipboard.GetTextAsync();
			if (string.IsNullOrWhiteSpace(raw))
			{
				ShowStatus("Processing", "Clipboard is empty.", InfoBarSeverity.Warning);
				ClearProcessingPlaceholder();
				return;
			}

			string modelName = !string.IsNullOrWhiteSpace(prompt.ModelName) ? prompt.ModelName : LlmSettings.DefaultModel;
			FastModeFallback? fastModeFallback = null;
			var requestOptions = new LlmRequestOptions
			{
				FastMode = prompt.FastMode,
				OnFastModeFallback = f => fastModeFallback = f,
			};
			string processed = await _transcriptFormatter.ProcessWithLlmAsync(raw, prompt.Content, modelName, requestOptions, run.Token);

			TxtFormatTranscript.Text = processed;
			bool copied = await _clipboard.TrySetTextAsync(processed);
			var outcome = await TryInsertIntoActiveApplicationAsync(processed, clipboardAvailable: copied);
			var plan = TranscriptCompletionPlanner.Plan(copied, outcome, "processed text");

			// Claimed after everything that can throw and just before the announcement,
			// so a failure on the delivery path cannot burn the one notice the user gets
			// this session. Resolved once, outside the branch, so it is spent exactly
			// once however the delivery turns out.
			FastModeFallbackReason? fastModeNotice = ClaimFastModeNotice(prompt.Id, fastModeFallback);

			// Ahead of the beep and the status rather than after them. Both can throw —
			// ShowStatus builds an automation peer and starts a timer, which is what
			// issue #234 was about — and the catch below would then swallow the shortcut
			// as well, after the text had already been delivered (issue #335).
			if (plan.SendConfiguredHotkey)
				HotkeyManager.SendHotkeyAfterDelay(
					_settings.SpeechToTextSettings?.SendHotkeyAfterTranscriptionOperation,
					PostOperationHotkey.SuccessDelayMs);

			BeepPlayer.Play(plan.Beep);
			ShowStatus("Processing",
				FastModeMessages.AppendTo(
					plan.FailureMessage ?? $"Applied prompt '{prompt.Name}' with the language model.",
					fastModeNotice),
				plan.Succeeded ? InfoBarSeverity.Success : InfoBarSeverity.Error);
        }
        catch (OperationCanceledException) when (_isClosing || _shutdownCts.IsCancellationRequested)
        {
             // The window is on its way out. Nothing to announce and nothing left to
             // announce it on — ShowStatus would build an automation peer and start a timer
             // on a window that has already been torn down.
             ErrorLogger.LogInfo("LLM", "Prompt run abandoned because the window is closing.");
        }
        catch (OperationCanceledException) when (llmToken.IsCancellationRequested)
        {
             // Asked for, not a failure: no error dialog, and the "Processing..."
             // placeholder is taken back out of the box rather than left standing there
             // telling a screen-reader user work is still running (issue #295).
             ClearProcessingPlaceholder();
             ShowStatus("Processing", CancellationMessages.LlmCompleted, InfoBarSeverity.Informational);
        }
        catch (Exception ex)
        {
             // A per-attempt timeout arrives here as an OperationCanceledException too,
             // once the ladder is exhausted — and nobody cancelled anything, so it keeps
             // the error dialog, the Error severity and the log entry it has always had.
             // Reading it as the user's own cancel is how a dead provider came to be
             // reported as something they had asked for, silently.
             //
             // Only the placeholder is cleared. This catch also covers everything after the
             // result lands in the box — the paste injection, the beep, the automation peer
             // ShowStatus builds (the throw #234 was filed about) — and clearing
             // unconditionally would take a result the user had waited minutes for and
             // leave them an empty box and an error.
             ClearProcessingPlaceholder();
             // ShowErrorDialog logs it; a second call here would duplicate the entry.
             ShowStatus("Processing Failed", ex.Message, InfoBarSeverity.Error);
             await ShowErrorDialog($"Error executing prompt '{prompt.Name}'", ex);
        }
    }

    // Takes the "Processing..." placeholder back out of the box, and only that. Left
    // standing it tells a screen-reader user work is still running (issue #295); cleared
    // blindly it would throw away a result that had already been delivered into the box.
    private void ClearProcessingPlaceholder()
    {
        if (TxtFormatTranscript.Text == ProcessingPlaceholder)
            TxtFormatTranscript.Text = string.Empty;
    }

    private const string ProcessingPlaceholder = "Processing...";

    // Names the prompt that is being stopped. Pressing prompt B's shortcut while prompt A
    // is in flight stops A — there is one slot — and without the name a blind user is left
    // with a generic "cancelling" and no clue why the prompt they asked for did nothing.
    private string ComposePromptCancelMessage() =>
        string.IsNullOrWhiteSpace(_runningPromptName)
            ? CancellationMessages.LlmRequested
            : $"Cancelling LLM processing for '{_runningPromptName}'...";

	/// <summary>
	/// Whether this Fast mode fallback should be told to the user, given they may
	/// already have heard it this session. The notice itself rides along on the run's
	/// outcome status — the ordinary announcement channel, not a modal, which would
	/// steal focus from whatever they were dictating into. Their Fast mode setting is
	/// never touched.
	/// </summary>
	private FastModeFallbackReason? ClaimFastModeNotice(int promptId, FastModeFallback? fallback)
	{
		if (fallback is null)
			return null;
		return _fastModeNotices.ShouldAnnounce(promptId, fallback.Reason)
			? fallback.Reason
			: null;
	}

	private void UpdateMicrophoneToggleVisuals()
	{
		bool muted = _audioDeviceManager.IsMuted;
		string labelText = muted ? "Unmute microphone" : "Mute microphone";
		BtnToggleMicIcon.Glyph = MicOnGlyph;
		BtnToggleMicSlash.Visibility = muted ? Visibility.Visible : Visibility.Collapsed;
		ConfigureButtonHotkey(BtnToggleMic, null, _settings.AudioSettings?.MicrophoneToggleMuteHotKey, labelText, labelText);
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

	private void UpdateSpeechButtonVisuals(string label, string glyph, bool isEnabled = true, string? description = null)
	{
		// isEnabled is honoured in every branch. It used to be read only by the last
		// one, which made the caller's choice silently inert for the Record and Stop
		// states — a plan could say "disabled" and the window would not agree.
		if (label == "Record")
		{
			// Idle state
			BtnSpeechToTextIcon.Glyph = RecordGlyph;
			BtnSpeechToText.IsEnabled = isEnabled;
			ConfigureButtonHotkey(BtnSpeechToText, null, _settings.SpeechToTextSettings?.SpeechToTextHotKey, "Record", "Record");

			BtnSpeechToTextWithFormatIcon.Glyph = MagicGlyph;
			BtnSpeechToTextWithFormat.IsEnabled = isEnabled;
			ConfigureButtonHotkey(BtnSpeechToTextWithFormat, null, _settings.SpeechToTextSettings?.SpeechToTextWithLlmProcessingHotKey, "Record and Format", "Record and Format");
		}
		else if (label == "Stop")
		{
			// Recording state
			BtnSpeechToTextIcon.Glyph = StopGlyph;
			BtnSpeechToText.IsEnabled = isEnabled;
			ConfigureButtonHotkey(BtnSpeechToText, null, _settings.SpeechToTextSettings?.SpeechToTextHotKey, "Stop", "Stop");

			BtnSpeechToTextWithFormatIcon.Glyph = StopGlyph;
			BtnSpeechToTextWithFormat.IsEnabled = isEnabled;
			ConfigureButtonHotkey(BtnSpeechToTextWithFormat, null, _settings.SpeechToTextSettings?.SpeechToTextWithLlmProcessingHotKey, "Stop and Format", "Stop and Format");
		}
		else
		{
			// Transcribing / Processing
			BtnSpeechToTextIcon.Glyph = glyph;
			BtnSpeechToText.IsEnabled = isEnabled;
			SetBusyButtonState(
				BtnSpeechToText,
				label,
				description,
				_settings.SpeechToTextSettings?.SpeechToTextHotKey);

			BtnSpeechToTextWithFormatIcon.Glyph = glyph;
			BtnSpeechToTextWithFormat.IsEnabled = isEnabled;
			SetBusyButtonState(
				BtnSpeechToTextWithFormat,
				label,
				description,
				_settings.SpeechToTextSettings?.SpeechToTextWithLlmProcessingHotKey);
		}
	}

	/// <summary>
	/// Names a button for the state it is in, and says the same thing in its tooltip.
	/// </summary>
	/// <param name="hotkey">
	/// Named even while the button is disabled. The shortcut is registered globally, so it
	/// keeps working when the button it belongs to does not — and during a transcription
	/// that shortcut is the only way to cancel. Withholding it there would hide the escape
	/// hatch from the one user who most needs to be told about it. (AutomationProperties
	/// announces AcceleratorKey regardless, so withholding it achieved nothing anyway.)
	/// </param>
	private void SetBusyButtonState(Button button, string label, string? description, string? hotkey)
	{
		SetButtonAccessibleLabel(button, label, hotkey);

		// Left alone when the plan states no description, so a caller that only means to
		// change the name cannot silently blank a tooltip it knows nothing about.
		if (string.IsNullOrWhiteSpace(description))
			return;

		string tooltip = HotkeyAccessibleText.ComposeTooltip(description, hotkey);
		ToolTipService.SetToolTip(button, tooltip);
		AutomationProperties.SetHelpText(button, tooltip);
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
        // IsProcessingWithLlm counts as busy here, and it has to: the audio session refuses
        // to navigate during the model call, and a button announced as available that does
        // nothing at all reads to a screen-reader user as a dead control. The model call is
        // the longest wait in the flow, so this is not a window anyone tabs past quickly.
        bool busy = _audioSessionManager.IsRecording
            || _audioSessionManager.IsTranscribing
            || _audioSessionManager.IsProcessingWithLlm;

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
        // Same reason as UpdateSessionNavigationAvailability: Retry and Upload both refuse
        // during the model call, so leaving them enabled offered the user an action that
        // could only answer "finish the current operation first".
        //
        // A dictation start parked waiting for the microphone counts too (issue #312). It
        // is neither recording nor transcribing yet, but it is committed to becoming a
        // recording, and offering Retry or Upload in that window invites a second audio
        // operation to start underneath the one that is already on its way.
        bool busy = _dictationStartWaiting
            || _audioSessionManager.IsRecording
            || _audioSessionManager.IsTranscribing
            || _audioSessionManager.IsProcessingWithLlm;
        // Asked of the session rather than the speakers: a long recording spends seconds
        // decoding before it is audible, and during that window the Play button is already a
        // Stop button. Reading IsPlaying here left Retry and Upload announced as available for
        // the whole of that stretch.
        bool isPlaying = _audioSessionManager.IsPlaybackActive;

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

	/// <param name="fastModeNotice">
	/// Folded into whichever status this method ends on. This is the last status a
	/// transcript run raises, so carrying the notice here is what keeps it from being
	/// superseded — and folding it in rather than adding a second announcement is what
	/// keeps the confirmation that the text landed.
	/// </param>
	private async void FinalizeTranscript(
		string rawText,
		string successMessage,
		string? formattedText = null,
		FastModeFallbackReason? fastModeNotice = null)
	{
		// The restoration below is what gives the user their transcript box back. It ran
		// last, unguarded, so any throw on the delivery path — a beep, an automation peer
		// inside ShowStatus — left the box read-only and auto-formatting suppressed for
		// the rest of the session, with nothing said about why (#234). It now runs in a
		// finally, and the failure is announced rather than left to the global handler.
		await GuardedUiOperation.RunAsync(
			work: async () =>
			{
				string formatted = formattedText ?? _transcriptFormatter.ApplyRules(rawText, false);

				TxtRawTranscript.Text = rawText;
				TxtFormatTranscript.Text = formatted;

				bool copied = await _clipboard.TrySetTextAsync(formatted);
				var outcome = await TryInsertIntoActiveApplicationAsync(formatted, clipboardAvailable: copied);
				var plan = TranscriptCompletionPlanner.Plan(copied, outcome, "transcript");

				// Ahead of the beep and the status rather than after them. A throw from
				// either lands in onFailure below, which announces that the delivery went
				// wrong — but the transcript is on the clipboard and already pasted by
				// then, and the user's shortcut would have been lost with it (issue #335).
				if (plan.SendConfiguredHotkey)
					HotkeyManager.SendHotkeyAfterDelay(
						_settings.SpeechToTextSettings?.SendHotkeyAfterTranscriptionOperation,
						PostOperationHotkey.SuccessDelayMs);

				BeepPlayer.Play(plan.Beep);
				ShowStatus("Speech to Text",
					FastModeMessages.AppendTo(plan.FailureMessage ?? successMessage, fastModeNotice),
					plan.Succeeded ? InfoBarSeverity.Success : InfoBarSeverity.Error);
			},
			onFailure: ex =>
			{
				ErrorLogger.LogError("FinalizeTranscript", ex);
				BeepPlayer.Play(BeepType.Failure);
				ShowStatus("Speech to Text",
					"Something went wrong while delivering the transcript. It is available in the Mutation window.",
					InfoBarSeverity.Error);
			},
			restore: () =>
			{
				TxtRawTranscript.IsReadOnly = false;
				_suppressAutoActions = false;
				UpdateRecordingActionAvailability();
				ScheduleSessionCleanup();
			},
			onReportFailed: ex => ErrorLogger.LogError("FinalizeTranscript failure report", ex));
	}

    // Driven by the activity the session says it is entering, never by re-reading the
    // recorder's flags. This handler is dispatched, so it runs after the raising code
    // has moved on: reading IsRecording here once let a stop be greeted with the start
    // beep and "Recording..." — the opposite of what the user had just done (#271).
    private void AudioSessionManager_StateChanged(object? sender, RecordingActivity activity)
    {
        DispatcherQueue.TryEnqueue(() =>
        {
            UpdateRecordingActionAvailability();

            var plan = RecordingUiPlanner.For(activity);
            UpdateSpeechButtonVisuals(plan.ButtonLabel, GlyphFor(activity), plan.ButtonEnabled, plan.ButtonDescription);
            TxtRawTranscript.IsReadOnly = plan.TranscriptReadOnly;
            // Null means "leave whatever is in the box"; empty means "clear it", which is
            // how a cancelled run takes its own "Transcribing..." back out (#295).
            if (plan.TranscriptText is string transcriptText)
                TxtRawTranscript.Text = transcriptText;
            if (plan.PlayStartBeep)
                BeepPlayer.Play(BeepType.Start);
        });
    }

    private static string GlyphFor(RecordingActivity activity) => activity switch
    {
        RecordingActivity.Recording => StopGlyph,
        RecordingActivity.Transcribing => ProcessingGlyph,
        // A stop, because that is what pressing it now does. Falling through to the default
        // would have shown the record glyph on a button whose accessible name says "Stop
        // LLM processing" — the icon and the name disagreeing about the same control.
        RecordingActivity.ProcessingWithLlm => StopGlyph,
        _ => RecordGlyph,
    };

    private void AudioSessionManager_TranscriptReady(object? sender, TranscriptResult result)
    {
        DispatcherQueue.TryEnqueue(() =>
        {
            FinalizeTranscript(
                result.RawText,
                // The cancel is folded into the delivery line rather than announced on its
                // own, because this is the last status the run raises — anything said
                // earlier is superseded by it and heard by nobody.
                result.LlmCancelled
                    ? CancellationMessages.LlmCancelledThen("Transcript ready.")
                    : "Transcript ready.",
                result.FormattedText,
                result.FastModeNotice);
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

	// State transitions push their label into this cache; hotkey-only refreshes read it
	// back, so they re-compose from the current state rather than resurrecting the name
	// the button had at startup (issue #214).
	private readonly HotkeyButtonLabelCache _buttonAccessibleLabels = new();

	/// <param name="accessibleLabel">
	/// The button's label in its current state. Pass this from anywhere the button
	/// changes state; omit it for a hotkey-only refresh, which then keeps whatever
	/// label the button already has.
	/// </param>
	private void ConfigureButtonHotkey(Button button, TextBlock? hotkeyTextBlock, string? hotkey, string baseTooltip, string? accessibleLabel = null)
	{
		string label = ResolveAccessibleLabel(button, baseTooltip, accessibleLabel);

		AutomationProperties.SetName(button, HotkeyAccessibleText.ComposeName(label, hotkey));

		string tooltip = HotkeyAccessibleText.ComposeTooltip(baseTooltip, hotkey);
		ToolTipService.SetToolTip(button, tooltip);
		AutomationProperties.SetHelpText(button, tooltip);
		AutomationProperties.SetAcceleratorKey(button, string.IsNullOrWhiteSpace(hotkey) ? string.Empty : hotkey);
		UpdateHotkeyText(hotkeyTextBlock, hotkey);
	}

	private string ResolveAccessibleLabel(Button button, string baseTooltip, string? accessibleLabel) =>
		_buttonAccessibleLabels.Resolve(button, AutomationProperties.GetName(button), baseTooltip, accessibleLabel);

	// Sets a state label on a button whose hotkey affordances are not being touched
	// (e.g. the disabled "Transcribing…" state), keeping the label cache in step so a
	// later hotkey refresh does not undo it.
	//
	// The cache holds BARE labels — ConfigureButtonHotkey composes the shortcut in when it
	// refreshes, reading whatever is cached. Handing it a name that already had the
	// shortcut in it made it compose a second one on the next refresh, so a settings save
	// during the model call left the button announcing "Stop LLM processing, SHIFT+ALT+U,
	// SHIFT+ALT+U" (issue #309). The composition happens here and only here.
	private void SetButtonAccessibleLabel(Button button, string label, string? hotkey = null)
	{
		_buttonAccessibleLabels.Set(button, label);
		AutomationProperties.SetName(button, HotkeyAccessibleText.ComposeName(label, hotkey));
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
			hotkeyTextBlock.Text = HotkeyAccessibleText.ComposeHotkeyText(hotkey);
			hotkeyTextBlock.Visibility = Visibility.Visible;
		}
	}



        private void ShowStatus(string title, string message, InfoBarSeverity severity)
        {
		// Several callers pass an exception's own message straight through, so the
		// InfoBar is the same redaction bypass the error dialog was (issue #242).
		// Redacting once here covers the bar, its HelpText, and the announcement. It
		// is a no-op for the literal strings most callers pass.
		message = ErrorLogger.RedactSecrets(message ?? string.Empty);

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

	// The debounced settings save runs on a fire-and-forget task, so a failed write
	// used to be completely silent — the setting simply reverted on the next launch
	// (issue #233). Surfaced on the same channel as every other status: the InfoBar
	// for sighted users, a UIA notification for the screen reader, and the failure
	// beep for anyone not looking at the window at all.
	private void ReportSettingsSaveFailure(Exception exception)
	{
		ErrorLogger.LogError("Settings save", exception);

		ShowStatus(
			SettingsFailureFeedback.BackgroundSaveTitle,
			SettingsFailureFeedback.ComposeBackgroundSaveMessage(exception),
			InfoBarSeverity.Error);

		BeepPlayer.Play(BeepType.Failure);
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
		// The full exception chain goes to the log and nowhere else. The log is
		// redacted and stays on the machine; this text is read aloud and pasted into
		// bug reports, so it gets the exception's own message, redacted, plus where
		// to find the rest (issue #242). Logging here also means every caller of this
		// method leaves a record without having to remember to.
		ErrorLogger.LogError(title, ex);
		string message = ErrorDialogMessage.ForException(ex, ErrorLogger.PrimaryLogPath);
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
        => (await ShowDialogCoreAsync(dialog)).Result;

    /// <summary>
    /// Shows a dialog and reports whether it actually made it on screen. Callers that
    /// owe the user a message they must acknowledge use this so they can fall back to
    /// another surface: a failed show otherwise degrades to a status-bar line, which is
    /// not an acknowledgement and can pass unnoticed entirely.
    /// </summary>
    private async Task<bool> TryShowDialogAsync(ContentDialog dialog)
        => (await ShowDialogCoreAsync(dialog)).Shown;

    private async Task<(ContentDialogResult Result, bool Shown)> ShowDialogCoreAsync(ContentDialog dialog)
    {
        // A dialog requested while another is open is announced now and queued;
        // the queue shows it when the current dialog closes rather than
        // dropping it (issue #167).
        if (_dialogQueue.IsBusy)
            AnnouncePendingDialog(dialog);

        try
        {
            return (await _dialogQueue.EnqueueAsync(async () => await dialog.ShowAsync()), true);
        }
        catch (Exception ex)
        {
            // Fallback safety if something else goes wrong with the dialog
            ShowStatus("Dialog Error", $"Failed to show dialog: {ex.Message}", InfoBarSeverity.Error);
            return (ContentDialogResult.None, false);
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

	// The user picked a different microphone. Everything on this path that can block
	// on the device — resolving it over winmm, and closing and reopening the capture
	// handle — runs on the switch coordinator's background worker, so a USB device
	// mid-reconnect, a Bluetooth headset, or a wedged driver can no longer freeze the
	// window and the screen reader with it (issue #267).
	//
	// Nothing here disables or reassigns the combo, so a screen-reader user keeps
	// their focus and their selection for the whole switch, however long it takes;
	// what they get is the outcome, announced through the status bar, rather than a
	// dead window and then silently dead capture.
	private async void CmbMicrophone_SelectionChanged(object sender, SelectionChangedEventArgs e)
	{
		// A programmatic rebuild of the list re-raises this; it is not a user choice.
		if (_suppressMicrophoneSelection)
			return;

		if (CmbMicrophone.SelectedItem is not Mutation.Ui.Core.CaptureDeviceInfo device)
		{
			// Releasing the handle is a winmm call like any other, so it goes through
			// the same worker — and queueing it there is also what keeps it ordered
			// against a switch that may still be in flight.
			_requestedMicrophoneId = null;
			_ = _microphoneSwitch.ReleaseAsync();
			return;
		}

		// Backstop for the flag above: if WinUI ever defers the SelectionChanged raised
		// by an ItemsSource swap, it lands after the flag has been cleared. Re-running
		// the pipeline for the device already selected would save settings, drop and
		// reopen capture, and re-probe the level controls — an audible glitch and a
		// screen-reader interruption for a selection that did not change.
		//
		// Compared against what was last *asked for*, not what the device manager has
		// settled on: the switch runs on a worker and the manager lags behind it, so
		// asking the manager would skip a genuine change of mind — pick B, go back to
		// A while B is still opening, and A looks like the current device — and leave
		// the combo naming one microphone while another is live.
		if (string.Equals(device.Id, RequestedOrSelectedMicrophoneId(), StringComparison.OrdinalIgnoreCase))
			return;

		_requestedMicrophoneId = device.Id;

		if (_settings.AudioSettings != null)
		{
			// Written into the settings object before the switch is attempted, not
			// after it succeeds: closing the window mid-switch runs PersistClosingState
			// first, and an assignment left to the continuation would never make it
			// into that save. A switch that then fails puts the previous name back
			// below, so an optimistic write cannot outlive the attempt.
			//
			// The file write itself is debounced. The save serializes the whole
			// settings object and atomically replaces the file plus its .bak, and that
			// disk I/O has no business on the UI thread.
			_settings.AudioSettings.ActiveCaptureDeviceFullName = device.FriendlyName;
			_settingsSaveDebouncer.Trigger();
		}

		// Selected by ID: the manager re-resolves the live device out of its current
		// enumeration, so a list entry that predates a hot-plug cannot become the
		// selection (issue #264).
		//
		// A null result means a newer selection superseded this one. That switch owns
		// the settings, the capture, and the level controls now, so this one reports
		// nothing — otherwise the controls would end up describing a device the user
		// has already moved on from.
		if (await _microphoneSwitch.SwitchAsync(device.Id) is not Mutation.Ui.Core.MicrophoneSwitchResult result)
			return;

		// The window closed while the device was being opened; its controls are torn
		// down and there is nobody left to tell.
		if (_isClosing)
			return;

		if (!result.Switched)
			RollBackFailedMicrophoneSwitch(device);

		switch (result.Outcome)
		{
			case Mutation.Ui.Core.MicrophoneSwitchOutcome.Switched:
				// This switch opened capture, so there is no longer a startup start
				// waiting to be made good.
				_startupCapturePending = false;

				// Re-sync the level controls to the newly-selected device (support and
				// current level may differ) and re-assert the pinned level on it.
				if (_micLevelInitialized)
					InitializeMicrophoneLevelControls();
				break;

			case Mutation.Ui.Core.MicrophoneSwitchOutcome.Unavailable:
				ShowStatus("Microphone",
					$"{device.FriendlyName} is no longer available. Choose another microphone.",
					InfoBarSeverity.Warning);
				break;

			default:
				// A device fault must reach the user as a status message: capture is
				// dead, and without this they would be left dictating into nothing. The
				// coordinator has already logged the exception behind the message.
				ShowStatus("Microphone",
					$"Could not switch to {device.FriendlyName}: {result.FailureMessage}",
					InfoBarSeverity.Error);
				break;
		}
	}

	// The microphone the selection path is currently working towards. Falls back to
	// the device manager's own selection when nothing has been asked for yet — at
	// startup, before the persisted choice is restored.
	private string? RequestedOrSelectedMicrophoneId() =>
		_requestedMicrophoneId ?? _audioDeviceManager.SelectedMicrophone?.Id;

	// Undoes what a switch that did not take left behind. The manager is still on the
	// previous device, so what the UI thread wrote ahead of the attempt has to follow
	// it back: the requested ID, or the next pick would be compared against a
	// microphone that is not the one being recorded from, and the persisted name, or
	// one transient device fault destroys the last choice that worked and every later
	// launch starts on the microphone that failed.
	//
	// Skipped entirely if the user has already asked for something else in the
	// meantime: that request is the one that owns this state now, and because all of
	// this is UI-thread-serial it has already written both fields itself.
	//
	// Does not cover a window closed mid-switch. PersistClosingState runs as the close
	// sequence's first step, so by the time a failure lands there is nothing left to
	// roll back into — the optimistic name is already on disk. That costs one startup
	// on the device that failed, which then rolls back for real. The alternative,
	// writing the name only on success, loses the choice every time a *successful*
	// switch is still running at close, and that is the common case.
	private void RollBackFailedMicrophoneSwitch(Mutation.Ui.Core.CaptureDeviceInfo attempted)
	{
		if (!string.Equals(_requestedMicrophoneId, attempted.Id, StringComparison.OrdinalIgnoreCase))
			return;

		var live = _audioDeviceManager.SelectedMicrophone;
		_requestedMicrophoneId = live?.Id;

		// Only over a name worth having. With no live device there is nothing better
		// to record, and blanking the setting would erase the user's choice for good —
		// the next launch would silently adopt whatever device sorts first, every
		// time. Keeping the name they last picked at least gets them back onto it when
		// the device returns.
		if (_settings.AudioSettings != null && !string.IsNullOrWhiteSpace(live?.FriendlyName))
		{
			_settings.AudioSettings.ActiveCaptureDeviceFullName = live.FriendlyName;
			_settingsSaveDebouncer.Trigger();
		}

		// Capture is only re-opened when the startup start was skipped in favour of
		// this switch, so nothing is running at all. A mid-session failure is left
		// alone on purpose: the switch never reached its own capture restart, which
		// means the previous device is still open and streaming, and cycling
		// waveInClose/waveInOpen on a healthy handle to recover from someone else's
		// failure is how a user with working capture ends up with none. A live device
		// is required too — without one the restart could only report "device not
		// resolved", talking over the message that actually tells them what to do.
		if (_startupCapturePending && live is not null)
		{
			// Cleared as the recovery is queued, not when it lands: the flag's job is
			// to make this happen once, and a request that is queued to open capture
			// has done that job. Leaving it set would re-arm the restart on the next
			// failed switch, and that one would be cycling a handle this one opened —
			// the healthy-capture teardown this guard exists to prevent, reached the
			// long way round. A recovery that fails reports itself through
			// StartCapture; it does not get retried by a later, unrelated failure.
			_startupCapturePending = false;
			_ = _microphoneSwitch.RestartCaptureAsync();
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
		if (CmbInsertOption.SelectedItem is DictationInsertOptionItem selected)
		{
			DictationInsertOption opt = selected.Option;
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

	// False until the saved preference has been restored. See where it is set.
	private bool _announceThirdPartyExplanation;

	private void UpdateThirdPartyExplanation(DictationInsertOption option)
	{
		string explanation = option switch
		{
			DictationInsertOption.DoNotInsert => DoNotInsertExplanation,
			DictationInsertOption.SendKeys => SendKeysExplanation,
			DictationInsertOption.Paste => PasteExplanation,
			_ => string.Empty
		};

		if (ThirdPartyExplanationText.Text == explanation)
			return;

		ThirdPartyExplanationText.Text = explanation;

		if (!_announceThirdPartyExplanation)
			return;

		// The live setting on the TextBlock says the text is worth announcing; it does not
		// announce it. WinUI raises nothing of its own when a TextBlock's Text changes, so
		// without this the explanation for the newly chosen mode is silent and the selection
		// change is the last thing the user hears (issue #243).
		AutomationPeer? peer = FrameworkElementAutomationPeer.FromElement(ThirdPartyExplanationText)
			?? FrameworkElementAutomationPeer.CreatePeerForElement(ThirdPartyExplanationText);
		peer?.RaiseAutomationEvent(AutomationEvents.LiveRegionChanged);
	}

	// Reports how far the text actually got. Every path that needs no insert — an empty
	// transcript, Mutation itself in front, the "do not insert" option — is Delivered.
	//
	// The injection is awaited rather than fired and forgotten (#232), so a short count
	// from SendInput is seen before anything announces success; a caller that had already
	// announced by then would have told a blind user their dictation landed in a window
	// that never received it. The await keeps the work off the UI thread — it runs on the
	// thread pool and the UI thread returns to its message pump — and it is bounded:
	// SendInput is synchronous, and the hotkey path waits at most
	// ModifierReleaseTimeoutMs for the user to let go of the chord they triggered this
	// with.
	//
	// The short count is not the whole story, which is why each branch below asks a
	// question first. When the window in front runs at a higher integrity level, Windows
	// takes the events and discards them with no failure reported anywhere, so the
	// elevated-app case has to be detected before sending rather than after (issue #294).
	private async Task<TranscriptDeliveryOutcome> TryInsertIntoActiveApplicationAsync(string text, bool clipboardAvailable = true)
	{
		if (string.IsNullOrWhiteSpace(text))
			return TranscriptDeliveryOutcome.Delivered;

		if (IsThisWindowInForeground())
			return TranscriptDeliveryOutcome.Delivered;

		switch (_insertOption)
		{
			case DictationInsertOption.SendKeys:
				// Asked before sending, because afterwards there is nothing to see. Windows
				// discards input aimed at a window above us without saying so — SendInput
				// accepts the events, returns the full count, and they are dropped further
				// down. The count check inside SendText catches input genuinely refused, but
				// not this, which is the elevated-app case the failure was filed about
				// (issue #294).
				if (ForegroundIntegrityProbe.ForegroundWindowWillDiscardInput())
					return TranscriptDeliveryOutcome.InjectionFailed;
				BeepPlayer.Play(BeepType.Start);
				bool typed = await Task.Run(() => HotkeyManager.SendText(text));
				return typed ? TranscriptDeliveryOutcome.Delivered : TranscriptDeliveryOutcome.InjectionFailed;
			case DictationInsertOption.Paste:
				// Pasting sends Ctrl+V, so the text must actually be on the
				// clipboard; retry the write here if the earlier copy failed.
				if (!clipboardAvailable && !await _clipboard.TrySetTextAsync(text))
					return TranscriptDeliveryOutcome.ClipboardBlocked;
				// After the clipboard write, not before it. The failure message tells the
				// user to paste the transcript themselves, so the text has to be somewhere
				// they can paste it from even when we already know Ctrl+V will not land.
				if (ForegroundIntegrityProbe.ForegroundWindowWillDiscardInput())
					return TranscriptDeliveryOutcome.InjectionFailed;
				// "Ctrl+V" (not "^v"): Hotkey.Parse has no caret syntax, so the
				// literal would throw and drop to the SendKeys.SendWait fallback.
				bool pasted = await Task.Run(() => HotkeyManager.SendHotkey("Ctrl+V"));
				return pasted ? TranscriptDeliveryOutcome.Delivered : TranscriptDeliveryOutcome.InjectionFailed;
		}

		return TranscriptDeliveryOutcome.Delivered;
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

	/// <summary>
	/// Says which of the five things a plain screenshot capture did. Shared by the shortcut and
	/// the button, so both tell the user the same thing.
	/// <para>
	/// A busy clipboard is a status line, not an error dialog. It used to be a dialog, because
	/// the failure arrived here as a thrown exception (issue #360) — and a modal box the user has
	/// to dismiss is the wrong weight for something that clears on its own in under a second.
	/// </para>
	/// </summary>
	private void AnnounceScreenshotOutcome(ScreenshotToClipboardOutcome outcome)
	{
		var (message, severity) = ClipboardCopyMessages.ForScreenshot(outcome);
		ShowStatus("Screenshot", message, severity);
	}

	/// <summary>
	/// Puts an OCR run's answer in front of the user: the text, or the error, in the OCR box;
	/// then the shortcut configured to run afterwards; then a word about the clipboard if it
	/// would not take the text.
	/// <para>
	/// One method for all eight single-image paths rather than the same three lines eight times.
	/// A ninth path added without the send is how batch OCR came to have none (issue #335), and
	/// the two things this now decides — that a refused capture sends nothing (issue #342), and
	/// that a failed copy is said out loud (issue #341) — are decided once instead of eight
	/// times.
	/// </para>
	/// <para>
	/// The send goes before the status, not after. A status builds an automation peer and starts
	/// a timer, the operation issue #234 was filed about throwing, and a throw there used to take
	/// the shortcut with it after the text had already landed.
	/// </para>
	/// </summary>
	/// <param name="statusTitle">
	/// The heading for this path's status line, or null for the four hotkey paths, which have
	/// no status of their own: the shortcut runs, the reader reads the OCR box, and a status
	/// would be one more thing between the user and the answer. A failed copy is said even
	/// then, because it is the only way they would learn of it.
	/// </param>
	private void PublishOcrResult(
		OcrResult result,
		string? statusTitle = null,
		string? successMessage = null,
		InfoBarSeverity failureSeverity = InfoBarSeverity.Error)
	{
		SetOcrText(result.Message);

		bool paste = ShouldPasteOcrText(result.Success, result.ClipboardCopyFailed, result.Message);
		if (PostOperationHotkey.ShouldSendAfterOcr(result.Outcome))
			HotkeyManager.SendHotkeyAfterDelay(
				PostOperationHotkey.AfterOcr(paste, _settings.AzureComputerVisionSettings?.SendHotkeyAfterOcrOperation),
				PostOperationHotkey.OcrDelay(result.Success));

		// A clipboard warning outranks the success line, and says so here rather than being
		// followed by it. The four button paths used to announce their own success afterwards,
		// which would leave the user believing a copy that did not happen. Which warning, and
		// whether there is one at all, is decided by ClipboardCopyMessages.
		string? clipboardWarning = ClipboardCopyMessages.ForOcrRun(
			result.Success, result.ClipboardCopyFailed, result.ScreenshotCopyFailed);

		if (clipboardWarning is not null)
		{
			ShowStatus(statusTitle ?? "OCR", clipboardWarning, InfoBarSeverity.Warning);
			return;
		}

		if (statusTitle is null)
			return;

		if (result.Success)
		{
			ShowStatus(statusTitle, successMessage ?? string.Empty, InfoBarSeverity.Success);
			return;
		}

		// Which severity, and why, is decided by ClipboardCopyMessages — a refused run is not a
		// failure and must not be announced as one (issue #367). Kept out here so it is pinned
		// by a test rather than by whoever next reads this line.
		ShowStatus(
			statusTitle,
			result.Message,
			ClipboardCopyMessages.ForOcrRunSeverity(result.Outcome, failureSeverity));
	}

	/// <summary>
	/// Whether an OCR run should paste its text into the application the user was working in
	/// before the shortcut configured to run afterwards.
	/// <para>
	/// Two ways the answer is no even when the setting is on. Either the clipboard does not
	/// hold this run's text — see <see cref="PostOperationHotkey.ClipboardHoldsOcrText"/>, which
	/// is where that gets decided — or this window still has the keyboard, which means the run
	/// was started from a button here and the paste would land in Mutation rather than anywhere
	/// the user meant.
	/// </para>
	/// </summary>
	private bool ShouldPasteOcrText(bool success, bool clipboardCopyFailed, string? text) =>
		_settings.AzureComputerVisionSettings?.PasteOcrTextIntoActiveApplication == true
		&& PostOperationHotkey.ClipboardHoldsOcrText(success, clipboardCopyFailed, text)
		&& !IsThisWindowInForeground();

	/// <summary>
	/// True when Mutation's own window has the keyboard, so injected keystrokes aimed at "the
	/// active application" would land back here. Treated as false when the handle cannot be
	/// read: the delivery paths would rather try and fail visibly than silently do nothing.
	/// </summary>
	private bool IsThisWindowInForeground()
	{
		var windowHandle = WindowNative.GetWindowHandle(this);
		return windowHandle != IntPtr.Zero && GetForegroundWindow() == windowHandle;
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

	/// <summary>
	/// Opens the user guide's contents page in whatever browser the user has set as
	/// their default. The guide ships beside the executable, so this works offline.
	/// </summary>
	private async void UserGuideButton_Click(object sender, RoutedEventArgs e)
	{
		UserGuideLocator.Result guide = UserGuideLocator.Locate(AppContext.BaseDirectory);

		if (!guide.Found)
		{
			BeepPlayer.Play(BeepType.Failure);
			ShowStatus("User guide", guide.ErrorMessage!, InfoBarSeverity.Warning);
			return;
		}

		try
		{
			// Handing a file to the shell can block while the browser starts, so it
			// stays off the UI thread.
			await Task.Run(() =>
			{
				using var _ = System.Diagnostics.Process.Start(
					new System.Diagnostics.ProcessStartInfo(guide.IndexPath) { UseShellExecute = true });
			});

			ShowStatus("User guide", "Opening the user guide in your browser.", InfoBarSeverity.Informational);
		}
		catch (Exception ex)
		{
			ErrorLogger.LogError(nameof(UserGuideButton_Click), ex);
			BeepPlayer.Play(BeepType.Failure);
			ShowStatus(
				"User guide",
				"The user guide could not be opened. Check that a web browser is set as the default for .html files.",
				InfoBarSeverity.Warning);
		}
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

		try
		{
			// A save rewrites the prompts in place; the ListView needs re-pointing at
			// them or it keeps rendering pre-save content (issue #219).
			_promptLibrary?.RebindPromptList();
		}
		catch (Exception ex)
		{
			System.Diagnostics.Debug.WriteLine($"ApplyLiveSettings (prompts) failed: {ex.Message}");
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
			const string title = "Some hotkeys could not be registered";
			string message = HotkeyManager.BuildFailureMessage(failures);

			// Shown on whichever surface is available. This used to return early when
			// the window had no XamlRoot yet — which is the state startup is in when it
			// registers the core hotkeys — so the failure beep above played and the list
			// of dead hotkeys was never shown to anyone.
			await ShowNoticeAsync(title, message, "OK", NoticeSeverity.Warning);
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
		// Already unsubscribed by MainWindow_Closed on the normal path; repeated here so
		// a Dispose that did not come through it still detaches the handler.
		_audioDeviceManager.CaptureDeviceListChanged -= AudioDeviceManager_CaptureDeviceListChanged;
		_textToSpeech.SpeakFailed -= TextToSpeech_SpeakFailed;
		_audioSessionManager?.Dispose();
		_microphoneVisualization?.Dispose();
		_formatDebounceCts?.Dispose();
		_promptDebounceCts?.Dispose();
		_settingsSaveDebouncer?.Dispose();
		_ocrDocumentsRun.Dispose();
		// Before _shutdownCts: this source is linked to it, and cancelling as it disposes
		// is what lets an in-flight prompt run unwind rather than be abandoned.
		_promptLlmOperation.Dispose();
		_shutdownCts.Dispose();
		_statusDismissTimer?.Stop();
	}
}
