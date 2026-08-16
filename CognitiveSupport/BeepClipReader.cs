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
		using var decoded = Decode(reader);
		ISampleProvider source = decoded.Provider;

		// Channels first, then rate. The resampler keeps whatever channel count it is handed, so
		// doing it in this order means only one of them ever has to think about the other.
		source = MatchChannels(source, channels);
		if (source.WaveFormat.SampleRate != sampleRate)
			source = new WdlResamplingSampleProvider(source, sampleRate);

		return new BeepClip(ReadToEnd(source, sampleRate, channels), sampleRate, channels);
	}

	/// <summary>
	/// Gets samples out of whatever kind of <c>.wav</c> the user pointed the setting at.
	/// <para>
	/// NAudio reads plain PCM and 32-bit float directly, and that covers nearly every beep file
	/// anyone has. It refuses two shapes that the old <c>PlaySound</c> path played without
	/// complaint, and both are common enough to matter: a file written in the "extensible"
	/// layout, which many tools use for float audio and which Windows requires above two
	/// channels; and a compressed one such as ADPCM or mu-law. Losing those would mean a sound
	/// the user has been hearing for months going quiet the day this shipped, which is not a
	/// trade worth making for a beep that arrives on time.
	/// </para>
	/// <para>
	/// Extensible files are relabelled: the layout only wraps ordinary PCM or float samples, and
	/// the sub-format written into the header says which. A compressed file is handed to
	/// Windows' own audio codecs — the same ones the old path relied on.
	/// </para>
	/// </summary>
	private static DecodedWave Decode(WaveFileReader reader)
	{
		try
		{
			return new DecodedWave(reader.ToSampleProvider(), null);
		}
		catch (ArgumentException)
		{
			// Not a shape NAudio converts on its own. Fall through.
		}

		var relabelled = AsStandardFormat(reader.WaveFormat);
		if (relabelled is not null)
		{
			var raw = new RawSourceWaveStream(reader, relabelled);
			return new DecodedWave(raw.ToSampleProvider(), raw);
		}

		// Compressed audio. CreatePcmStream goes through Windows' installed codecs, and throws
		// if none of them knows this format — which is the honest answer, and reaches the user
		// as a named beep file that could not be loaded.
		var pcm = WaveFormatConversionStream.CreatePcmStream(reader);
		return new DecodedWave(pcm.ToSampleProvider(), pcm);
	}

	// The sub-format GUID of an extensible header, whose first four bytes say PCM (1) or IEEE
	// float (3). It sits after the two bytes of valid-bits and the four of channel mask.
	private const int SubFormatOffset = 6;
	private const byte SubFormatPcm = 1;
	private const byte SubFormatIeeeFloat = 3;

	private static WaveFormat? AsStandardFormat(WaveFormat format)
	{
		if (format.Encoding != WaveFormatEncoding.Extensible || format is not WaveFormatExtraData extra)
			return null;

		var data = extra.ExtraData;
		if (data is null || data.Length < SubFormatOffset + 4)
			return null;

		return data[SubFormatOffset] switch
		{
			SubFormatIeeeFloat when format.BitsPerSample == 32 =>
				WaveFormat.CreateIeeeFloatWaveFormat(format.SampleRate, format.Channels),
			SubFormatPcm =>
				new WaveFormat(format.SampleRate, format.BitsPerSample, format.Channels),
			_ => null,
		};
	}

	/// <param name="Owned">
	/// The stream wrapped around the reader, when one was needed. Held so it is closed with the
	/// clip rather than left to a finalizer.
	/// </param>
	private readonly record struct DecodedWave(ISampleProvider Provider, IDisposable? Owned) : IDisposable
	{
		public void Dispose() => Owned?.Dispose();
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
