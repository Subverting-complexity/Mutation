using System;
using NAudio.Wave;

namespace CognitiveSupport;

/// <summary>
/// The output device contract <see cref="AudioPlayer"/> drives. Extracting it from NAudio's
/// <c>WaveOutEvent</c> (which opens a real WinMM device) lets the player's teardown rules —
/// releasing the device when a file reaches its end, and leaving a newer playback alone — be
/// exercised in a unit test.
/// </summary>
public interface IAudioOutputDevice : IDisposable
{
	/// <summary>
	/// Raised when playback ends, whether the audio ran out or <see cref="Stop"/> was called.
	/// </summary>
	event EventHandler<StoppedEventArgs>? PlaybackStopped;

	PlaybackState PlaybackState { get; }

	void Init(IWaveProvider waveProvider);

	void Play();

	void Stop();
}
