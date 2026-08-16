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
		: this(desiredLatencyMs: null, bufferCount: null)
	{
	}

	/// <param name="desiredLatencyMs">
	/// How much audio the device holds ahead of what is being heard, split across
	/// <paramref name="bufferCount"/> buffers. Null keeps NAudio's own default, which is right
	/// for playing a recording back and much too generous for a beep — see
	/// <see cref="BeepAudioOutput.DesiredLatencyMs"/>.
	/// </param>
	public WaveOutAudioOutputDevice(int? desiredLatencyMs, int? bufferCount)
	{
		if (desiredLatencyMs.HasValue)
			_waveOut.DesiredLatency = desiredLatencyMs.Value;
		if (bufferCount.HasValue)
			_waveOut.NumberOfBuffers = bufferCount.Value;

		_waveOut.PlaybackStopped += (_, e) => PlaybackStopped?.Invoke(this, e);
	}

	public event EventHandler<StoppedEventArgs>? PlaybackStopped;

	public PlaybackState PlaybackState => _waveOut.PlaybackState;

	public void Init(IWaveProvider waveProvider) => _waveOut.Init(waveProvider);

	public void Play() => _waveOut.Play();

	public void Stop() => _waveOut.Stop();

	public void Dispose() => _waveOut.Dispose();
}
