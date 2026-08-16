using System;
using System.IO;
using System.Linq;
using CognitiveSupport;
using Xunit;

namespace Mutation.Tests;

/// <summary>
/// Beeps are mixed together now rather than played one at a time, and a mixer can only add
/// sources that agree on sample rate and channel count. The user's own sound files do not agree
/// — the ones shipped with the app are a mix of 44.1 kHz and 48 kHz — so everything is converted
/// to one format when it is loaded. These tests are about that conversion (issue #386).
/// </summary>
public class BeepClipReaderTests
{
	private const int Rate = BeepAudioOutput.SampleRate;
	private const int Channels = BeepAudioOutput.Channels;

	// A PCM 16-bit WAV of the given shape, filled with a value that is easy to recognise again.
	private static byte[] Wav(int sampleRate, short channels, int frames, short value = 4000)
	{
		using var stream = new MemoryStream();
		using var writer = new BinaryWriter(stream);
		int dataBytes = frames * channels * 2;
		writer.Write("RIFF"u8);
		writer.Write(36 + dataBytes);
		writer.Write("WAVE"u8);
		writer.Write("fmt "u8);
		writer.Write(16);
		writer.Write((short)1);
		writer.Write(channels);
		writer.Write(sampleRate);
		writer.Write(sampleRate * channels * 2);
		writer.Write((short)(channels * 2));
		writer.Write((short)16);
		writer.Write("data"u8);
		writer.Write(dataBytes);
		for (var i = 0; i < frames * channels; i++)
			writer.Write(value);
		writer.Flush();
		return stream.ToArray();
	}

	[Fact]
	public void A_clip_always_comes_back_in_the_mixer_format()
	{
		var clip = BeepClipReader.ReadBytes(Wav(sampleRate: 22050, channels: 1, frames: 22050), Rate, Channels);

		Assert.Equal(Rate, clip.SampleRate);
		Assert.Equal(Channels, clip.Channels);
	}

	// The synthesized tones are 22.05 kHz mono and the device runs at 48 kHz stereo, so this is
	// the conversion every default beep goes through.
	[Fact]
	public void Upsampling_keeps_the_clip_the_same_length_in_time()
	{
		var clip = BeepClipReader.ReadBytes(Wav(sampleRate: 22050, channels: 1, frames: 22050), Rate, Channels);

		Assert.InRange(clip.Duration.TotalSeconds, 0.98, 1.02);
	}

	// 44.1 kHz is the other rate the user's own beep files come in.
	[Fact]
	public void Downsampling_from_44100_keeps_the_clip_the_same_length_in_time()
	{
		var clip = BeepClipReader.ReadBytes(Wav(sampleRate: 44100, channels: 2, frames: 22050), Rate, Channels);

		Assert.InRange(clip.Duration.TotalSeconds, 0.48, 0.52);
	}

	[Fact]
	public void A_mono_file_is_heard_in_both_ears()
	{
		var clip = BeepClipReader.ReadBytes(Wav(sampleRate: Rate, channels: 1, frames: 1000), Rate, Channels);

		var samples = clip.Samples.ToArray();
		Assert.Equal(2000, samples.Length);
		// Interleaved: left and right of the same frame carry the same value.
		for (var frame = 0; frame < 1000; frame++)
			Assert.Equal(samples[frame * 2], samples[frame * 2 + 1]);
		Assert.Contains(samples, s => s != 0f);
	}

	// A clip ending mid-frame would swap left and right for every sound mixed after it.
	[Fact]
	public void A_clip_holds_whole_frames_only()
	{
		var clip = BeepClipReader.ReadBytes(Wav(sampleRate: 44100, channels: 2, frames: 7777), Rate, Channels);

		Assert.Equal(0, clip.Samples.Length % Channels);
	}

	[Fact]
	public void A_file_longer_than_a_beep_has_any_business_being_is_cut_short()
	{
		var overlong = Wav(sampleRate: Rate, channels: 1, frames: Rate * 45);

		var clip = BeepClipReader.ReadBytes(overlong, Rate, Channels);

		Assert.InRange(clip.Duration, TimeSpan.FromSeconds(29.9), BeepClipReader.MaxDuration);
	}

	[Fact]
	public void Something_that_is_not_a_wav_file_is_refused_rather_than_played_as_noise()
	{
		var garbage = new byte[] { 1, 2, 3, 4, 5, 6, 7, 8 };

		Assert.ThrowsAny<Exception>(() => BeepClipReader.ReadBytes(garbage, Rate, Channels));
	}

	// The real beep files ship next to the test host, so this is the shape the app actually
	// loads at startup rather than one invented here.
	[Theory]
	[InlineData("Success.wav")]
	[InlineData("End.wav")]
	[InlineData("Failure.wav")]
	[InlineData("Start.wav")]
	public void The_beep_files_that_ship_with_the_app_load_and_have_audio_in_them(string fileName)
	{
		var path = Path.Combine(AppContext.BaseDirectory, "CustomAudio", fileName);
		Assert.True(File.Exists(path), $"The fixture assumes {fileName} is copied next to the tests.");

		var clip = BeepClipReader.ReadFile(path, Rate, Channels);

		Assert.Equal(Rate, clip.SampleRate);
		Assert.Equal(Channels, clip.Channels);
		Assert.True(clip.Duration > TimeSpan.Zero);
		Assert.Contains(clip.Samples.ToArray(), s => s != 0f);
	}
}
