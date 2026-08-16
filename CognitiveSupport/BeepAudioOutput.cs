using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Threading;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;

namespace CognitiveSupport;

/// <summary>
/// The speaker every beep goes out of: one audio device, opened once and held open, fed by a
/// mixer that beeps are dropped into.
///
/// <para>
/// This replaces <c>System.Media.SoundPlayer</c>, and it exists because of what that API does
/// between being asked for a sound and the sound being heard. It owns no device, so for every
/// beep Windows opens the default output, reads the file, plays, and closes again — on an
/// internal thread, with no bound on how long the open takes. A dictation run is the worst case
/// for it: the recording has just released the microphone, and on hardware that shares a
/// capture and a playback endpoint (a headset above all) Windows reconfigures the endpoint when
/// the microphone is let go. A beep asked for during that window waits it out. Measured on the
/// user's machine, the app was asking for the success beep 40 to 110 ms after the transcript was
/// pasted, every time, and the sound was still arriving five to ten seconds later (issue #386).
/// </para>
///
/// <para>
/// Holding the device open moves that cost to startup, where nobody is waiting on it, and leaves
/// a beep needing nothing but an array handed to a mixer. It also ends the one-sound-at-a-time
/// rule that came with the old API: sounds are added together now, so a retry beep still
/// counting cannot swallow the success beep that follows it.
/// </para>
///
/// <para>
/// The trade is worth stating plainly. Mutation now shows as an active playback session in the
/// Windows volume mixer for as long as it runs, and on a Bluetooth headset it keeps the audio
/// link awake. That is the price of a confirmation sound that arrives with the thing it is
/// confirming.
/// </para>
///
/// <para>
/// Every request is handed to one background thread rather than played on the caller's. Opening
/// a device is the slow step and it can fail, and the callers here are the UI thread finishing a
/// dictation and a Polly retry lambda mid-transcription — neither can afford to wait on an audio
/// device or to be thrown out of by one.
/// </para>
/// </summary>
public sealed class BeepAudioOutput : IDisposable
{
	/// <summary>The one format the mixer runs at; every clip is converted to it when loaded.</summary>
	public const int SampleRate = 48000;

	public const int Channels = 2;

	/// <summary>
	/// How much audio the device buffers, and in how many pieces. A beep starts at the next
	/// buffer boundary, so this is the app's own share of the delay between asking for a sound
	/// and hearing it: about 33 ms here. NAudio's default is 300 ms in two buffers, which would
	/// have put a tenth of a second between the paste and its confirmation for no reason.
	/// </summary>
	public const int DesiredLatencyMs = 100;

	public const int BufferCount = 3;

	/// <summary>
	/// Anything slower than this is worth a line in the log. The point is that the next time a
	/// beep is late, whether the app was the cause is a matter of record rather than argument
	/// (issue #386).
	/// </summary>
	public const int SlowReportThresholdMs = 250;

	/// <summary>
	/// How long to leave a device that would not open alone before trying again. Without it, a
	/// machine with no working audio output logs a failure for every beep of the session.
	/// </summary>
	public static readonly TimeSpan ReopenBackoff = TimeSpan.FromSeconds(30);

	private readonly Func<IAudioOutputDevice> _deviceFactory;
	private readonly Action<string, string> _log;
	private readonly Func<DateTimeOffset> _now;
	private readonly BlockingCollection<PlayRequest> _requests = new();
	private readonly Thread _pump;

	// Raised whenever the queue empties. The pump is asynchronous by design, so a test needs a
	// way to say "and now everything asked for has been handed over" without sleeping. The count
	// and the event are moved together under one lock, or a beep queued in the instant the pump
	// finished the one before it would leave the event saying there was nothing left to do.
	private readonly ManualResetEventSlim _idle = new(initialState: true);
	private readonly object _idleLock = new();
	private int _pending;

	private IAudioOutputDevice? _device;
	private MixingSampleProvider? _mixer;
	private DateTimeOffset _nextOpenAttempt = DateTimeOffset.MinValue;
	private volatile bool _deviceFaulted;
	private bool _disposed;

