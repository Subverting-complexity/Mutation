using CognitiveSupport;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Mutation.Ui.Core;
using Mutation.Ui.Services;
using Mutation.Ui.Views;
using NAudio.Wave;
using ScottPlot;
using ScottPlot.Plottables;
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

	private WaveInEvent? _waveformCapture;
	private DispatcherQueueTimer? _waveformTimer;
	// ScottPlot v5 renamed SignalPlot (v4) to Signal. Adjusting type accordingly.
	private Signal? _waveformSignal;
	private double[] _waveformBuffer = Array.Empty<double>();
	private double[] _waveformRenderBuffer = Array.Empty<double>();
	private int _waveformBufferIndex;
	private bool _waveformBufferFilled;
	private readonly object _waveformBufferLock = new();
	private double _waveformPeak;
	private double _waveformRms;
	private double _waveformPulse; // smoothed peak for visual pulse
	private bool VisualizationEnabled => _settings.AudioSettings?.EnableMicrophoneVisualization != false;

	// Suppress auto-format/clipboard/beep when we change text programmatically or during record/transcribe
	private bool _suppressAutoActions = false;

	private ISpeechToTextService? _activeSpeechService;
	private CancellationTokenSource _formatDebounceCts = new();
	private CancellationTokenSource _promptDebounceCts = new();
	private readonly CancellationTokenSource _shutdownCts = new();
	private DictationInsertOption _insertOption = DictationInsertOption.Paste;
	private readonly DispatcherTimer _statusDismissTimer;
	private bool _isDialogOpen;
	private bool _hotkeyRouterInitialized;

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

	private const int WaveformSampleRate = 16_000;
	private const int WaveformWindowMilliseconds = 40;
	private const int WaveformWindowSampleCount = WaveformSampleRate * WaveformWindowMilliseconds / 1_000;
	private const double WaveformFrameIntervalMilliseconds = 1_000.0 / 30.0;
	private const int WaveformBufferMilliseconds = 15;

	private const string DoNotInsertExplanation = "Keep the transcript inside Mutation without sending it anywhere.";
	private const string SendKeysExplanation = "Types the transcript into the active app as if you entered it yourself.";
	private const string PasteExplanation = "Copies the transcript and pastes it into the active application.";
	private const double ApproximateLineHeightMultiplier = 1.35;
	private const double MinimumLineHeightInDips = 1.0;

	[DllImport("user32.dll")]
	private static extern IntPtr GetForegroundWindow();

	public ObservableCollection<HotkeyRouterEntry> HotkeyRouterEntries { get; } = new();
	private readonly List<(string From, string To)> _hotkeyRouterPersistedSnapshot = new();

	internal MainWindow(
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
        InitializeMicrophoneVisualization();

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
		StartMicrophoneVisualizationCapture();

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

            // Populate Prompt Grid
            if (_settings.LlmSettings != null)
            {
                LstPrompts.ItemsSource = _settings.LlmSettings.Prompts;
            }

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
		InitializeHotkeyRouter();

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
		RefreshHotkeyRouterRegistrations();

        if (_settings.LlmSettings?.Prompts != null)
        {
           _hotkeyManager.RegisterPromptHotkeys(_settings.LlmSettings.Prompts, ExecutePrompt);
        }
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

	private void InitializeHotkeyRouter()
	{
		_hotkeyRouterInitialized = false;

		_settings.HotKeyRouterSettings ??= new HotKeyRouterSettings();

		foreach (var entry in HotkeyRouterEntries)
			DetachHotkeyRouterEntry(entry);
		HotkeyRouterEntries.Clear();
		foreach (var map in _settings.HotKeyRouterSettings.Mappings)
		{
			var entry = new HotkeyRouterEntry(map);
			AttachHotkeyRouterEntry(entry);
			HotkeyRouterEntries.Add(entry);
		}

		var initialPairs = HotkeyRouterEntries
				  .Where(e => e.IsValid && e.NormalizedFromHotkey is not null && e.NormalizedToHotkey is not null)
				  .Select(e => (From: e.NormalizedFromHotkey!, To: e.NormalizedToHotkey!))
				  .ToList();
		UpdateHotkeyRouterSnapshot(initialPairs);

		RecalculateHotkeyRouterDuplicates();
		// Defer registration & persistence until HotkeyManager is attached to avoid
		// any chance of wiping persisted mappings during initial construction.
		_hotkeyRouterInitialized = true;
	}

	private void InitializeMicrophoneVisualization()
	{
		if (MicWaveformPlot is null)
			return;

		_waveformBuffer = new double[WaveformWindowSampleCount];
		_waveformRenderBuffer = new double[WaveformWindowSampleCount];
		_waveformBufferIndex = 0;
		_waveformBufferFilled = false;

		var plot = MicWaveformPlot.Plot;
		plot.Clear();
		_waveformSignal = plot.Add.Signal(_waveformRenderBuffer);
		plot.Axes.SetLimitsX(0, Math.Max(1, WaveformWindowSampleCount - 1));
		plot.Axes.SetLimitsY(-1, 1);
		plot.HideGrid();
		MicWaveformPlot.Refresh();
		if (_settings.AudioSettings != null)
		{
			if (!VisualizationEnabled)
			{
				MicWaveformPlot.Visibility = Visibility.Collapsed;
				if (MicWaveformOffLabel != null) MicWaveformOffLabel.Visibility = Visibility.Visible;
			}
		}

		_waveformTimer = DispatcherQueue.CreateTimer();
		_waveformTimer.Interval = TimeSpan.FromMilliseconds(WaveformFrameIntervalMilliseconds);
		_waveformTimer.Tick += WaveformTimer_Tick;
		_waveformTimer.Start();
	}

	private void WaveformTimer_Tick(DispatcherQueueTimer sender, object args)
	{
		if (!VisualizationEnabled)
		{
			if (MicWaveformPlot.Visibility != Visibility.Collapsed)
				MicWaveformPlot.Visibility = Visibility.Collapsed;
			if (MicWaveformOffLabel != null)
				MicWaveformOffLabel.Visibility = Visibility.Visible;
			if (RmsLevelBar != null)
				RmsLevelBar.Height = 0;
			return;
		}

		if (MicWaveformPlot.Visibility != Visibility.Visible)
			MicWaveformPlot.Visibility = Visibility.Visible;
		if (MicWaveformOffLabel != null && MicWaveformOffLabel.Visibility == Visibility.Visible)
			MicWaveformOffLabel.Visibility = Visibility.Collapsed;

		int validSamples = PopulateWaveformRenderBuffer();

                double peak = 0;
                double sumSquares = 0;
                if (validSamples > 0)
                {
                        int samplesToProcess = Math.Min(validSamples, _waveformRenderBuffer.Length);
                        int startIndex = _waveformRenderBuffer.Length - samplesToProcess;
                        for (int i = startIndex; i < _waveformRenderBuffer.Length; i++)
                        {
                                double value = _waveformRenderBuffer[i];
                                double abs = Math.Abs(value);
                                if (abs > peak)
                                        peak = abs;
                                sumSquares += value * value;
                        }
                        _waveformRms = Math.Sqrt(sumSquares / Math.Max(1, samplesToProcess));
                }
		else
		{
			_waveformRms = 0;
		}

		_waveformPeak = peak;

		if (_waveformSignal != null)
			MicWaveformPlot.Refresh();

		UpdateMicLevelMeter(peak, _waveformRms);

		_waveformPulse = Math.Max(_waveformPulse * 0.85, Math.Min(1.0, peak));
		if (MicPulseOverlay != null)
			MicPulseOverlay.Opacity = _waveformPulse * 0.35;
	}

	private int PopulateWaveformRenderBuffer()
	{
		if (_waveformRenderBuffer.Length == 0 || _waveformBuffer.Length == 0)
		{
			return 0;
		}

		lock (_waveformBufferLock)
		{
			if (!_waveformBufferFilled && _waveformBufferIndex == 0)
			{
				Array.Clear(_waveformRenderBuffer, 0, _waveformRenderBuffer.Length);
				return 0;
			}

                        if (_waveformBufferFilled)
                        {
                                int bufferLen = _waveformRenderBuffer.Length;
                                int index = _waveformBufferIndex;
                                if (index > bufferLen)
                                        index = bufferLen;
                                int tailLength = bufferLen - index;
                                if (tailLength > 0)
                                        Array.Copy(_waveformBuffer, index, _waveformRenderBuffer, 0, tailLength);
                                if (index > 0)
                                        Array.Copy(_waveformBuffer, 0, _waveformRenderBuffer, tailLength, index);
                                return bufferLen;
                        }

			int validCount = _waveformBufferIndex;
			int leadingZeros = _waveformRenderBuffer.Length - validCount;
			if (leadingZeros > 0)
				Array.Clear(_waveformRenderBuffer, 0, leadingZeros);
			Array.Copy(_waveformBuffer, 0, _waveformRenderBuffer, Math.Max(0, leadingZeros), validCount);
			return validCount;
		}
	}

	private void UpdateMicLevelMeter(double peak, double rms)
	{
		if (RmsLevelBar is null || MicWaveformPlot is null)
			return;

		double waveformHeight = MicWaveformPlot.ActualHeight;
		if (double.IsNaN(waveformHeight) || waveformHeight <= 0)
			waveformHeight = MicWaveformPlot.Height;

		if (MicLevelMeter is not null && waveformHeight > 0)
			MicLevelMeter.Height = waveformHeight;

                double levelValue = rms;
		levelValue = Math.Min(1.0, Math.Max(0, levelValue));

		RmsLevelBar.Height = waveformHeight * levelValue;
	}

	private void StartMicrophoneVisualizationCapture()
	{
		if (_waveformRenderBuffer.Length == 0)
			return;

		StopMicrophoneVisualizationCapture();

		lock (_waveformBufferLock)
		{
			if (_waveformBuffer.Length > 0)
				Array.Clear(_waveformBuffer, 0, _waveformBuffer.Length);
			if (_waveformRenderBuffer.Length > 0)
				Array.Clear(_waveformRenderBuffer, 0, _waveformRenderBuffer.Length);
			_waveformBufferIndex = 0;
			_waveformBufferFilled = false;
		}

		int deviceIndex = _audioDeviceManager.MicrophoneDeviceIndex;
		if (deviceIndex < 0)
		{
			// Provide a visible hint if selection failed to map to an NAudio device index.
			DispatcherQueue.TryEnqueue(() =>
					  ShowStatus("Microphone", "Unable to start waveform monitor (device not resolved)", InfoBarSeverity.Warning));
			return;
		}

		try
		{
			_waveformCapture = new WaveInEvent
			{
				DeviceNumber = deviceIndex,
				WaveFormat = new WaveFormat(WaveformSampleRate, 16, 1),
				BufferMilliseconds = WaveformBufferMilliseconds
			};
			_waveformCapture.DataAvailable += OnWaveformDataAvailable;
			_waveformCapture.StartRecording();
		}
		catch (Exception ex)
		{
			_waveformCapture?.Dispose();
			_waveformCapture = null;
			DispatcherQueue.TryEnqueue(() =>
					  ShowStatus("Microphone", $"Unable to monitor audio: {ex.Message}", InfoBarSeverity.Error));
		}
	}

	private void RestartMicrophoneVisualizationCapture()
	{
		StopMicrophoneVisualizationCapture();
		StartMicrophoneVisualizationCapture();
	}

	private void StopMicrophoneVisualizationCapture()
	{
		if (_waveformCapture is null)
			return;

		try
		{
			_waveformCapture.DataAvailable -= OnWaveformDataAvailable;
			_waveformCapture.StopRecording();
		}
		catch
		{
			// Ignore failures that occur while shutting down capture.
		}

		_waveformCapture.Dispose();
		_waveformCapture = null;
	}

	private void DisposeMicrophoneVisualization()
	{
		StopMicrophoneVisualizationCapture();

		if (_waveformTimer is not null)
		{
			_waveformTimer.Tick -= WaveformTimer_Tick;
			_waveformTimer.Stop();
			_waveformTimer = null;
		}

		_waveformSignal = null;
		_waveformBuffer = Array.Empty<double>();
		_waveformRenderBuffer = Array.Empty<double>();
		_waveformBufferIndex = 0;
		_waveformBufferFilled = false;
	}

	private void OnWaveformDataAvailable(object? sender, WaveInEventArgs e)
	{
		if (_waveformBuffer.Length == 0 || e.BytesRecorded <= 0)
			return;

		int sampleCount = e.BytesRecorded / 2;
		if (sampleCount <= 0)
			return;

		lock (_waveformBufferLock)
		{
			for (int i = 0; i < sampleCount; i++)
			{
				short sample = BitConverter.ToInt16(e.Buffer, i * 2);
				double value = sample / 32768d;
				_waveformBuffer[_waveformBufferIndex++] = value;
				if (_waveformBufferIndex >= _waveformBuffer.Length)
				{
					_waveformBufferIndex = 0;
					_waveformBufferFilled = true;
				}
			}
		}
	}

	private void RefreshHotkeyRouterRegistrations()
	{
		_settings.HotKeyRouterSettings ??= new HotKeyRouterSettings();

		RecalculateHotkeyRouterDuplicates();
		var normalizedPairs = SyncHotkeyRouterSettings();

		if (_hotkeyManager is null)
		{
			foreach (var entry in HotkeyRouterEntries)
				entry.SetBindingResult(HotkeyBindingState.Inactive, null);

			if (ShouldPersistHotkeyRouterMappings(normalizedPairs))
			{
				_settingsManager.SaveSettingsToFile(_settings);
				UpdateHotkeyRouterSnapshot(normalizedPairs);
			}
			return;
		}

		var mappings = _settings.HotKeyRouterSettings.Mappings;
		var results = _hotkeyManager.RefreshRouterHotkeys(mappings);
		var resultLookup = results.ToDictionary(r => r.Map);

		foreach (var entry in HotkeyRouterEntries)
		{
			if (resultLookup.TryGetValue(entry.Map, out var result))
			{
				entry.SetBindingResult(result.Success ? HotkeyBindingState.Bound : HotkeyBindingState.Failed, result.ErrorMessage);
			}
			else
			{
				entry.SetBindingResult(HotkeyBindingState.Inactive, null);
			}
		}

		if (ShouldPersistHotkeyRouterMappings(normalizedPairs))
		{
			_settingsManager.SaveSettingsToFile(_settings);
			UpdateHotkeyRouterSnapshot(normalizedPairs);
		}
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

		var normalizedPairs = SyncHotkeyRouterSettings();
		_settingsManager.SaveSettingsToFile(_settings);
		UpdateHotkeyRouterSnapshot(normalizedPairs);
        
        _audioSessionManager.Dispose();
        
		BeepPlayer.DisposePlayers();
		DisposeMicrophoneVisualization();
		Dispose();
	}

	private void CopyText_Click(object sender, RoutedEventArgs e)
	{
		_clipboard.SetText(TxtClipboard.Text);
		ShowStatus("Clipboard", "Text copied to the clipboard.", InfoBarSeverity.Success);
	}

	private void BtnAddHotkeyRoute_Click(object sender, RoutedEventArgs e)
	{
		_settings.HotKeyRouterSettings ??= new HotKeyRouterSettings();

		var map = new HotKeyRouterSettings.HotKeyRouterMap(string.Empty, string.Empty);

		var entry = new HotkeyRouterEntry(map);
		AttachHotkeyRouterEntry(entry);
		HotkeyRouterEntries.Add(entry);

		RefreshHotkeyRouterRegistrations();

		// Defer focusing until the ListView generates the container
		TryFocusHotkeyRouterFromTextBox(entry);
	}

	private void HotkeyRouterDelete_Click(object sender, RoutedEventArgs e)
	{
		if (((FrameworkElement)sender).Tag is not HotkeyRouterEntry entry)
			return;

		if (_settings.HotKeyRouterSettings is not null)
			_settings.HotKeyRouterSettings.Mappings.Remove(entry.Map);

		DetachHotkeyRouterEntry(entry);
		HotkeyRouterEntries.Remove(entry);
		RefreshHotkeyRouterRegistrations();
	}

	private void HotkeyRouterFrom_LostFocus(object sender, RoutedEventArgs e)
	{
		if (sender is FrameworkElement { DataContext: HotkeyRouterEntry entry })
		{
			entry.CommitFromHotkey();
			RefreshHotkeyRouterRegistrations();
		}
	}

	private void HotkeyRouterTo_LostFocus(object sender, RoutedEventArgs e)
	{
		if (sender is FrameworkElement { DataContext: HotkeyRouterEntry entry })
		{
			entry.CommitToHotkey();
			RefreshHotkeyRouterRegistrations();
		}
	}

	private void HotkeyRouterEntry_PropertyChanged(object? sender, PropertyChangedEventArgs e)
	{
		if (sender is not HotkeyRouterEntry)
			return;

		if (e.PropertyName == nameof(HotkeyRouterEntry.FromHotkey) || e.PropertyName == nameof(HotkeyRouterEntry.IsFromValid))
			RecalculateHotkeyRouterDuplicates();
	}

	private void AttachHotkeyRouterEntry(HotkeyRouterEntry entry)
	{
		entry.PropertyChanged += HotkeyRouterEntry_PropertyChanged;
	}

	private void TryFocusHotkeyRouterFromTextBox(HotkeyRouterEntry entry)
	{
		// Run async attempts on dispatcher without blocking UI thread
		DispatcherQueue.TryEnqueue(async () =>
		{
			for (int i = 0; i < 8; i++)
			{
				var container = HotkeyRouterList.ContainerFromItem(entry) as ListViewItem;
				if (container?.ContentTemplateRoot is FrameworkElement root)
				{
					// First TextBox inside the template corresponds to the 'From' hotkey
					var fromTextBox = FindDescendant<TextBox>(root);
					if (fromTextBox != null)
					{
						fromTextBox.Focus(FocusState.Programmatic);
						// Select existing text (if any) to allow immediate typing
						fromTextBox.SelectAll();
						return;
					}
				}
				await Task.Delay(40);
			}
		});
	}

	private static T? FindDescendant<T>(DependencyObject root) where T : class
	{
		int count = VisualTreeHelper.GetChildrenCount(root);
		for (int i = 0; i < count; i++)
		{
			var child = VisualTreeHelper.GetChild(root, i);
			if (child is T typed)
				return typed;
			var result = FindDescendant<T>(child);
			if (result != null)
				return result;
		}
		return null;
	}

	private void DetachHotkeyRouterEntry(HotkeyRouterEntry entry)
	{
		entry.PropertyChanged -= HotkeyRouterEntry_PropertyChanged;
	}

	private void RecalculateHotkeyRouterDuplicates()
	{
		var duplicates = HotkeyRouterEntries
				  .Where(e => e.IsFromValid && e.NormalizedFromHotkey is not null)
				  .GroupBy(e => e.NormalizedFromHotkey!, StringComparer.OrdinalIgnoreCase)
				  .Where(g => g.Count() > 1)
				  .SelectMany(g => g);

		var duplicateSet = new HashSet<HotkeyRouterEntry>(duplicates);

		foreach (var entry in HotkeyRouterEntries)
			entry.SetDuplicate(duplicateSet.Contains(entry));
	}

	private List<(string From, string To)> SyncHotkeyRouterSettings()
	{
		_settings.HotKeyRouterSettings ??= new HotKeyRouterSettings();

		foreach (var entry in HotkeyRouterEntries)
		{
			entry.CommitFromHotkey();
			entry.CommitToHotkey();
		}

		var validEntries = HotkeyRouterEntries
				  .Where(e => e.IsValid && e.NormalizedFromHotkey is not null && e.NormalizedToHotkey is not null)
				  .ToList();

		// If no entries are currently valid but existing settings contain mappings, preserve them.
		// This avoids wiping user settings due to a transient validation state during startup.
		if (validEntries.Count == 0 && _settings.HotKeyRouterSettings.Mappings.Count > 0)
		{
			return _settings.HotKeyRouterSettings.Mappings
					  .Where(m => !string.IsNullOrWhiteSpace(m.FromHotKey) && !string.IsNullOrWhiteSpace(m.ToHotKey))
					  .Select(m => (From: m.FromHotKey!, To: m.ToHotKey!))
					  .ToList();
		}

		var normalizedPairs = validEntries
				  .Select(e => (From: e.NormalizedFromHotkey!, To: e.NormalizedToHotkey!))
				  .ToList();

		var existing = _settings.HotKeyRouterSettings.Mappings;

		bool changed = existing.Count != normalizedPairs.Count;
		if (!changed)
		{
			for (int i = 0; i < existing.Count; i++)
			{
				var existingFrom = existing[i].FromHotKey ?? string.Empty;
				var existingTo = existing[i].ToHotKey ?? string.Empty;

				if (!string.Equals(existingFrom, normalizedPairs[i].From, StringComparison.Ordinal) ||
					 !string.Equals(existingTo, normalizedPairs[i].To, StringComparison.Ordinal))
				{
					changed = true;
					break;
				}
			}
		}

		if (changed)
		{
			var updatedMaps = normalizedPairs
					  .Select(pair => new HotKeyRouterSettings.HotKeyRouterMap(pair.From, pair.To))
					  .ToList();

			_settings.HotKeyRouterSettings.Mappings = updatedMaps;

			for (int i = 0; i < validEntries.Count; i++)
				validEntries[i].ReplaceBackingMap(updatedMaps[i]);
		}

		return normalizedPairs;
	}

	private bool ShouldPersistHotkeyRouterMappings(List<(string From, string To)> normalizedPairs)
	{
		if (!_hotkeyRouterInitialized)
			return false;

		if (_hotkeyRouterPersistedSnapshot.Count != normalizedPairs.Count)
			return true;

		for (int i = 0; i < normalizedPairs.Count; i++)
		{
			var previous = _hotkeyRouterPersistedSnapshot[i];
			var current = normalizedPairs[i];

			if (!string.Equals(previous.From, current.From, StringComparison.Ordinal) ||
				 !string.Equals(previous.To, current.To, StringComparison.Ordinal))
			{
				return true;
			}
		}

		return false;
	}

	private void UpdateHotkeyRouterSnapshot(IEnumerable<(string From, string To)> normalizedPairs)
	{
		_hotkeyRouterPersistedSnapshot.Clear();
		_hotkeyRouterPersistedSnapshot.AddRange(normalizedPairs);
	}

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
            
            string llmPromptContent = GetAutoRunPromptContent();
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
			RestartMicrophoneVisualizationCapture();
		}
		else
		{
			StopMicrophoneVisualizationCapture();
		}
	}

	private void MicWaveArea_PointerReleased(object sender, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs e)
	{
		if (_settings.AudioSettings == null)
			return;
		bool newState = !_settings.AudioSettings.EnableMicrophoneVisualization;
		_settings.AudioSettings.EnableMicrophoneVisualization = newState;
		_settingsManager.SaveSettingsToFile(_settings);
		if (newState)
		{
			InitializeMicrophoneVisualization();
			StartMicrophoneVisualizationCapture();
		}
		else
		{
			DisposeMicrophoneVisualization();
			if (MicWaveformOffLabel != null)
				MicWaveformOffLabel.Visibility = Visibility.Visible;
		}
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

    private string GetAutoRunPromptContent()
    {
        // Return content of the prompt marked as AutoRun, or empty if none (or fallback to first?)
        // Spec says: "Only 1 prompt... can have Auto-Run". 
        var prompt = _settings.LlmSettings?.Prompts.FirstOrDefault(p => p.AutoRun);
        return prompt?.Content ?? string.Empty;
    }

    private void BtnAddPrompt_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new PromptEditorWindow(null, _transcriptFormatter);
        // dialog.XamlRoot = this.Content.XamlRoot; 
        // Window doesn't have XamlRoot property itself like ContentDialog?
        // Wait, PromptEditorWindow is a Window. It doesn't need XamlRoot. it opens as a separate window.
        // But for modal behavior over generic window? WinUI 3 Window is weird about modality.
        // Let's assume non-modal or use .Activate().
        // To make it modal to *this* window requires P/Invoke SetParent usually.
        // For simplicity, I'll just show it. Ideally use ContentDialog for modal within app window, but I created a Window.
        // Re-reading implementation plan: "PromptEditorWindow.xaml - A new Window/Dialog".
        // If I use Window:
        dialog.Activate();
        
        // Wait for it to close? We can't await a Window easily without events.
        dialog.Closed += (s, args) => 
        {
             if (dialog.IsSaved && dialog.Prompt != null && !string.IsNullOrWhiteSpace(dialog.Prompt.Name))
             {
                 // Add new prompt
                 dialog.Prompt.Id = (_settings.LlmSettings.Prompts.Max(p => (int?)p.Id) ?? 0) + 1;
                 
                 // Handle AutoRun exclusivity
                 if (dialog.Prompt.AutoRun)
                     foreach(var p in _settings.LlmSettings.Prompts) p.AutoRun = false;
                     
                 _settings.LlmSettings.Prompts.Add(dialog.Prompt);
                 SaveAndRefreshPrompts();
             }
        };
    }

    private void BtnEditPrompt_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is LlmSettings.LlmPrompt prompt)
        {
             var dialog = new PromptEditorWindow(prompt, _transcriptFormatter);
             dialog.Activate();
             dialog.Closed += (s, args) =>
             {
                 if (dialog.IsSaved)
                 {
                     // Handle AutoRun exclusivity if this one is now AutoRun
                     if (prompt.AutoRun)
                     {
                         foreach(var p in _settings.LlmSettings.Prompts)
                         {
                             if (p != prompt) p.AutoRun = false;
                         }
                     }
                     
                     SaveAndRefreshPrompts();
                 }
                 // If not saved (Cancel/Close), do nothing. The object references might have been modified 
                 // if the dialog logic wasn't careful (it is careful to only modify on save now? 
                 // actually I modified PromptEditorWindow to modify property on Save, but wait...
                 // PromptEditorWindow takes a reference. If I type in the text box, does it update the reference?
                 // No, PromptEditorWindow updates the object in BtnSave_Click. 
                 // So we are safe.
             };
        }
    }

    private void BtnDeletePrompt_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is LlmSettings.LlmPrompt prompt)
        {
             _settings.LlmSettings.Prompts.Remove(prompt);
             SaveAndRefreshPrompts();
        }
    }

    private void BtnRunPrompt_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is LlmSettings.LlmPrompt prompt)
        {
             ExecutePrompt(prompt);
        }
    }

    private void SaveAndRefreshPrompts()
    {
        _settingsManager.SaveSettingsToFile(_settings);
        // Refresh List
        LstPrompts.ItemsSource = null;
        LstPrompts.ItemsSource = _settings.LlmSettings.Prompts;
        
        // Refresh Hotkeys
        _hotkeyManager?.RegisterPromptHotkeys(_settings.LlmSettings.Prompts, ExecutePrompt);
    }

	public void Dispose()
	{
		_audioSessionManager?.Dispose();
		_waveformCapture?.Dispose();
		_formatDebounceCts?.Dispose();
		_promptDebounceCts?.Dispose();
		_shutdownCts.Dispose();
		_statusDismissTimer?.Stop();
		_waveformTimer?.Stop();
	}
}
