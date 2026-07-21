using System;
using System.IO;
using CognitiveSupport;
using Concentus.Enums;
using Concentus.Oggfile;
using Concentus.Structs;

namespace Mutation.Tests;

/// <summary>
/// Covers the managed Ogg/Opus input path added because Media Foundation cannot demux
/// Ogg/Opus (it throws 0xC00D36C4). Both ends of this round trip are pure Concentus, so
/// these tests need no codec support from the host OS.
/// </summary>
public class AudioFileConverterOggTests : IDisposable
{
	private const int SampleRate = 48000;
	private const int FrameSize = 960; // 20ms

	private readonly string _workingDirectory = Directory.CreateTempSubdirectory("ogg-convert-tests").FullName;

	public void Dispose()
	{
		try { Directory.Delete(_workingDirectory, recursive: true); } catch { }
	}

	private string PathFor(string fileName) => Path.Combine(_workingDirectory, fileName);

	private static SilenceTrimmerOptions StripSilence() =>
		new(SilenceThresholdDbFs: -40.0, MinSilenceSeconds: 0.2, GuardMilliseconds: 40.0);

	/// <summary>
	/// Writes an Ogg/Opus file of tone / silence / tone, one second each.
	/// </summary>
	private string WriteFixture(string fileName, int channels)
	{
		string path = PathFor(fileName);

		using var outStream = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None);
#pragma warning disable CS0618 // Concentus.OggFile 1.0.6 requires the concrete OpusEncoder type.
		var encoder = new OpusEncoder(SampleRate, channels, OpusApplication.OPUS_APPLICATION_AUDIO)
		{
			Bitrate = 32000,
			UseDTX = false
		};
#pragma warning restore CS0618
		var oggStream = new OpusOggWriteStream(encoder, outStream, new OpusTags());

		int framesPerSecond = SampleRate / FrameSize;
		WriteSeconds(oggStream, channels, framesPerSecond, loud: true);
		WriteSeconds(oggStream, channels, framesPerSecond, loud: false);
		WriteSeconds(oggStream, channels, framesPerSecond, loud: true);

		oggStream.Finish();
		return path;
	}

	private static void WriteSeconds(OpusOggWriteStream oggStream, int channels, int frameCount, bool loud)
	{
		var frame = new short[FrameSize * channels];
		if (loud)
		{
			// A 440Hz tone; a DC-constant signal would be trimmed as "silence" by nothing but
			// is also unrealistic for a codec round trip.
			for (int sample = 0; sample < FrameSize; sample++)
			{
				short value = (short)(12000 * Math.Sin(2 * Math.PI * 440 * sample / SampleRate));
				for (int channel = 0; channel < channels; channel++)
					frame[sample * channels + channel] = value;
			}
		}

		for (int i = 0; i < frameCount; i++)
			oggStream.WriteSamples(frame, 0, frame.Length);
	}

	private static int CountDecodedSamples(string oggPath)
	{
		int total = 0;
		OggOpusPcmDecoder.DecodeToMonoPcm(oggPath, samples => total += samples.Length);
		return total;
	}

	private static int CountNonZeroSamples(string oggPath)
	{
		int nonZero = 0;
		OggOpusPcmDecoder.DecodeToMonoPcm(oggPath, samples =>
		{
			foreach (short sample in samples)
			{
				if (sample != 0) nonZero++;
			}
		});
		return nonZero;
	}

	[Fact]
	public void MonoOggOpus_WithoutSilenceStripping_KeepsFullDuration()
	{
		string input = WriteFixture("mono.ogg", channels: 1);
		string output = PathFor("mono-out.ogg");

		AudioFileConverter.ConvertToOgg(input, output, silenceOptions: null);

		int decoded = CountDecodedSamples(output);
		// ~3 seconds in, ~3 seconds out (allow a frame or two of codec padding either way).
		Assert.InRange(decoded, SampleRate * 3 - FrameSize * 4, SampleRate * 3 + FrameSize * 4);
		Assert.True(CountNonZeroSamples(output) > 0, "Converted audio should not be all zeros.");
	}

	[Fact]
	public void MonoOggOpus_WithSilenceStripping_DropsTheSilentGap()
	{
		string input = WriteFixture("mono-gap.ogg", channels: 1);
		string output = PathFor("mono-gap-out.ogg");

		AudioFileConverter.ConvertToOgg(input, output, StripSilence());

		int decoded = CountDecodedSamples(output);
		// The one-second gap collapses to the 0.2s threshold, so well under three seconds.
		Assert.True(decoded > 0, "Trimmed audio should still contain the speech.");
		Assert.True(decoded < SampleRate * 3 - SampleRate / 2,
			$"Expected the silent gap to be trimmed, but got {decoded} samples.");
	}

	[Fact]
	public void StereoOggOpus_IsDownmixedToMono()
	{
		string input = WriteFixture("stereo.ogg", channels: 2);
		string output = PathFor("stereo-out.ogg");

		Assert.Equal(2, OggOpusPcmDecoder.ReadChannelCount(input));

		AudioFileConverter.ConvertToOgg(input, output, silenceOptions: null);

		Assert.Equal(1, OggOpusPcmDecoder.ReadChannelCount(output));
		Assert.True(CountNonZeroSamples(output) > 0, "Downmixed audio should not be all zeros.");
	}

	[Fact]
	public void IsOggOpus_DetectsOpusStreams()
	{
		string input = WriteFixture("detect.ogg", channels: 1);

		Assert.True(OggOpusPcmDecoder.IsOggOpus(input));
	}

	[Fact]
	public void IsOggOpus_RejectsNonOpusFiles()
	{
		string wavPath = PathFor("not-opus.wav");
		File.WriteAllBytes(wavPath, "RIFF\0\0\0\0WAVEfmt "u8.ToArray());

		string garbagePath = PathFor("garbage.bin");
		File.WriteAllBytes(garbagePath, new byte[512]);

		Assert.False(OggOpusPcmDecoder.IsOggOpus(wavPath));
		Assert.False(OggOpusPcmDecoder.IsOggOpus(garbagePath));
		Assert.False(OggOpusPcmDecoder.IsOggOpus(PathFor("missing.ogg")));
	}
}
