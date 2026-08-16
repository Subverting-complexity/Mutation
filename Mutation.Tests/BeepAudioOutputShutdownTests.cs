using System;
using System.Threading;
using CognitiveSupport;
using NAudio.Wave;
using Xunit;

namespace Mutation.Tests;

/// <summary>
/// Closing down while a beep is still being handed to a slow audio device.
///
/// <para>
/// This is the case the whole change is about, seen from the other end. The device open is
/// unbounded — that is why beeps were arriving late in the first place (issue #386) — so the
/// window can perfectly well close while the beep thread is still inside one. Shutdown waits
/// two seconds for that thread and then stops waiting, and everything it does afterwards has to
/// be safe against the thread still running. An exception escaping a background thread's
/// <c>finally</c> ends the process, which is a poor way for an app to close because a beep was
/// still in the air.
/// </para>
/// </summary>
public class BeepAudioOutputShutdownTests
{
	[Fact]
	public void Closing_down_while_the_device_is_still_opening_does_not_bring_the_process_down()
	{
		var opening = new ManualResetEventSlim(false);
		var release = new ManualResetEventSlim(false);
		var escaped = (Exception?)null;

		var previousHandler = new UnhandledExceptionEventHandler((_, e) => escaped = e.ExceptionObject as Exception);
		AppDomain.CurrentDomain.UnhandledException += previousHandler;
		try
		{
			var output = new BeepAudioOutput(
				() => new BlockingDevice(opening, release),
				log: (_, _) => { });

			output.Warm();
			Assert.True(opening.Wait(TimeSpan.FromSeconds(5)), "The device never started opening.");

			// Shutdown gives up on the pump after two seconds; the device is still held open
			// past that, so the pump is guaranteed to still be running when Dispose returns.
			output.Dispose();

			release.Set();
			// Long enough for the pump to run its finally against whatever Dispose left behind.
			Thread.Sleep(500);

			Assert.Null(escaped);
		}
		finally
		{
			AppDomain.CurrentDomain.UnhandledException -= previousHandler;
			release.Set();
			opening.Dispose();
			release.Dispose();
		}
	}

	[Fact]
	public void Asking_for_a_beep_while_the_window_is_closing_is_dropped_rather_than_thrown()
	{
		var output = new BeepAudioOutput(() => new FakeAudioOutputDevice(), log: (_, _) => { });
		var clip = new BeepClip(new float[64], BeepAudioOutput.SampleRate, BeepAudioOutput.Channels);

		output.Dispose();

		Assert.Null(Record.Exception(() =>
		{
			output.Play(clip);
			output.Warm();
			output.Dispose();
		}));
	}

	/// <summary>An audio device that takes as long to open as the test tells it to.</summary>
	private sealed class BlockingDevice : IAudioOutputDevice
	{
		private readonly ManualResetEventSlim _opening;
		private readonly ManualResetEventSlim _release;

		public BlockingDevice(ManualResetEventSlim opening, ManualResetEventSlim release)
		{
			_opening = opening;
			_release = release;
		}

		public event EventHandler<StoppedEventArgs>? PlaybackStopped;

		public PlaybackState PlaybackState { get; private set; } = PlaybackState.Stopped;

		public void Init(IWaveProvider waveProvider)
		{
			_opening.Set();
			_release.Wait(TimeSpan.FromSeconds(30));
		}

		public void Play() => PlaybackState = PlaybackState.Playing;

		public void Stop() => PlaybackState = PlaybackState.Stopped;

		public void Dispose()
		{
			PlaybackState = PlaybackState.Stopped;
			PlaybackStopped?.Invoke(this, new StoppedEventArgs());
		}
	}
}
