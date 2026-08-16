using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using CognitiveSupport;
using NAudio.Wave;
using Xunit;

namespace Mutation.Tests;

/// <summary>
/// The rules that make a beep arrive when it is asked for (issue #386).
///
/// <para>
/// Measured on the user's machine, the app was asking for the success beep 40 to 110 ms after
/// the transcript had been pasted, every time, and the sound was still arriving five to ten
/// seconds later. The old <c>SoundPlayer</c> path owned no device, so Windows opened the default
/// output, played, and closed it again for every single beep — and a dictation run is exactly
/// when that open is slow, because the microphone has just been released. This class holds the
/// device open instead. What is tested here is that it really is held, that a beep never waits
/// on it, and that a device which fails is replaced rather than beeped into for the rest of the
/// session.
/// </para>
/// </summary>
public class BeepAudioOutputTests
{
	private static readonly TimeSpan Patience = TimeSpan.FromSeconds(5);

	private static BeepClip Clip(float value = 0.25f, int frames = 4800) =>
		new(Fill(frames * BeepAudioOutput.Channels, value), BeepAudioOutput.SampleRate, BeepAudioOutput.Channels);

	private static float[] Fill(int length, float value)
	{
		var samples = new float[length];
		for (var i = 0; i < length; i++)
			samples[i] = value;
		return samples;
	}

	// Every device this factory hands out, in the order they were made.
	private sealed class RecordingFactory
	{
		public List<FakeAudioOutputDevice> Devices { get; } = new();

		public IAudioOutputDevice Create()
		{
			var device = new FakeAudioOutputDevice();
			lock (Devices)
				Devices.Add(device);
			return device;
		}

		public FakeAudioOutputDevice this[int index]
		{
			get { lock (Devices) return Devices[index]; }
		}

		public int Count
		{
			get { lock (Devices) return Devices.Count; }
		}
	}

	// Reads 16-bit samples back out of what the device was handed, which is the only way to see
	// what a listener would actually hear.
	private static short[] Pull(FakeAudioOutputDevice device, int sampleCount)
	{
		Assert.NotNull(device.Provider);
		var bytes = new byte[sampleCount * 2];
		int read = device.Provider!.Read(bytes, 0, bytes.Length);
		var samples = new short[read / 2];
		for (var i = 0; i < samples.Length; i++)
			samples[i] = BitConverter.ToInt16(bytes, i * 2);
		return samples;
	}

	[Fact]
	public void The_device_is_opened_once_and_kept_open_however_many_beeps_are_played()
	{
		var factory = new RecordingFactory();
		using var output = new BeepAudioOutput(factory.Create, log: (_, _) => { });

		for (var i = 0; i < 20; i++)
			output.Play(Clip());
		Assert.True(output.WaitForIdle(Patience));

		Assert.Equal(1, factory.Count);
		Assert.Equal(1, output.DeviceOpenCount);
		Assert.Equal(PlaybackState.Playing, factory[0].PlaybackState);
	}

	[Fact]
	public void Warming_opens_the_device_before_any_beep_needs_it()
	{
		var factory = new RecordingFactory();
		using var output = new BeepAudioOutput(factory.Create, log: (_, _) => { });

		output.Warm();
		Assert.True(output.WaitForIdle(Patience));

		Assert.Equal(1, output.DeviceOpenCount);
	}

	// The whole point. A device that takes a second to open must not take that second out of the
	// caller — which on the transcript delivery path is the UI thread, and inside a retry ladder
	// is the thread the transcription itself is running on.
	[Fact]
	public void Asking_for_a_beep_does_not_wait_for_the_audio_device()
	{
		var slowDevice = new SlowOpeningDevice(TimeSpan.FromSeconds(1));
		using var output = new BeepAudioOutput(() => slowDevice, log: (_, _) => { });

		var stopwatch = Stopwatch.StartNew();
		output.Play(Clip());
		stopwatch.Stop();

		Assert.True(stopwatch.ElapsedMilliseconds < 250,
			$"Play blocked the caller for {stopwatch.ElapsedMilliseconds} ms.");
		Assert.True(output.WaitForIdle(Patience));
	}

	// Windows only lets one sound play per process through the old API, so a retry beep that was
	// still counting stopped the success beep that followed it, and was stopped in turn by its
	// own next repetition. Mixed, both are heard.
	[Fact]
	public void Two_beeps_at_once_are_added_together_rather_than_cutting_each_other_off()
	{
		var factory = new RecordingFactory();
		using var output = new BeepAudioOutput(factory.Create, log: (_, _) => { });

		output.Play(Clip(value: 0.25f));
		output.Play(Clip(value: 0.25f));
		Assert.True(output.WaitForIdle(Patience));

		var heard = Pull(factory[0], sampleCount: 100);

		Assert.NotEmpty(heard);
		// Two quarter-scale beeps together are half scale. One on its own would be half that,
		// and silence would be zero, so this distinguishes mixing from either failure.
		short expected = (short)(0.5f * short.MaxValue);
		Assert.All(heard, s => Assert.InRange(s, expected - 200, expected + 200));
	}

