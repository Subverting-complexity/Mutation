using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using CognitiveSupport;
using NAudio.Wave;

namespace Mutation.Tests;

/// <summary>
/// Covers <see cref="AudioPlayer.Speed"/> in situ — the time-stretch stage wired into the
/// player's real playback chain, rather than <see cref="SoundTouchSampleProvider"/> on its
/// own, which is where the existing coverage stopped (issue #255).
/// <para>
/// The provider being correct says nothing about the player building the chain around it.
/// A speed that is snapped but never handed to the provider, or a provider rebuilt at the
/// default on the next file, leaves the setting looking applied and sounding unchanged —
/// and the only feedback is how fast the recording sounds.
/// </para>
/// <para>
/// The output device is faked: build machines have no speakers, and pulling samples through
/// the chain the player handed the device measures the stretch exactly.
/// </para>
/// </summary>
public class AudioPlayerSpeedTests : IDisposable
{
	private readonly string _workingDirectory = Directory.CreateTempSubdirectory("audio-player-speed-tests").FullName;
	private readonly List<FakeAudioOutputDevice> _devices = new();

	public void Dispose()
	{
		try { Directory.Delete(_workingDirectory, recursive: true); } catch { }
	}

	private AudioPlayer NewPlayer()
	{
		return new AudioPlayer(() =>
		{
			var device = new FakeAudioOutputDevice();
			_devices.Add(device);
			return device;
		});
	}

	// A real .wav, so the player's own reader and sample-provider chain run rather than
	// being stubbed out. One second is long enough for the stretch to dominate SoundTouch's
	// own buffering latency.
	private string WriteTone(string fileName, int durationMs = 1_000)
	{
		string path = Path.Combine(_workingDirectory, fileName);
		File.WriteAllBytes(path, BeepToneSynthesizer.SynthesizeWav(new[] { (440, durationMs) }));
		return path;
	}

	/// <summary>Bytes the chain produces when played to its end — the audible length of the file.</summary>
	private static long DrainByteCount(IWaveProvider provider)
	{
		var buffer = new byte[16_384];
		long total = 0;
		int read;
		while ((read = provider.Read(buffer, 0, buffer.Length)) > 0)
			total += read;
		return total;
	}

	private async Task<long> PlayAndMeasureAsync(double speed)
	{
		using var player = NewPlayer();
		player.Speed = speed;
		await player.PlayAsync(WriteTone($"tone-{speed}.wav"));

		var device = Assert.Single(_devices);
		_devices.Clear();
		return DrainByteCount(Assert.IsAssignableFrom<IWaveProvider>(device.Provider));
	}

	// The measured ratios are exact — the source is a synthesized tone and SoundTouch.Net is
	// pinned — so the tolerance is a guard against the library moving, not against the machine.
	// It has to stay tighter than the gap to the neighbouring supported speeds, or a
	// regression that snapped 2.0 to 1.75 or 2.25 would pass: those give 0.571 and 0.444.
	private const double RatioTolerance = 0.05;

	private static void AssertRatio(long measured, long baseline, double expectedRatio)
	{
		Assert.True(baseline > 0, "The normal-speed chain produced no audio at all.");
		Assert.InRange(
			measured / (double)baseline,
			expectedRatio - RatioTolerance,
			expectedRatio + RatioTolerance);
	}

	[Fact]
	public async Task Playing_at_double_speed_produces_about_half_as_much_audio()
	{
		long atNormal = await PlayAndMeasureAsync(1.0);
		long atDouble = await PlayAndMeasureAsync(2.0);

		AssertRatio(atDouble, atNormal, 0.5);
	}

	[Fact]
	public async Task Playing_at_half_speed_produces_about_twice_as_much_audio()
	{
		long atNormal = await PlayAndMeasureAsync(1.0);
		long atHalf = await PlayAndMeasureAsync(0.5);

		AssertRatio(atHalf, atNormal, 2.0);
	}

	[Fact]
	public async Task A_speed_changed_mid_playback_reaches_audio_that_is_already_in_flight()
	{
		using var player = NewPlayer();
		await player.PlayAsync(WriteTone("mid-playback.wav"));
		var provider = Assert.IsAssignableFrom<IWaveProvider>(Assert.Single(_devices).Provider);

		// Pull a little at normal speed, the way the device would, then change gear.
		var buffer = new byte[16_384];
		long consumed = provider.Read(buffer, 0, buffer.Length);
		Assert.True(consumed > 0);

		player.Speed = 2.0;
		long total = consumed + DrainByteCount(provider);

		_devices.Clear();
		long straightThrough = await PlayAndMeasureAsync(1.0);

		// The rest of the file arrives faster, so the whole play is shorter than an unchanged
		// one. This says the change reached a chain that was already running — it does NOT say
		// the read position survived it; the byte counts cannot tell a continue from a restart
		// at the new speed, which is what the next test is for.
		Assert.True(
			total < straightThrough,
			$"Expected the sped-up play ({total}) to be shorter than a normal-speed play ({straightThrough}).");
	}

	[Fact]
	public async Task A_speed_change_does_not_rewind_the_file()
	{
		using var player = NewPlayer();
		await player.PlayAsync(WriteTone("no-rewind.wav"));
		var provider = Assert.IsAssignableFrom<IWaveProvider>(Assert.Single(_devices).Provider);

		Assert.True(DrainByteCount(provider) > 0);

		player.Speed = 2.0;

		// The file has already been played to its end. If changing speed rebuilt or rewound the
		// chain, there would be a second copy of the recording waiting here — the user would
		// hear it start again from the top on every speed change.
		Assert.Equal(0, DrainByteCount(provider));
	}

	[Theory]
	[InlineData(1.87, 1.75)]
	[InlineData(-4.0, 0.5)]
	[InlineData(99.0, 3.0)]
	public void An_unsupported_speed_is_snapped_to_the_nearest_supported_one(double requested, double expected)
	{
		using var player = NewPlayer();

		player.Speed = requested;

		Assert.Equal(expected, player.Speed);
	}

	[Fact]
	public async Task The_speed_carries_over_to_the_next_file_played()
	{
		using var player = NewPlayer();
		player.Speed = 2.0;

		await player.PlayAsync(WriteTone("first.wav"));
		player.Stop();
		await player.PlayAsync(WriteTone("second.wav"));

		Assert.Equal(2.0, player.Speed);
		Assert.Equal(2, _devices.Count);

		// The second chain has to be built at the remembered speed, not rebuilt at the
		// default — the setting is persisted and the user expects it to survive navigating
		// between recordings.
		long second = DrainByteCount(Assert.IsAssignableFrom<IWaveProvider>(_devices[1].Provider));
		_devices.Clear();
		long normal = await PlayAndMeasureAsync(1.0);

		AssertRatio(second, normal, 0.5);
	}

	[Fact]
	public async Task A_speed_set_after_playback_ends_still_applies_to_the_next_file()
	{
		using var player = NewPlayer();
		await player.PlayAsync(WriteTone("ended.wav"));
		Assert.Single(_devices).RaisePlaybackStopped();

		// The chain has been torn down by the end of the file, so there is no provider to
		// retune. Setting the speed here must not throw, and must still be waiting when the
		// next recording is played.
		player.Speed = 0.5;
		Assert.Equal(0.5, player.Speed);

		_devices.Clear();
		await player.PlayAsync(WriteTone("next.wav"));
		long atHalf = DrainByteCount(Assert.IsAssignableFrom<IWaveProvider>(Assert.Single(_devices).Provider));

		_devices.Clear();
		long normal = await PlayAndMeasureAsync(1.0);

		AssertRatio(atHalf, normal, 2.0);
	}
}