	public BeepAudioOutput()
		: this(() => new WaveOutAudioOutputDevice(DesiredLatencyMs, BufferCount))
	{
	}

	/// <param name="deviceFactory">Makes the output device. Injected so the rules here can be tested without audio hardware.</param>
	/// <param name="log">Where slow opens and failures are reported. Defaults to the app's error log.</param>
	/// <param name="now">The clock, so the reopen backoff can be tested without waiting on it.</param>
	public BeepAudioOutput(
		Func<IAudioOutputDevice> deviceFactory,
		Action<string, string>? log = null,
		Func<DateTimeOffset>? now = null)
	{
		_deviceFactory = deviceFactory ?? throw new ArgumentNullException(nameof(deviceFactory));
		_log = log ?? ErrorLogger.LogInfo;
		_now = now ?? (() => DateTimeOffset.UtcNow);

		_pump = new Thread(Pump)
		{
			IsBackground = true,
			Name = "Mutation beep output",
		};
		_pump.Start();
	}

	/// <summary>How many times a device has actually been opened. Zero until the first beep, or the first <see cref="Warm"/>.</summary>
	public int DeviceOpenCount { get; private set; }

	/// <summary>
	/// Opens the device ahead of the first beep, so the one slow open of the session happens at
	/// startup instead of under a user who is waiting to hear something. Returns immediately.
	/// </summary>
	public void Warm() => Enqueue(new PlayRequest(Clip: null, RepeatCount: 1, QueuedAt: Stopwatch.GetTimestamp()));

	/// <summary>
	/// Asks for <paramref name="clip"/> to be played <paramref name="repeatCount"/> times over.
	/// Returns straight away and never throws: a beep that cannot be played must never take down
	/// the operation it was reporting on.
	/// </summary>
	public void Play(BeepClip clip, int repeatCount = 1)
	{
		ArgumentNullException.ThrowIfNull(clip);
		if (repeatCount <= 0)
			return;

		// Checked here rather than left to the mixer. NAudio's mixer adds the input to its list
		// first and validates the format afterwards, so a clip in the wrong format is already in
		// the mix by the time it throws — and would then be played, at the wrong rate, over
		// everything else. Every clip the app builds goes through BeepClipReader in this format,
		// so this only ever catches a mistake made outside it.
		if (clip.SampleRate != SampleRate || clip.Channels != Channels)
		{
			_log("Beep", $"A beep in {clip.SampleRate} Hz / {clip.Channels} channel form was not played; the mixer runs at {SampleRate} Hz / {Channels} channels.");
			return;
		}

		Enqueue(new PlayRequest(clip, repeatCount, Stopwatch.GetTimestamp()));
	}

	private void Enqueue(PlayRequest request)
	{
		if (_disposed)
			return;

		try
		{
			lock (_idleLock)
			{
				_pending++;
				_idle.Reset();
			}

			_requests.Add(request);
		}
		catch (Exception)
		{
			// The window closed underneath a beep that was on its way in. There is nothing left
			// to play it on, and a shutdown race must not become an exception at a call site
			// that was only trying to make a noise.
			try { MarkServed(); } catch { }
		}
	}

	private void MarkServed()
	{
		lock (_idleLock)
		{
			if (--_pending <= 0)
			{
				_pending = 0;
				_idle.Set();
			}
		}
	}

	/// <summary>
	/// Waits until every request queued so far has been handed to the mixer. A test seam; nothing
	/// in the app waits for a beep.
	/// </summary>
	internal bool WaitForIdle(TimeSpan timeout) => _idle.Wait(timeout);