	[Fact]
	public void A_beep_reaches_the_speaker_as_audio_rather_than_silence()
	{
		var factory = new RecordingFactory();
		using var output = new BeepAudioOutput(factory.Create, log: (_, _) => { });

		output.Play(Clip(value: 0.5f));
		Assert.True(output.WaitForIdle(Patience));

		Assert.Contains(Pull(factory[0], sampleCount: 100), s => s != 0);
	}

	// With the mixer reading fully, the audio never runs out — so a stop means the device has
	// gone: unplugged, reconfigured, or failed. Beeping into it again would be silent for the
	// rest of the session.
	[Fact]
	public void A_device_that_stops_is_replaced_on_the_next_beep()
	{
		var factory = new RecordingFactory();
		using var output = new BeepAudioOutput(factory.Create, log: (_, _) => { });

		output.Play(Clip());
		Assert.True(output.WaitForIdle(Patience));
		factory[0].RaisePlaybackStopped(new InvalidOperationException("the headset went away"));

		output.Play(Clip());
		Assert.True(output.WaitForIdle(Patience));

		Assert.Equal(2, factory.Count);
		Assert.Equal(2, output.DeviceOpenCount);
		Assert.Equal(1, factory[0].DisposeCount);
	}

	[Fact]
	public void A_device_that_will_not_open_is_reported_once_rather_than_on_every_beep()
	{
		var now = new DateTimeOffset(2026, 8, 16, 12, 0, 0, TimeSpan.Zero);
		var reports = new List<string>();
		using var output = new BeepAudioOutput(
			() => new UnopenableDevice(),
			log: (_, message) => { lock (reports) reports.Add(message); },
			now: () => now);

		for (var i = 0; i < 5; i++)
		{
			output.Play(Clip());
			Assert.True(output.WaitForIdle(Patience));
		}

		lock (reports)
			Assert.Single(reports);
	}

	[Fact]
	public void A_device_that_would_not_open_is_tried_again_once_the_backoff_has_passed()
	{
		var now = new DateTimeOffset(2026, 8, 16, 12, 0, 0, TimeSpan.Zero);
		var attempts = 0;
		using var output = new BeepAudioOutput(
			() => { Interlocked.Increment(ref attempts); return new UnopenableDevice(); },
			log: (_, _) => { },
			now: () => now);

		output.Play(Clip());
		Assert.True(output.WaitForIdle(Patience));
		output.Play(Clip());
		Assert.True(output.WaitForIdle(Patience));
		Assert.Equal(1, Volatile.Read(ref attempts));

		now += BeepAudioOutput.ReopenBackoff + TimeSpan.FromSeconds(1);
		output.Play(Clip());
		Assert.True(output.WaitForIdle(Patience));

		Assert.Equal(2, Volatile.Read(ref attempts));
	}

	// A beep that will not play must never take down the operation it is reporting on: these
	// calls sit inside Polly retry lambdas, where an escaping exception aborts the transcription.
	[Fact]
	public void A_broken_audio_device_never_throws_at_the_caller()
	{
		using var output = new BeepAudioOutput(() => new UnopenableDevice(), log: (_, _) => { });

		var exception = Record.Exception(() =>
		{
			output.Play(Clip());
			output.Warm();
			output.WaitForIdle(Patience);
		});

		Assert.Null(exception);
	}

	[Fact]
	public void Closing_down_releases_the_device()
	{
		var factory = new RecordingFactory();
		var output = new BeepAudioOutput(factory.Create, log: (_, _) => { });
		output.Play(Clip());
		Assert.True(output.WaitForIdle(Patience));

		output.Dispose();

		Assert.Equal(1, factory[0].DisposeCount);
	}

	[Fact]
	public void A_beep_asked_for_after_shutdown_is_dropped_rather_than_throwing()
	{
		var factory = new RecordingFactory();
		var output = new BeepAudioOutput(factory.Create, log: (_, _) => { });
		output.Dispose();

		Assert.Null(Record.Exception(() => output.Play(Clip())));
	}

	private sealed class SlowOpeningDevice : IAudioOutputDevice
	{
		private readonly TimeSpan _openDelay;

		public SlowOpeningDevice(TimeSpan openDelay) => _openDelay = openDelay;

		public event EventHandler<StoppedEventArgs>? PlaybackStopped;

		public PlaybackState PlaybackState { get; private set; } = PlaybackState.Stopped;

		public void Init(IWaveProvider waveProvider) => Thread.Sleep(_openDelay);

		public void Play() => PlaybackState = PlaybackState.Playing;

		public void Stop() => PlaybackState = PlaybackState.Stopped;

		public void Dispose()
		{
			PlaybackStopped?.Invoke(this, new StoppedEventArgs());
			PlaybackState = PlaybackState.Stopped;
		}
	}

	/// <summary>A machine with no working audio output, which is what a build agent usually is.</summary>
	private sealed class UnopenableDevice : IAudioOutputDevice
	{
		public event EventHandler<StoppedEventArgs>? PlaybackStopped;

		public PlaybackState PlaybackState => PlaybackState.Stopped;

		public void Init(IWaveProvider waveProvider) => throw new InvalidOperationException("No audio output device.");

		public void Play() { }

		public void Stop() { }

		public void Dispose() => PlaybackStopped?.Invoke(this, new StoppedEventArgs());
	}
}
