using System;
using NAudio.Wave;

namespace CognitiveSupport;

/// <summary>
/// Plays one <see cref="BeepClip"/> through the beep mixer, back-to-back
/// <c>repeatCount</c> times, and then ends so the mixer drops it.
/// <para>
/// It must fill every read completely until it is genuinely finished. NAudio's mixer drops an
/// input the moment a read comes back short — measured against NAudio 2.3.0, the test is
/// "fewer samples than asked for", not "no samples at all" — so a beep that returned a partial
/// buffer for any reason other than reaching its end would be cut off there and never heard
/// again. The samples in that last short read are mixed before the input is dropped, so ending
/// this way loses nothing.
/// </para>
/// <para>
/// The repeat lives here rather than in a loop at the call site because the mixer is what makes
/// counting possible at all. The old code ran the clip synchronously on a background thread, N
/// times over, and Windows only lets one sound play per process — so a success beep raised
/// while a retry beep was still counting stopped it dead, and was stopped in turn by the retry's
/// next repetition. Mixed instead of interrupted, both are heard (issue #386).
/// </para>
/// </summary>
internal sealed class BeepClipSampleProvider : ISampleProvider
{
	private readonly BeepClip _clip;
	private readonly int _repeatCount;
	private int _repeatsDone;
	private int _position;
	private Action? _onFirstRead;

	/// <param name="onFirstRead">
	/// Called once, on the audio device's thread, the first time the device pulls samples from
	/// this beep. That is the moment the beep leaves the app for the speaker, and the last one the
	/// app can see — which is what tells a late beep held up in here from one held up in Windows
	/// (issue #411).
	/// </param>
	public BeepClipSampleProvider(BeepClip clip, int repeatCount, Action? onFirstRead = null)
	{
		ArgumentNullException.ThrowIfNull(clip);
		if (repeatCount <= 0)
			throw new ArgumentOutOfRangeException(nameof(repeatCount));

		_clip = clip;
		_repeatCount = repeatCount;
		_onFirstRead = onFirstRead;
		WaveFormat = WaveFormat.CreateIeeeFloatWaveFormat(clip.SampleRate, clip.Channels);
	}

	public WaveFormat WaveFormat { get; }

	public int Read(float[] buffer, int offset, int count)
	{
		ArgumentNullException.ThrowIfNull(buffer);

		var firstRead = _onFirstRead;
		if (firstRead is not null)
		{
			_onFirstRead = null;
			try { firstRead(); } catch { /* A report must never stop the beep it reports on. */ }
		}

		var samples = _clip.Samples.Span;
		int written = 0;

		while (written < count && _repeatsDone < _repeatCount)
		{
			// An empty clip would otherwise spin here for ever: nothing is copied, the position
			// never reaches the end, and the repeat is never counted off.
			if (samples.Length == 0)
			{
				_repeatsDone = _repeatCount;
				break;
			}

			int available = Math.Min(count - written, samples.Length - _position);
			samples.Slice(_position, available).CopyTo(buffer.AsSpan(offset + written, available));
			written += available;
			_position += available;

			if (_position >= samples.Length)
			{
				_position = 0;
				_repeatsDone++;
			}
		}

		return written;
	}
}
