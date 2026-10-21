using System;
using NAudio.Wave;

namespace CognitiveSupport;

/// <summary>
/// The real speaker: <see cref="IAudioOutputDevice"/> backed by NAudio's <c>WaveOut</c>.
/// </summary>
internal sealed class WaveOutAudioOutputDevice : IAudioOutputDevice
{
	private readonly WaveOut _waveOut = new();

	public WaveOutAudioOutputDevice()
		: this(bufferMilliseconds: null, bufferCount: null)
	{
	}

	/// <param name="bufferMilliseconds">
	/// How much audio each of the <paramref name="bufferCount"/> buffers holds, so the device
	/// holds about that times the count ahead of what is being heard. Null keeps NAudio's own
	/// default, which is right for playing a recording back and much too generous for a beep —
	/// see <see cref="BeepAudioOutput.BufferMilliseconds"/>.
	/// </param>
	public WaveOutAudioOutputDevice(int? bufferMilliseconds, int? bufferCount)
	{
		if (bufferMilliseconds.HasValue)
			_waveOut.BufferMilliseconds = bufferMilliseconds.Value;
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
