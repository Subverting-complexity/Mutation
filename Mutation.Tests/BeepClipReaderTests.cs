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

	// A WAV in the "extensible" layout, which is what many tools write for float audio and what
	// Windows requires above two channels. NAudio refuses these on its own, and the old
	// PlaySound path played them without complaint — so a beep file the user has been hearing
	// for months would have gone quiet the day this shipped.
	private static byte[] ExtensibleWav(int sampleRate, short channels, int frames, bool ieeeFloat)
	{
		short bits = (short)(ieeeFloat ? 32 : 16);
		short blockAlign = (short)(channels * bits / 8);
		int dataBytes = frames * blockAlign;

		using var stream = new MemoryStream();
		using var writer = new BinaryWriter(stream);
		writer.Write("RIFF"u8);
		writer.Write(36 + 24 + dataBytes);
		writer.Write("WAVE"u8);
		writer.Write("fmt "u8);
		writer.Write(40);              // fmt chunk size with the extensible tail
		writer.Write(unchecked((short)0xFFFE));   // WAVE_FORMAT_EXTENSIBLE
		writer.Write(channels);
		writer.Write(sampleRate);
		writer.Write(sampleRate * blockAlign);
		writer.Write(blockAlign);
		writer.Write(bits);
		writer.Write((short)22);       // cbSize
		writer.Write(bits);            // valid bits per sample
		writer.Write(channels == 2 ? 3 : 4);  // channel mask
		// KSDATAFORMAT_SUBTYPE_PCM / _IEEE_FLOAT: the first four bytes are what tells them apart.
		writer.Write(ieeeFloat ? 3 : 1);
		writer.Write(new byte[] { 0x00, 0x00, 0x10, 0x00, 0x80, 0x00, 0x00, 0xAA, 0x00, 0x38, 0x9B, 0x71 });
		writer.Write("data"u8);
		writer.Write(dataBytes);
		for (var i = 0; i < frames * channels; i++)
		{
			if (ieeeFloat)
				writer.Write(0.25f);
			else
				writer.Write((short)4000);
		}
		writer.Flush();
		return stream.ToArray();
	}

	[Theory]
	[InlineData(true)]
	[InlineData(false)]
	public void A_wav_written_in_the_extensible_layout_still_plays(bool ieeeFloat)
	{
		var wav = ExtensibleWav(sampleRate: 44100, channels: 2, frames: 4410, ieeeFloat);

		var clip = BeepClipReader.ReadBytes(wav, Rate, Channels);

		Assert.Equal(Rate, clip.SampleRate);
		Assert.Equal(Channels, clip.Channels);
		Assert.InRange(clip.Duration.TotalSeconds, 0.08, 0.12);
		Assert.Contains(clip.Samples.ToArray(), s => s != 0f);
	}

	[Fact]
	public void Something_that_is_not_a_wav_file_is_refused_rather_than_played_as_noise()
	{
		var garbage = new byte[] { 1, 2, 3, 4, 5, 6, 7, 8 };

		Assert.ThrowsAny<Exception>(() => BeepClipReader.ReadBytes(garbage, Rate, Channels));
	}

	/// <summary>
	/// Loading every beep file has to stay quick, because <c>BeepPlayer.Initialize</c> does it on
	/// the UI thread while holding its lock — at startup and again after every settings save.
	/// Doing the work there is the whole point: it is what leaves nothing to do at the moment a
	/// beep is actually wanted. Measured at 29 ms for all six files, so the budget below is wide
	/// enough not to fail on a loaded build machine and narrow enough to catch decoding that has
	/// quietly become expensive — or moved to the wrong place.
	/// </summary>
	[Fact]
	public void Loading_every_beep_file_stays_out_of_the_way_of_the_user_interface()
	{
		var files = new[] { "Start.wav", "Success.wav", "Failure.wav", "End.wav", "Mute.wav", "Unmute.wav" };
		var paths = files.Select(f => Path.Combine(AppContext.BaseDirectory, "CustomAudio", f)).ToArray();
		Assert.All(paths, p => Assert.True(File.Exists(p), $"The fixture assumes {p} is copied next to the tests."));

		var stopwatch = System.Diagnostics.Stopwatch.StartNew();
		var clips = paths.Select(p => BeepClipReader.ReadFile(p, Rate, Channels)).ToArray();
		stopwatch.Stop();

		Assert.True(stopwatch.ElapsedMilliseconds < 1000,
			$"Loading the six beep files took {stopwatch.ElapsedMilliseconds} ms on the thread that opens the window.");
		Assert.All(clips, c => Assert.True(c.Duration > TimeSpan.Zero));
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
