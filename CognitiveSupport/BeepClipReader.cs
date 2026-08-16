using System;
using System.Collections.Generic;
using System.IO;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;

namespace CognitiveSupport;

/// <summary>
/// Turns a <c>.wav</c> — a file the user chose, or the tones the app synthesizes for itself —
/// into a <see cref="BeepClip"/> in one fixed format, so everything the beep mixer is given can
/// simply be added together.
/// <para>
/// The conversion has to happen somewhere. The user's own beep files are a mix of sample rates
/// (44.1 kHz and 48 kHz are both common) and the synthesized tones are 22.05 kHz mono, while a
/// mixer can only add sources that agree. Doing it here, once, when settings are loaded, is what
/// keeps it out of the moment a beep is actually wanted.
/// </para>
/// </summary>
public static class BeepClipReader
{
	/// <summary>
	/// Longest clip accepted. A beep is a beep; if someone points the setting at an album track
	/// the tail is dropped rather than held in memory for the life of the process.
	/// </summary>
	public static readonly TimeSpan MaxDuration = TimeSpan.FromSeconds(30);

	public static BeepClip ReadFile(string filePath, int sampleRate, int channels)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(filePath);
		using var stream = File.OpenRead(filePath);
		return Read(stream, sampleRate, channels);
	}

	public static BeepClip ReadBytes(byte[] wav, int sampleRate, int channels)
	{
		ArgumentNullException.ThrowIfNull(wav);
		using var stream = new MemoryStream(wav, writable: false);
		return Read(stream, sampleRate, channels);
	}

	public static BeepClip Read(Stream wav, int sampleRate, int channels)
	{
		ArgumentNullException.ThrowIfNull(wav);
		if (sampleRate <= 0)
			throw new ArgumentOutOfRangeException(nameof(sampleRate));
		if (channels is not (1 or 2))
			throw new ArgumentOutOfRangeException(nameof(channels), "Only mono and stereo are supported.");

		using var reader = new WaveFileReader(wav);
		ISampleProvider source = reader.ToSampleProvider();

		// Channels first, then rate. The resampler keeps whatever channel count it is handed, so
		// doing it in this order means only one of them ever has to think about the other.
		source = MatchChannels(source, channels);
		if (source.WaveFormat.SampleRate != sampleRate)
			source = new WdlResamplingSampleProvider(source, sampleRate);

		return new BeepClip(ReadToEnd(source, sampleRate, channels), sampleRate, channels);
	}

	private static ISampleProvider MatchChannels(ISampleProvider source, int channels)
	{
		if (source.WaveFormat.Channels == channels)
			return source;

		return (source.WaveFormat.Channels, channels) switch
		{
			(1, 2) => new MonoToStereoSampleProvider(source),
			(2, 1) => new StereoToMonoSampleProvider(source),
			_ => throw new NotSupportedException(
				$"A {source.WaveFormat.Channels}-channel beep file cannot be played as {channels}-channel audio."),
		};
	}

	private static float[] ReadToEnd(ISampleProvider source, int sampleRate, int channels)
	{
		long maxSamples = (long)(MaxDuration.TotalSeconds * sampleRate) * channels;
		var buffer = new float[sampleRate * channels / 4];
		var samples = new List<float>();

		int read;
		while (samples.Count < maxSamples && (read = source.Read(buffer, 0, buffer.Length)) > 0)
		{
			for (var i = 0; i < read && samples.Count < maxSamples; i++)
				samples.Add(buffer[i]);
		}

		// A whole number of frames, always. A clip ending mid-frame would swap the channels of
		// every sound mixed after it.
		int remainder = samples.Count % channels;
		if (remainder != 0)
			samples.RemoveRange(samples.Count - remainder, remainder);

		return samples.ToArray();
	}
}