	private void Pump()
	{
		try
		{
			foreach (var request in _requests.GetConsumingEnumerable())
			{
				try
				{
					Serve(request);
				}
				catch (Exception ex)
				{
					// The device has gone — pulled out, reconfigured, or refused. Drop it and let
					// the next beep open a fresh one rather than failing for the rest of the run.
					CloseDevice();
					_nextOpenAttempt = _now() + ReopenBackoff;
					_log("Beep", $"Could not play a beep: {ex.Message}. The audio device will be reopened on a later beep.");
				}

				MarkServed();
			}
		}
		catch (ObjectDisposedException)
		{
			// Disposed underneath the enumerator. Shutting down is the whole point.
		}
		finally
		{
			// Both of these can throw if shutdown gave up waiting for this thread and disposed
			// the queue and the event underneath it — see Dispose. An exception escaping a
			// background thread's finally takes the process down with it, which is a poor way
			// for an app to close because a beep was still in the air.
			try
			{
				lock (_idleLock)
				{
					_pending = 0;
					_idle.Set();
				}
			}
			catch (ObjectDisposedException) { }

			try { CloseDevice(); } catch { }
		}
	}

	private void Serve(PlayRequest request)
	{
		if (_deviceFaulted)
			CloseDevice();

		if (_device is null && _now() < _nextOpenAttempt)
			return;

		OpenDeviceIfNeeded();

		if (request.Clip is null || _mixer is null)
			return;

		_mixer.AddMixerInput((ISampleProvider)new BeepClipSampleProvider(request.Clip, request.RepeatCount));

		var waited = Stopwatch.GetElapsedTime(request.QueuedAt);
		if (waited.TotalMilliseconds > SlowReportThresholdMs)
			_log("Beep", $"A beep waited {waited.TotalMilliseconds:F0} ms to reach the speaker.");
	}

	private void OpenDeviceIfNeeded()
	{
		if (_device is not null)
			return;

		var mixer = new MixingSampleProvider(WaveFormat.CreateIeeeFloatWaveFormat(SampleRate, Channels))
		{
			// Silence rather than nothing when no beep is playing. Without it the mixer reports
			// that the audio ran out the moment the first beep ends, the device stops, and every
			// beep after that pays the open cost this class exists to avoid.
			ReadFully = true,
		};

		var started = Stopwatch.GetTimestamp();
		var device = _deviceFactory();
		try
		{
			device.PlaybackStopped += OnPlaybackStopped;
			device.Init(mixer.ToWaveProvider16());
			device.Play();
		}
		catch
		{
			device.PlaybackStopped -= OnPlaybackStopped;
			try { device.Dispose(); } catch { }
			throw;
		}

		_device = device;
		_mixer = mixer;
		_deviceFaulted = false;
		_nextOpenAttempt = DateTimeOffset.MinValue;
		DeviceOpenCount++;

		var elapsed = Stopwatch.GetElapsedTime(started);
		if (elapsed.TotalMilliseconds > SlowReportThresholdMs)
			_log("Beep", $"Opening the audio output took {elapsed.TotalMilliseconds:F0} ms.");
	}

	// Raised on the device's own thread when playback ends. With ReadFully the audio never runs
	// out, so this only happens when the device fails or is stopped — either way the handle is no
	// longer worth beeping into.
	private void OnPlaybackStopped(object? sender, StoppedEventArgs e) => _deviceFaulted = true;

	private void CloseDevice()
	{
		var device = _device;
		_device = null;
		_mixer = null;
		_deviceFaulted = false;

		if (device is null)
			return;

		device.PlaybackStopped -= OnPlaybackStopped;
		try { device.Stop(); } catch { }
		try { device.Dispose(); } catch { }
	}

	public void Dispose()
	{
		if (_disposed)
			return;
		_disposed = true;

		try { _requests.CompleteAdding(); } catch { }

		// Bounded, because closing the window must not hang on an audio driver. The device is
		// closed by the pump's finally either way, and by the process if the driver never lets
		// go. Only tidy up behind the pump if it actually finished: pulling the queue and the
		// event out from under a thread that is still running them is how a slow driver would
		// turn a clean exit into a crash.
		if (_pump.Join(TimeSpan.FromSeconds(2)))
		{
			try { _requests.Dispose(); } catch { }
			try { _idle.Dispose(); } catch { }
		}
	}

	/// <param name="Clip">Null asks only that the device be opened — see <see cref="Warm"/>.</param>
	/// <param name="QueuedAt">A <see cref="Stopwatch"/> timestamp, so the wait can be measured.</param>
	private readonly record struct PlayRequest(BeepClip? Clip, int RepeatCount, long QueuedAt);
}
