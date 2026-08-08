using System;
using CognitiveSupport;
using NAudio.Wave;

namespace Mutation.Tests;

/// <summary>
/// A speaker that makes no sound. The build machine has no audio hardware, and the rules
/// worth testing around <see cref="AudioPlayer"/> are about ownership and threading rather
/// than about what comes out.
/// </summary>
internal sealed class FakeAudioOutputDevice : IAudioOutputDevice
{
	public int DisposeCount { get; private set; }

	public bool IsPlaying => PlaybackState == PlaybackState.Playing;

	public PlaybackState PlaybackState { get; private set; } = PlaybackState.Stopped;

	/// <summary>
	/// The synchronization context in force where this device was constructed. NAudio's real
	/// <c>WaveOutEvent</c> captures exactly this to post <c>PlaybackStopped</c> back to, so a
	/// device built with no context raises its events on the playback thread instead of the
	/// UI one.
	/// </summary>
	public System.Threading.SynchronizationContext? CreatedOnContext { get; } =
		System.Threading.SynchronizationContext.Current;

	public event EventHandler<StoppedEventArgs>? PlaybackStopped;

	/// <summary>
	/// The chain the player handed over to be played. A real device pulls samples from this;
	/// a test can pull from it too, which is the only way to observe what the time-stretch
	/// stage actually did to the audio.
	/// </summary>
	public IWaveProvider? Provider { get; private set; }

	public void Init(IWaveProvider waveProvider)
	{
		Provider = waveProvider;
	}

	public void Play() => PlaybackState = PlaybackState.Playing;

	public void Stop() => PlaybackState = PlaybackState.Stopped;

	/// <summary>Stands in for the device reporting that the audio ran out, or failed.</summary>
	public void RaisePlaybackStopped(Exception? exception = null)
	{
		PlaybackState = PlaybackState.Stopped;
		PlaybackStopped?.Invoke(this, new StoppedEventArgs(exception));
	}

	public void Dispose()
	{
		DisposeCount++;
		PlaybackState = PlaybackState.Stopped;
	}
}
