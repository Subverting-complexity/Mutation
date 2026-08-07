using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using CognitiveSupport;

namespace Mutation.Tests;

/// <summary>
/// Covers <see cref="AudioPlayer.PlayAsync"/>: decoding a recording moved off the calling
/// thread, because a 30-minute voice note took seconds to tens of seconds to decode and on
/// the UI thread that was a frozen window and a silent screen reader (issue #281).
/// <para>
/// Two things had to stay true through that move. The output device must still be built where
/// the caller was, since NAudio captures that thread's synchronization context to post
/// playback notifications back to. And a recording the user has already stopped must not
/// start playing when its decode finally lands.
/// </para>
/// </summary>
public class AudioPlayerAsyncPlaybackTests : IDisposable
{
	private readonly string _workingDirectory = Directory.CreateTempSubdirectory("audio-player-async-tests").FullName;
	private readonly List<FakeAudioOutputDevice> _devices = new();

	public void Dispose()
	{
		try { Directory.Delete(_workingDirectory, recursive: true); } catch { }
	}

	private string WriteWav(string fileName, int milliseconds = 200)
	{
		string path = Path.Combine(_workingDirectory, fileName);
		File.WriteAllBytes(path, BeepToneSynthesizer.SynthesizeWav(new[] { (440, milliseconds) }));
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
	public async Task PlayAsync_starts_the_file()
	{
		using var player = NewPlayer();

		await player.PlayAsync(WriteWav("plays.wav"));

		FakeAudioOutputDevice device = Assert.Single(_devices);
		Assert.True(device.IsPlaying);
		Assert.True(player.IsPlaying);
	}

	// The whole reason only the decode moved to the thread pool. Built without a context, the
	// real device raises PlaybackEnded on NAudio's playback thread, and every listener of that
	// event touches the UI.
	[Fact]
	public async Task PlayAsync_opens_the_device_back_on_the_callers_context()
	{
		var context = new InlineSynchronizationContext();
		SynchronizationContext? original = SynchronizationContext.Current;
		SynchronizationContext.SetSynchronizationContext(context);
		try
		{
			using var player = NewPlayer();

			await player.PlayAsync(WriteWav("context.wav"));

			Assert.Same(context, Assert.Single(_devices).CreatedOnContext);
		}
		finally
		{
			SynchronizationContext.SetSynchronizationContext(original);
		}
	}

	// Pressing Play and then Stop while a long recording is still decoding used to be
	// impossible, because the window was frozen for the whole decode. Now that it is not, the
	// stop has to win: without it the audio starts anyway, seconds after the user stopped it,
	// with the button already reading "Play".
	[Fact]
	public async Task Stopping_while_a_file_is_still_decoding_keeps_it_silent()
	{
		using var player = NewPlayer();

		Task playback = player.PlayAsync(WriteWav("stopped-mid-decode.wav", milliseconds: 3000));
		player.Stop();
		await playback;

		Assert.False(player.IsPlaying);
	}

	// Superseding is per-request, not a latch: the next Play after a Stop still plays.
	[Fact]
	public async Task Playing_again_after_a_stop_still_starts()
	{
		using var player = NewPlayer();

		await player.PlayAsync(WriteWav("first.wav"));
		player.Stop();
		await player.PlayAsync(WriteWav("second.wav"));

		Assert.True(player.IsPlaying);
		Assert.Equal(2, _devices.Count);
		Assert.True(_devices[1].IsPlaying);
	}

	[Fact]
	public async Task PlayAsync_reports_a_missing_file_rather_than_throwing()
	{
		using var player = NewPlayer();
		string? failure = null;
		player.PlaybackFailed += (_, message) => failure = message;

		await player.PlayAsync(Path.Combine(_workingDirectory, "not-there.wav"));

		Assert.NotNull(failure);
		Assert.Empty(_devices);
	}

	// Two overlapping requests: the newer one owns the player, and the older one throws its
	// decoded audio away instead of stealing the device back.
	[Fact]
	public async Task The_last_file_asked_for_is_the_one_that_plays()
	{
		using var player = NewPlayer();

		Task first = player.PlayAsync(WriteWav("older.wav", milliseconds: 3000));
		Task second = player.PlayAsync(WriteWav("newer.wav"));
		await Task.WhenAll(first, second);

		Assert.True(player.IsPlaying);
		FakeAudioOutputDevice last = _devices[^1];
		Assert.True(last.IsPlaying);
		Assert.Equal(0, last.DisposeCount);
	}

	/// <summary>
	/// Stands in for a UI thread's context: it runs the continuation immediately, but with
	/// itself installed as <c>SynchronizationContext.Current</c>, which is what lets a test
	/// see where the continuation actually resumed.
	/// </summary>
	private sealed class InlineSynchronizationContext : SynchronizationContext
	{
		public override void Post(SendOrPostCallback d, object? state)
		{
			SynchronizationContext? previous = Current;
			SetSynchronizationContext(this);
			try
			{
				d(state);
			}
			finally
			{
				SetSynchronizationContext(previous);
			}
		}
	}
}
