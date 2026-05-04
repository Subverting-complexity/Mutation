using CognitiveSupport;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Mutation.Ui.Core;
using NAudio.Wave;
using ScottPlot.Plottables;
using ScottPlot.WinUI;
using System;
using XamlRectangle = Microsoft.UI.Xaml.Shapes.Rectangle;

namespace Mutation.Ui.Services;

internal sealed class MicrophoneVisualizationController : IDisposable
{
	private const int WaveformSampleRate = 16_000;
	private const int WaveformWindowMilliseconds = 40;
	private const int WaveformWindowSampleCount = WaveformSampleRate * WaveformWindowMilliseconds / 1_000;
	private const double WaveformFrameIntervalMilliseconds = 1_000.0 / 30.0;
	private const int WaveformBufferMilliseconds = 15;

	private readonly DispatcherQueue _dispatcherQueue;
	private readonly AudioDeviceManager _audioDeviceManager;
	private readonly Settings _settings;
	private readonly ISettingsManager _settingsManager;
	private readonly WinUIPlot _waveformPlot;
	private readonly TextBlock? _offLabel;
	private readonly Grid? _levelMeter;
	private readonly XamlRectangle? _rmsLevelBar;
	private readonly XamlRectangle? _pulseOverlay;
	private readonly Action<string, string, InfoBarSeverity> _showStatus;

	private WaveInEvent? _waveformCapture;
	private DispatcherQueueTimer? _waveformTimer;
	private Signal? _waveformSignal;
	private double[] _waveformBuffer = Array.Empty<double>();
	private double[] _waveformRenderBuffer = Array.Empty<double>();
	private int _waveformBufferIndex;
	private bool _waveformBufferFilled;
	private readonly object _waveformBufferLock = new();
	private double _waveformPeak;
	private double _waveformRms;
	private double _waveformPulse;

	private bool VisualizationEnabled => _settings.AudioSettings?.EnableMicrophoneVisualization != false;

	public MicrophoneVisualizationController(
		DispatcherQueue dispatcherQueue,
		AudioDeviceManager audioDeviceManager,
		Settings settings,
		ISettingsManager settingsManager,
		WinUIPlot waveformPlot,
		TextBlock? offLabel,
		Grid? levelMeter,
		XamlRectangle? rmsLevelBar,
		XamlRectangle? pulseOverlay,
		Action<string, string, InfoBarSeverity> showStatus)
	{
		_dispatcherQueue = dispatcherQueue ?? throw new ArgumentNullException(nameof(dispatcherQueue));
		_audioDeviceManager = audioDeviceManager ?? throw new ArgumentNullException(nameof(audioDeviceManager));
		_settings = settings ?? throw new ArgumentNullException(nameof(settings));
		_settingsManager = settingsManager ?? throw new ArgumentNullException(nameof(settingsManager));
		_waveformPlot = waveformPlot ?? throw new ArgumentNullException(nameof(waveformPlot));
		_offLabel = offLabel;
		_levelMeter = levelMeter;
		_rmsLevelBar = rmsLevelBar;
		_pulseOverlay = pulseOverlay;
		_showStatus = showStatus ?? throw new ArgumentNullException(nameof(showStatus));
	}

	public void Initialize()
	{
		_waveformBuffer = new double[WaveformWindowSampleCount];
		_waveformRenderBuffer = new double[WaveformWindowSampleCount];
		_waveformBufferIndex = 0;
		_waveformBufferFilled = false;

		var plot = _waveformPlot.Plot;
		plot.Clear();
		_waveformSignal = plot.Add.Signal(_waveformRenderBuffer);
		plot.Axes.SetLimitsX(0, Math.Max(1, WaveformWindowSampleCount - 1));
		plot.Axes.SetLimitsY(-1, 1);
		plot.HideGrid();
		_waveformPlot.Refresh();
		if (_settings.AudioSettings != null && !VisualizationEnabled)
		{
			_waveformPlot.Visibility = Visibility.Collapsed;
			if (_offLabel != null) _offLabel.Visibility = Visibility.Visible;
		}

		_waveformTimer = _dispatcherQueue.CreateTimer();
		_waveformTimer.Interval = TimeSpan.FromMilliseconds(WaveformFrameIntervalMilliseconds);
		_waveformTimer.Tick += WaveformTimer_Tick;
		_waveformTimer.Start();
	}

	public void StartCapture()
	{
		if (_waveformRenderBuffer.Length == 0)
			return;

		StopCapture();

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
			_dispatcherQueue.TryEnqueue(() =>
				_showStatus("Microphone", "Unable to start waveform monitor (device not resolved)", InfoBarSeverity.Warning));
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
			_dispatcherQueue.TryEnqueue(() =>
				_showStatus("Microphone", $"Unable to monitor audio: {ex.Message}", InfoBarSeverity.Error));
		}
	}

	public void RestartCapture()
	{
		StopCapture();
		StartCapture();
	}

	public void StopCapture()
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

	public void Toggle()
	{
		if (_settings.AudioSettings == null)
			return;

		bool newState = !_settings.AudioSettings.EnableMicrophoneVisualization;
		_settings.AudioSettings.EnableMicrophoneVisualization = newState;
		_settingsManager.SaveSettingsToFile(_settings);
		if (newState)
		{
			Initialize();
			StartCapture();
		}
		else
		{
			Dispose();
			if (_offLabel != null)
				_offLabel.Visibility = Visibility.Visible;
		}
	}

	public void Dispose()
	{
		StopCapture();

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

	private void WaveformTimer_Tick(DispatcherQueueTimer sender, object args)
	{
		if (!VisualizationEnabled)
		{
			if (_waveformPlot.Visibility != Visibility.Collapsed)
				_waveformPlot.Visibility = Visibility.Collapsed;
			if (_offLabel != null)
				_offLabel.Visibility = Visibility.Visible;
			if (_rmsLevelBar != null)
				_rmsLevelBar.Height = 0;
			return;
		}

		if (_waveformPlot.Visibility != Visibility.Visible)
			_waveformPlot.Visibility = Visibility.Visible;
		if (_offLabel != null && _offLabel.Visibility == Visibility.Visible)
			_offLabel.Visibility = Visibility.Collapsed;

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
			_waveformPlot.Refresh();

		UpdateMicLevelMeter(peak, _waveformRms);

		_waveformPulse = Math.Max(_waveformPulse * 0.85, Math.Min(1.0, peak));
		if (_pulseOverlay != null)
			_pulseOverlay.Opacity = _waveformPulse * 0.35;
	}

	private int PopulateWaveformRenderBuffer()
	{
		if (_waveformRenderBuffer.Length == 0 || _waveformBuffer.Length == 0)
			return 0;

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
		if (_rmsLevelBar is null)
			return;

		double waveformHeight = _waveformPlot.ActualHeight;
		if (double.IsNaN(waveformHeight) || waveformHeight <= 0)
			waveformHeight = _waveformPlot.Height;

		if (_levelMeter is not null && waveformHeight > 0)
			_levelMeter.Height = waveformHeight;

		double levelValue = rms;
		levelValue = Math.Min(1.0, Math.Max(0, levelValue));

		_rmsLevelBar.Height = waveformHeight * levelValue;
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
}
