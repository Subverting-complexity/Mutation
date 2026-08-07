using System;
using System.Collections.Generic;
using System.IO;
using CognitiveSupport;
using NAudio.Wave;

namespace Mutation.Tests;

/// <summary>
/// Covers what <see cref="AudioPlayer"/> does when playback ends. A file played to the end used
/// to leave its output device open and its decoded audio resident for the rest of the app's
/// life, because the only teardown was in Stop and Dispose. The output device is faked: the
/// build machine has no speakers, and the rules under test are about ownership rather than
/// sound.
/// </summary>
public class AudioPlayerTeardownTests : IDisposable
{
	private readonly string _workingDirectory = Directory.CreateTempSubdirectory("audio-player-tests").FullName;
	private readonly List<FakeAudioOutputDevice> _devices = new();

	public void Dispose()
	{
		try { Directory.Delete(_workingDirectory, recursive: true); } catch { }
	}

	// A real .wav, so the player's own reader path runs rather than being stubbed out.
	private string WriteWav(string fileName)
	{
		string path = Path.Combine(_workingDirectory, fileName);
		File.WriteAllBytes(path, BeepToneSynthesizer.SynthesizeWav(new[] { (440, 200) }));
		return path;
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

	[Fact]
	public void Reaching_the_end_of_a_file_releases_the_device()
	{
		using var player = NewPlayer();
		bool ended = false;
		player.PlaybackEnded += (_, _) => ended = true;

		player.Play(WriteWav("ends.wav"));
		FakeAudioOutputDevice device = Assert.Single(_devices);
		Assert.True(device.IsPlaying);

		device.RaisePlaybackStopped();

		Assert.True(ended);
		Assert.Equal(1, device.DisposeCount);
		Assert.False(player.IsPlaying);
	}

	[Fact]
	public void A_failed_playback_reports_the_error_and_still_releases_the_device()
	{
		using var player = NewPlayer();
		string? failure = null;
		player.PlaybackFailed += (_, message) => failure = message;

		player.Play(WriteWav("fails.wav"));
		FakeAudioOutputDevice device = Assert.Single(_devices);

		device.RaisePlaybackStopped(new InvalidOperationException("device lost"));

		Assert.NotNull(failure);
		Assert.Contains("device lost", failure);
		Assert.Equal(1, device.DisposeCount);
	}

	// A PlaybackEnded listener that queues up the next file is the normal "play the whole
	// list" pattern. The finishing device must not tear down the one that just started.
	[Fact]
	public void Starting_the_next_file_from_the_ended_handler_leaves_the_new_device_alone()
	{
		using var player = NewPlayer();
		string next = WriteWav("next.wav");

		player.PlaybackEnded += (_, _) =>
		{
			if (_devices.Count == 1)
				player.Play(next);
		};

		player.Play(WriteWav("first.wav"));
		FakeAudioOutputDevice first = _devices[0];

		first.RaisePlaybackStopped();

		Assert.Equal(2, _devices.Count);
		Assert.Equal(1, first.DisposeCount);

		FakeAudioOutputDevice second = _devices[1];
		Assert.Equal(0, second.DisposeCount);
		Assert.True(second.IsPlaying);
		Assert.True(player.IsPlaying);
	}

	// Two overlapping Play calls must not strand the loser's device: the file is decoded
	// outside the playback lock now, so the second call can arrive while the first is
	// still preparing.
	[Fact]
	public void Playing_a_second_file_releases_the_first_files_device()
	{
		using var player = NewPlayer();

		player.Play(WriteWav("one.wav"));
		player.Play(WriteWav("two.wav"));

		Assert.Equal(2, _devices.Count);
		Assert.Equal(1, _devices[0].DisposeCount);
		Assert.Equal(0, _devices[1].DisposeCount);
	}

	[Fact]
	public void A_stopped_notification_from_a_superseded_device_is_ignored()
	{
		using var player = NewPlayer();
		int endedCount = 0;
		player.PlaybackEnded += (_, _) => endedCount++;

		player.Play(WriteWav("stale.wav"));
		FakeAudioOutputDevice stale = _devices[0];
		player.Play(WriteWav("current.wav"));
		FakeAudioOutputDevice current = _devices[1];

		// The old device's PlaybackStopped, delivered late.
		stale.RaisePlaybackStopped();

		Assert.Equal(0, endedCount);
		Assert.Equal(0, current.DisposeCount);
		Assert.True(player.IsPlaying);
	}

	[Fact]
	public void Dispose_releases_the_device()
	{
		var player = NewPlayer();
		player.Play(WriteWav("disposed.wav"));

		player.Dispose();

		Assert.Equal(1, _devices[0].DisposeCount);
		Assert.False(player.IsPlaying);
	}
}
