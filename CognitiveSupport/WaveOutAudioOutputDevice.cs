using System;
using NAudio.Wave;

namespace CognitiveSupport;

/// <summary>
/// The real speaker: <see cref="IAudioOutputDevice"/> backed by NAudio's <c>WaveOutEvent</c>.
/// </summary>
internal sealed class WaveOutAudioOutputDevice : IAudioOutputDevice
{
	private readonly WaveOutEvent _waveOut = new();

	public WaveOutAudioOutputDevice()
	{
		_waveOut.PlaybackStopped += (_, e) => PlaybackStopped?.Invoke(this, e);
	}

	public event EventHandler<StoppedEventArgs>? PlaybackStopped;

	public PlaybackState PlaybackState => _waveOut.PlaybackState;

	public void Init(IWaveProvider waveProvider) => _waveOut.Init(waveProvider);

	public void Play() => _waveOut.Play();

	public void Stop() => _waveOut.Stop();

	public void Dispose() => _waveOut.Dispose();
}
