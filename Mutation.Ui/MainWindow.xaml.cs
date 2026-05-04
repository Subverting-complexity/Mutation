using CognitiveSupport;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
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
	private readonly TranscriptFormatter _transcriptFormatter;
	private readonly ITextToSpeechService _textToSpeech;
	private readonly Settings _settings;
	private HotkeyManager? _hotkeyManager;
	private MicrophoneVisualizationController? _microphoneVisualization;
	private PromptLibraryController? _promptLibrary;

	// Suppress auto-format/clipboard/beep when we change text programmatically or during record/transcribe
	private bool _suppressAutoActions = false;

	private ISpeechToTextService? _activeSpeechService;
	private CancellationTokenSource _formatDebounceCts = new();
	private CancellationTokenSource _promptDebounceCts = new();
	private readonly CancellationTokenSource _shutdownCts = new();
	private DictationInsertOption _insertOption = DictationInsertOption.Paste;
	private readonly DispatcherTimer _statusDismissTimer;
	private bool _isDialogOpen;
	private HotkeyRouterController? _hotkeyRouter;

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

	public ObservableCollection<HotkeyRouterEntry> HotkeyRouterEntries { get; } = new();

	public MainWindow(
		ClipboardManager clipboard,
		UiStateManager uiStateManager,
		AudioDeviceManager audioDeviceManager,
		OcrManager ocrManager,
		ISpeechToTextService[] speechServices,
		ITextToSpeechService textToSpeech,
		TranscriptFormatter transcriptFormatter,

		ISettingsManager settingsManager,
		Settings settings,
		Mutation.Ui.Core.AudioSessionManager audioSessionManager)
	{
		_clipboard = clipboard;
		_uiStateManager = uiStateManager;
		_settingsManager = settingsManager;
		_audioDeviceManager = audioDeviceManager;
		_ocrManager = ocrManager;
		_speechServices = speechServices;
		_textToSpeech = textToSpeech;
		_transcriptFormatter = transcriptFormatter;
		_settings = settings;

        _audioSessionManager = audioSessionManager;
        _audioSessionManager.StateChanged += AudioSessionManager_StateChanged;
        _audioSessionManager.TranscriptReady += AudioSessionManager_TranscriptReady;
        _audioSessionManager.ErrorOccurred += AudioSessionManager_ErrorOccurred;
        _audioSessionManager.StatusMessage += AudioSessionManager_StatusMessage;
        _audioSessionManager.SelectedSessionChanged += AudioSessionManager_SelectedSessionChanged;
        _audioSessionManager.PlaybackStarted += AudioSessionManager_PlaybackStarted;
        _audioSessionManager.PlaybackStopped += AudioSessionManager_PlaybackStopped;

        InitializeComponent();
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

		if (_settings.LlmSettings != null)
		{
			CmbLlmModel.ItemsSource = _settings.LlmSettings.Models;
			if (!string.IsNullOrEmpty(_settings.LlmSettings.SelectedLlmModel) && _settings.LlmSettings.Models.Contains(_settings.LlmSettings.SelectedLlmModel))
			{
				CmbLlmModel.SelectedItem = _settings.LlmSettings.SelectedLlmModel;
			}
			else if (_settings.LlmSettings.Models.Any())
			{
				CmbLlmModel.SelectedIndex = 0;
				_settings.LlmSettings.SelectedLlmModel = _settings.LlmSettings.Models[0];
			}
		}

            _promptLibrary = new PromptLibraryController(
                _settings,
                _settingsManager,
                _transcriptFormatter,
                LstPrompts,
                ExecutePrompt);
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

		InitializeHotkeyVisuals();
		_hotkeyRouter = new HotkeyRouterController(
			HotkeyRouterEntries,
			_settings,
			_settingsManager,
			DispatcherQueue,
			HotkeyRouterList);
		_hotkeyRouter.Initialize();

		this.Closed += MainWindow_Closed;
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
		yield return TxtClipboard;
	}

	public void AttachHotkeyManager(HotkeyManager hotkeyManager)
	{
		_hotkeyManager = hotkeyManager;
		_hotkeyRouter?.AttachHotkeyManager(hotkeyManager);
		_promptLibrary?.AttachHotkeyManager(hotkeyManager);
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
		ConfigureButtonHotkey(BtnTextToSpeech, BtnTextToSpeechHotkey, _settings.TextToSpeechSettings?.TextToSpeechHotKey, "Play the clipboard text using text-to-speech");
		ConfigureButtonHotkey(BtnFormatLlm, null, null, "Send transcript through the configured language model");
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

		var normalizedPairs = _hotkeyRouter?.SyncSettings() ?? new List<(string From, string To)>();
		_settingsManager.SaveSettingsToFile(_settings);
		_hotkeyRouter?.UpdateSnapshot(normalizedPairs);
        
        _audioSessionManager.Dispose();
        
		BeepPlayer.DisposePlayers();
		Dispose();
	}

	private void CopyText_Click(object sender, RoutedEventArgs e)
	{
		_clipboard.SetText(TxtClipboard.Text);
		ShowStatus("Clipboard", "Text copied to the clipboard.", InfoBarSeverity.Success);
	}

	private void BtnAddHotkeyRoute_Click(object sender, RoutedEventArgs e) =>
		_hotkeyRouter?.AddNewMapping();

	private void HotkeyRouterDelete_Click(object sender, RoutedEventArgs e) =>
		_hotkeyRouter?.DeleteMapping(sender);

	private void HotkeyRouterFrom_LostFocus(object sender, RoutedEventArgs e) =>
		_hotkeyRouter?.CommitFromLostFocus(sender);

	private void HotkeyRouterTo_LostFocus(object sender, RoutedEventArgs e) =>
		_hotkeyRouter?.CommitToLostFocus(sender);

	public void BtnToggleMic_Click(object? sender, RoutedEventArgs? e)
	{
		_audioDeviceManager.ToggleMute();
		UpdateMicrophoneToggleVisuals();
		ShowStatus("Microphone", _audioDeviceManager.IsMuted ? "Microphone muted." : "Microphone is live.",
			_audioDeviceManager.IsMuted ? InfoBarSeverity.Warning : InfoBarSeverity.Success);
		BeepPlayer.Play(_audioDeviceManager.IsMuted ? BeepType.Mute : BeepType.Unmute);
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

	public async Task StartStopSpeechToTextAsync(bool useLlmFormatting = false)
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
            
            string llmPromptContent = _promptLibrary?.GetAutoRunPromptContent() ?? string.Empty;
            await _audioSessionManager.StartStopRecordingAsync(_activeSpeechService, useLlmFormatting, GetActivePrompt(), llmPromptContent, _shutdownCts.Token);
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
		string text = await _clipboard.GetTextAsync();
		_textToSpeech.SpeakText(text);
		ShowStatus("Text to Speech", "Speaking clipboard text.", InfoBarSeverity.Informational);
	}

	public void BtnFormatTranscript_Click(object? sender, RoutedEventArgs? e)
	{
		string raw = TxtRawTranscript.Text;
		string formatted = _transcriptFormatter.ApplyRules(raw, false);
		TxtFormatTranscript.Text = formatted;
		_clipboard.SetText(formatted);
		InsertIntoActiveApplication(formatted);
		BeepPlayer.Play(BeepType.Success);
		ShowStatus("Formatting", "Transcript formatted and copied.", InfoBarSeverity.Success);
	}

	public async void BtnFormatLlm_Click(object? sender, RoutedEventArgs? e)
	{
		try
		{
            // Use the prompt marked as AutoRun, or the first available one, or prompt user?
            // For now, let's use the AutoRun prompt if available.
            var prompt = _settings.LlmSettings?.Prompts.FirstOrDefault(p => p.AutoRun) 
                         ?? _settings.LlmSettings?.Prompts.FirstOrDefault();
                         
            if (prompt == null)
            {
                ShowStatus("Formatting", "No prompts configured.", InfoBarSeverity.Warning);
                return;
            }

            // If triggered manually, maybe we want to run the specific prompt logic directly
            ExecutePrompt(prompt);
		}
		catch (Exception ex)
		{
			ShowStatus("Formatting", ex.Message, InfoBarSeverity.Error);
			await ShowErrorDialog("Format with LLM Error", ex);
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
			TxtFormatTranscript.Text = "Formatting...";
			string raw = await _clipboard.GetTextAsync();
			if (string.IsNullOrWhiteSpace(raw))
			{
				ShowStatus("Formatting", "Clipboard is empty.", InfoBarSeverity.Warning);
				TxtFormatTranscript.Text = string.Empty;
				return;
			}

			string modelName = _settings.LlmSettings?.SelectedLlmModel ?? LlmSettings.DefaultModel;
			string formatted = await _transcriptFormatter.FormatWithLlmAsync(raw, prompt.Content, modelName);
            
			TxtFormatTranscript.Text = formatted;
			_clipboard.SetText(formatted);
			InsertIntoActiveApplication(formatted);
			BeepPlayer.Play(BeepType.Success);
			ShowStatus("Formatting", $"Applied prompt '{prompt.Name}' with the language model.", InfoBarSeverity.Success);
			HotkeyManager.SendHotkeyAfterDelay(_settings.SpeechToTextSettings?.SendHotkeyAfterTranscriptionOperation, Constants.SendHotkeyDelay);
        }
        catch (Exception ex)
        {
             ShowStatus("Formatting Failed", ex.Message, InfoBarSeverity.Error);
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
			ConfigureButtonHotkey(BtnSpeechToTextWithFormat, null, _settings.SpeechToTextSettings?.SpeechToTextWithLlmFormattingHotKey, "Record and Format");
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
			ConfigureButtonHotkey(BtnSpeechToTextWithFormat, null, _settings.SpeechToTextSettings?.SpeechToTextWithLlmFormattingHotKey, "Stop and Format");
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

	private void FinalizeTranscript(string rawText, string successMessage, string? formattedText = null)
	{
		string formatted = formattedText ?? _transcriptFormatter.ApplyRules(rawText, false);

		TxtRawTranscript.Text = rawText;
		TxtFormatTranscript.Text = formatted;

		_clipboard.SetText(formatted);
		InsertIntoActiveApplication(formatted);

                BeepPlayer.Play(BeepType.Success);
                TxtRawTranscript.IsReadOnly = false;
                _suppressAutoActions = false;

                ShowStatus("Speech to Text", successMessage, InfoBarSeverity.Success);
                HotkeyManager.SendHotkeyAfterDelay(_settings.SpeechToTextSettings?.SendHotkeyAfterTranscriptionOperation, Constants.SendHotkeyDelay);
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

	private void ConfigureButtonHotkey(Button button, TextBlock? hotkeyTextBlock, string? hotkey, string baseTooltip)
	{
		string tooltip = ComposeTooltip(baseTooltip, hotkey);
		ToolTipService.SetToolTip(button, tooltip);
		AutomationProperties.SetHelpText(button, tooltip);
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
			_statusDismissTimer.Stop();
			_statusDismissTimer.Start();
		}

		if (DispatcherQueue.HasThreadAccess)
			Update();
		else
			DispatcherQueue.TryEnqueue(Update);
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
		await ShowDialogAsync(dialog);
	}

    private async Task<ContentDialogResult> ShowDialogAsync(ContentDialog dialog)
    {
        if (_isDialogOpen)
            return ContentDialogResult.None;

        _isDialogOpen = true;
        try
        {
            return await dialog.ShowAsync();
        }
        catch (Exception ex)
        {
            // Fallback safety if something else goes wrong with the dialog
            ShowStatus("Dialog Error", $"Failed to show dialog: {ex.Message}", InfoBarSeverity.Error);
            return ContentDialogResult.None;
        }
        finally
        {
            _isDialogOpen = false;
        }
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
		}
		else
		{
			_microphoneVisualization?.StopCapture();
		}
	}

	private void MicWaveArea_PointerReleased(object sender, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs e)
	{
		_microphoneVisualization?.Toggle();
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

	private void CmbLlmModel_SelectionChanged(object sender, SelectionChangedEventArgs e)
	{
		if (_settings.LlmSettings != null && CmbLlmModel.SelectedItem is string selectedModel)
		{
			_settings.LlmSettings.SelectedLlmModel = selectedModel;
			_settingsManager.SaveSettingsToFile(_settings);
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

	private void InsertIntoActiveApplication(string text)
	{
		if (string.IsNullOrWhiteSpace(text))
			return;

		var windowHandle = WindowNative.GetWindowHandle(this);
		if (windowHandle != IntPtr.Zero)
		{
			var foregroundWindow = GetForegroundWindow();
			if (foregroundWindow == windowHandle)
				return;
		}

		switch (_insertOption)
		{
			case DictationInsertOption.SendKeys:
				BeepPlayer.Play(BeepType.Start);
				HotkeyManager.SendText(text);
				break;
			case DictationInsertOption.Paste:
				_clipboard.SetText(text);
				HotkeyManager.SendHotkey("^v");
				break;
		}
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

	private async void SettingsMenuItem_Click(object sender, RoutedEventArgs e)
	{
		if (Content is not FrameworkElement rootElement)
		{
			return;
		}

		var settingsDialog = new SettingsDialog(_settings)
		{
			XamlRoot = rootElement.XamlRoot,
			RequestedTheme = rootElement.ActualTheme
		};

		if (await ShowDialogAsync(settingsDialog) == ContentDialogResult.Primary)
		{
			_settingsManager.SaveSettingsToFile(_settings);
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

    private void BtnDeletePrompt_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is LlmSettings.LlmPrompt prompt)
            _promptLibrary?.DeletePrompt(prompt);
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
		_shutdownCts.Dispose();
		_statusDismissTimer?.Stop();
	}
}
