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

	private readonly WaveformRenderGate _waveformRenderGate = new();
	private readonly SingleTimerSlot<DispatcherQueueTimer> _waveformTimer;

	// Guards the capture handle and the epoch that decides which start owns it.
	// Capture transitions now arrive from two directions — the UI thread (startup,
	// the visualization toggle, window close) and the background switch worker
	// (issue #267) — so the handle can no longer be a plain field. Held for field
	// access only, never across a device call: a background start blocked on a
	// wedged waveInOpen must not be able to stall a UI-thread caller that takes it.
	private readonly object _captureSync = new();
	private WaveInEvent? _waveformCapture;
	// Bumped by every start and stop. A start that finds its epoch superseded while
	// it was opening the device closes the handle it just opened rather than
	// publishing it — otherwise a switch that lands after a stop leaves a capture
	// running that nothing owns, and the microphone stays live with no way to
	// release it.
	private int _captureEpoch;
	// Set by Dispose, cleared by Initialize. Separate from the epoch because Dispose
	// also tears down _samples: a start that read _samples just before Dispose ran
	// would otherwise claim a *newer* epoch and publish a handle into a controller
	// that has already been shut down.
	private bool _captureDisposed;

	private Signal? _waveformSignal;

	// Null until Initialize() runs, and again after Dispose() — the capture callback and the
	// render tick both check it rather than inspecting array lengths.
	private WaveformSampleBuffer? _samples;
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

		// Initialize() runs again on every Settings-dialog save; the slot guarantees
		// the previous frame timer is stopped and unwired before the next is created,
		// so the saves cannot stack up 30 FPS timers on the UI thread (issue #231).
		_waveformTimer = new SingleTimerSlot<DispatcherQueueTimer>(
			create: () =>
			{
				var timer = _dispatcherQueue.CreateTimer();
				timer.Interval = TimeSpan.FromMilliseconds(WaveformFrameIntervalMilliseconds);
				timer.Tick += WaveformTimer_Tick;
				return timer;
			},
			start: timer => timer.Start(),
			stop: timer =>
			{
				timer.Tick -= WaveformTimer_Tick;
				timer.Stop();
			});
	}

	public void Initialize()
	{
		// Re-opens the controller after a Dispose that was really an "off" switch
		// (ApplyEnabledStateFromSettings), so capture can be published again.
		lock (_captureSync)
			_captureDisposed = false;

		_samples = new WaveformSampleBuffer(WaveformWindowSampleCount);

		var plot = _waveformPlot.Plot;
		plot.Clear();
		_waveformSignal = plot.Add.Signal(_samples.RenderBuffer);
		plot.Axes.SetLimitsX(0, Math.Max(1, WaveformWindowSampleCount - 1));
		plot.Axes.SetLimitsY(-1, 1);
		plot.HideGrid();
		_waveformPlot.Refresh();
		if (_settings.AudioSettings != null && !VisualizationEnabled)
		{
			_waveformPlot.Visibility = Visibility.Collapsed;
			if (_offLabel != null) _offLabel.Visibility = Visibility.Visible;
		}

		_waveformTimer.Restart();
	}

	/// <summary>
	/// Opens capture on the currently-selected device. Safe to call from the
	/// background switch worker as well as the UI thread: everything it touches is
	/// either thread-safe on its own (the sample ring, the render gate) or published
	/// under <c>_captureSync</c>, and the status messages it raises are marshalled
	/// back to the UI thread.
	/// </summary>
	public void StartCapture()
	{
		// Taken once into a local rather than re-read further down: this method blocks
		// on the device in between, and a Dispose on the UI thread nulls the field
		// while it does. Re-reading it would throw, and the switch coordinator would
		// dress that up as "could not switch to this microphone" — a failure message
		// about a device that was fine, for a visualization the user had just switched
		// off.
		var samples = _samples;
		if (samples is null)
			return;

		int epoch;
		WaveInEvent? superseded;
		lock (_captureSync)
		{
			if (_captureDisposed)
				return;

			epoch = ++_captureEpoch;
			superseded = _waveformCapture;
			_waveformCapture = null;
		}

		// Outside the lock: closing a handle is a winmm call into the driver and is
		// exactly the kind of work that must never be held over a lock the UI thread
		// can take.
		CloseCapture(superseded);

		samples.Reset();

		// Render the reset-to-flat state once even before the first samples arrive.
		_waveformRenderGate.MarkDataArrived();

		int deviceIndex = _audioDeviceManager.MicrophoneDeviceIndex;
		if (deviceIndex < 0)
		{
			_dispatcherQueue.TryEnqueue(() =>
				_showStatus("Microphone", "Unable to start waveform monitor (device not resolved)", InfoBarSeverity.Warning));
			return;
		}

		WaveInEvent? capture = null;
		try
		{
			capture = new WaveInEvent
			{
				DeviceNumber = deviceIndex,
				WaveFormat = new WaveFormat(WaveformSampleRate, 16, 1),
				BufferMilliseconds = WaveformBufferMilliseconds
			};
			capture.DataAvailable += OnWaveformDataAvailable;
			capture.StartRecording();
		}
		catch (Exception ex)
		{
			CloseCapture(capture);
			_dispatcherQueue.TryEnqueue(() =>
				_showStatus("Microphone", $"Unable to monitor audio: {ex.Message}", InfoBarSeverity.Error));
			return;
		}

		bool stillCurrent;
		lock (_captureSync)
		{
			stillCurrent = !_captureDisposed && _captureEpoch == epoch;
			if (stillCurrent)
				_waveformCapture = capture;
		}

		// Someone stopped, disposed, or started a newer capture while this one was
		// opening. Release the handle here rather than leave the microphone live with
		// nothing holding a reference to close it.
		if (!stillCurrent)
			CloseCapture(capture);
	}

	public void RestartCapture()
	{
		StopCapture();
		StartCapture();
	}

	/// <summary>
	/// Releases capture. Callable from either thread; see <see cref="StartCapture"/>.
	/// </summary>
	public void StopCapture()
	{
		WaveInEvent? capture;
		lock (_captureSync)
		{
			// Bumped even when there is no handle to close: a start that is mid-
			// waveInOpen right now has to learn that its result is no longer wanted.
			_captureEpoch++;
			capture = _waveformCapture;
			_waveformCapture = null;
		}

		CloseCapture(capture);
	}

	// Shuts a capture handle down and releases it, treating any failure as nothing to
	// act on — the handle is being abandoned either way, and a throw from the teardown
	// of the device being replaced must not be reported as a failure to switch to the
	// new one.
	private void CloseCapture(WaveInEvent? capture)
	{
		if (capture is null)
			return;

		try
		{
			capture.DataAvailable -= OnWaveformDataAvailable;
			capture.StopRecording();
		}
		catch
		{
			// Ignore failures that occur while shutting down capture.
		}

		// Disposed even when stopping threw, so a device that faults on the way down
		// does not leak its handle.
		try
		{
			capture.Dispose();
		}
		catch
		{
			// Ignore failures that occur while shutting down capture.
		}
	}

	// Sets the visualization to a specific on/off state, persists it, and applies it.
	// Driven by the in-place ToggleButton (IsChecked), whose two-state value is the
	// authority — unlike a stateless flip, this keeps the setting in lockstep with
	// the control even if the two ever diverged. No-ops when already in that state.
	public void SetEnabled(bool enabled)
	{
		if (_settings.AudioSettings == null)
			return;

		if (_settings.AudioSettings.EnableMicrophoneVisualization == enabled)
			return;

		_settings.AudioSettings.EnableMicrophoneVisualization = enabled;
		_settingsManager.SaveSettingsToFile(_settings);
		ApplyEnabledStateFromSettings();
	}

	// Applies the EnableMicrophoneVisualization flag from _settings without writing it back.
	// Used by the Settings dialog post-save pipeline so a toggle change in the dialog takes
	// effect immediately without re-toggling.
	public void ApplyEnabledStateFromSettings()
	{
		bool enabled = _settings.AudioSettings?.EnableMicrophoneVisualization ?? true;
		if (enabled)
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
		// Set before the stop, so a start already running on the switch worker cannot
		// slip a freshly-opened handle past the epoch check and leave the microphone
		// live after the window has closed.
		lock (_captureSync)
			_captureDisposed = true;

		StopCapture();

		_waveformTimer.Stop();

		_waveformSignal = null;
		_samples = null;
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

		// Nothing new has been captured since the last frame — the plot, meter, and
		// pulse overlay are all still showing the last rendered state, so there is
		// nothing to redraw. Skip the buffer copy, RMS scan, ScottPlot refresh, and
		// meter writes. This is the idle path whenever no capture is running (device
		// not resolved, capture failed, or capture stopped) and it keeps that
		// per-frame work — and the DispatcherQueue it shares with hotkey dispatch —
		// free instead of spinning at ~30 FPS for the app's lifetime.
		if (!_waveformRenderGate.ConsumeShouldRender())
			return;

		if (_samples is null)
			return;

		int validSamples = _samples.Snapshot();
		var levels = WaveformSampleBuffer.MeasureLevels(_samples.RenderBuffer, validSamples);

		_waveformRms = levels.Rms;
		_waveformPeak = levels.Peak;

		if (_waveformSignal != null)
			_waveformPlot.Refresh();

		UpdateMicLevelMeter(levels.Peak, levels.Rms);

		_waveformPulse = Math.Max(_waveformPulse * 0.85, Math.Min(1.0, levels.Peak));
		if (_pulseOverlay != null)
			_pulseOverlay.Opacity = _waveformPulse * 0.35;
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
		// One read into a local, for the same reason as StartCapture: this runs on the
		// capture device's own thread, and Dispose can null the field between a
		// null-check and a use of it.
		var samples = _samples;
		if (samples is null || samples.Write(e.Buffer, e.BytesRecorded) == 0)
			return;

		// New samples are in the ring buffer — let the next render tick draw them.
		_waveformRenderGate.MarkDataArrived();
	}
}
