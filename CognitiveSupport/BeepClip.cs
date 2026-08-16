using System;

namespace CognitiveSupport;

/// <summary>
/// One beep's audio, decoded into memory and already in the format the beep mixer runs at.
/// <para>
/// Beeps used to be played straight off disk by <c>System.Media.SoundPlayer</c>, which asks
/// Windows to open an audio device, read the file and close the device again for every single
/// sound. All of that happened after the app had asked for the beep and before anything was
/// audible, and none of it was bounded — which is how a success sound came to arrive five to
/// ten seconds after the text it was confirming (issue #386). Holding the samples here means
/// the only work left at the moment a beep is wanted is handing an array to a device that is
/// already open.
/// </para>
/// </summary>
public sealed class BeepClip
{
	public BeepClip(float[] samples, int sampleRate, int channels)
	{
		ArgumentNullException.ThrowIfNull(samples);
		if (sampleRate <= 0)
			throw new ArgumentOutOfRangeException(nameof(sampleRate));
		if (channels <= 0)
			throw new ArgumentOutOfRangeException(nameof(channels));

		Samples = samples;
		SampleRate = sampleRate;
		Channels = channels;
	}

	/// <summary>
	/// Interleaved samples, one float per channel per frame. Read-only by contract: a clip is
	/// shared by every playback of that beep and several can be mixing at once.
	/// </summary>
	public ReadOnlyMemory<float> Samples { get; }

	public int SampleRate { get; }

	public int Channels { get; }

	public TimeSpan Duration =>
		TimeSpan.FromSeconds((double)Samples.Length / Channels / SampleRate);
}
