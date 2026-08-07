using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CognitiveSupport;
using Concentus.Enums;
using Concentus.Oggfile;
using Concentus.Structs;

namespace Mutation.Tests;

/// <summary>
/// Covers <see cref="AudioPlayer.PlayAsync"/>: decoding a recording moved off the calling
/// thread, because a 30-minute voice note took seconds to tens of seconds to decode and on
/// the UI thread that was a frozen window and a silent screen reader (issue #281).
/// <para>
/// Two things had to stay true through that move. The output device must still be built where
/// the caller was, since NAudio captures that thread's synchronization context to post
/// playback notifications back to. And a request that has been superseded — the user pressed
/// Stop, or picked another file — must do nothing at all when it finally lands, whether it
/// succeeded or failed.
/// </para>
/// <para>
/// The superseded cases are driven through the player's <c>beforePrepare</c> seam rather than
/// by racing the thread pool: a test that only wins by scheduling luck is a test that passes
/// when the fix is reverted.
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

	private string WriteWav(string fileName)
	{
		string path = Path.Combine(_workingDirectory, fileName);
		File.WriteAllBytes(path, BeepToneSynthesizer.SynthesizeWav(new[] { (440, 200) }));
		return path;
	}

	/// <summary>
	/// A real Ogg/Opus file carrying its two header pages and no audio — what a recording
	/// interrupted before it captured anything leaves on disk. It decodes to zero samples,
	/// which is the only way to reach the "no audio data" branch that decides what a
	/// superseded empty decode is allowed to say.
	/// </summary>
	private string WriteHeadersOnlyOgg(string fileName)
	{
		using var complete = new MemoryStream();
#pragma warning disable CS0618 // Concentus.OggFile 1.0.6 requires the concrete OpusEncoder type.
		var encoder = new OpusEncoder(48000, 1, OpusApplication.OPUS_APPLICATION_AUDIO);
#pragma warning restore CS0618
		// Finish pads the part-filled buffer out and encodes it, so even a stream nothing was
		// written to ends up with one frame of silence. The audio starts at the third page.
		new OpusOggWriteStream(encoder, complete, new OpusTags()).Finish();

		byte[] bytes = complete.ToArray();
		int audioPage = PageOffsets(bytes).Skip(2).First();

		string path = Path.Combine(_workingDirectory, fileName);
		File.WriteAllBytes(path, bytes[..audioPage]);
		return path;
	}

	/// <summary>Where each Ogg page starts: every "OggS" capture pattern in the file.</summary>
	private static IEnumerable<int> PageOffsets(byte[] bytes)
	{
		for (int index = 0; index + 3 < bytes.Length; index++)
			if (bytes[index] == (byte)'O' && bytes[index + 1] == (byte)'g'
				&& bytes[index + 2] == (byte)'g' && bytes[index + 3] == (byte)'S')
				yield return index;
	}

	private AudioPlayer NewPlayer(Action<string>? beforePrepare = null)
	{
		return new AudioPlayer(
			() =>
			{
				var device = new FakeAudioOutputDevice();
				_devices.Add(device);
				return device;
			},
			beforePrepare);
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
	// stop has to win: without this the audio starts anyway, seconds after the user stopped
	// it, with the button already reading "Play".
	[Fact]
	public async Task Stopping_while_a_file_is_still_decoding_keeps_it_silent()
	{
		using var gate = new DecodeGate();
		using var player = NewPlayer(gate.Wait);

		Task playback = player.PlayAsync(WriteWav("stopped-mid-decode.wav"));
		gate.WaitUntilDecodeStarted();

		player.Stop();
		gate.Release();
		await playback;

		Assert.Empty(_devices);
		Assert.False(player.IsPlaying);
	}

	// A decode that fails after it has been superseded belongs to nobody. Reporting it stops
	// whatever is playing now and puts a modal error dialog in front of the user about a file
	// they moved on from seconds ago.
	[Fact]
	public async Task A_stale_decode_that_fails_leaves_the_current_recording_playing()
	{
		using var gate = new DecodeGate();
		using var player = NewPlayer(gate.Wait);
		string? failure = null;
		player.PlaybackFailed += (_, message) => failure = message;

		// The stale request: held at the gate, and rigged to throw when it is let go.
		Task stale = player.PlayAsync(Path.Combine(_workingDirectory, "deleted-mid-decode.wav"));
		gate.WaitUntilDecodeStarted();

		// The user moves on. This one is not gated, so it decodes and starts normally.
		gate.LetTheNextOneThrough();
		await player.PlayAsync(WriteWav("current.wav"));
		FakeAudioOutputDevice current = Assert.Single(_devices);

		gate.Release();
		await stale;

		Assert.True(current.IsPlaying);
		Assert.Equal(0, current.DisposeCount);
		Assert.True(player.IsPlaying);
		Assert.Null(failure);
	}

	// A file that decodes to nothing is reported, not started.
	[Fact]
	public async Task PlayAsync_reports_a_file_that_holds_no_audio()
	{
		using var player = NewPlayer();
		string? failure = null;
		player.PlaybackFailed += (_, message) => failure = message;

		await player.PlayAsync(WriteHeadersOnlyOgg("empty.ogg"));

		Assert.Equal("No audio data found in file.", failure);
		Assert.Empty(_devices);
	}

	// ...and once superseded it is not reported either, for the same reason a failing stale
	// decode is not: the message would stop the recording that is playing now.
	[Fact]
	public async Task A_stale_decode_that_holds_no_audio_says_nothing()
	{
		using var gate = new DecodeGate();
		using var player = NewPlayer(gate.Wait);
		string? failure = null;
		player.PlaybackFailed += (_, message) => failure = message;

		Task stale = player.PlayAsync(WriteHeadersOnlyOgg("stale-empty.ogg"));
		gate.WaitUntilDecodeStarted();

		gate.LetTheNextOneThrough();
		await player.PlayAsync(WriteWav("current.wav"));
		FakeAudioOutputDevice current = Assert.Single(_devices);

		gate.Release();
		await stale;

		Assert.Null(failure);
		Assert.True(current.IsPlaying);
		Assert.Equal(0, current.DisposeCount);
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

	// Two overlapping requests. Exactly one device is ever opened — the older request throws
	// its decoded audio away rather than opening a second one and stealing the player back.
	[Fact]
	public async Task The_last_file_asked_for_is_the_one_that_plays()
	{
		using var gate = new DecodeGate();
		using var player = NewPlayer(gate.Wait);

		Task older = player.PlayAsync(WriteWav("older.wav"));
		gate.WaitUntilDecodeStarted();

		gate.LetTheNextOneThrough();
		Task newer = player.PlayAsync(WriteWav("newer.wav"));
		await newer;

		gate.Release();
		await older;

		FakeAudioOutputDevice only = Assert.Single(_devices);
		Assert.True(only.IsPlaying);
		Assert.Equal(0, only.DisposeCount);
		Assert.True(player.IsPlaying);
	}

	/// <summary>
	/// Holds the first decode open until the test says otherwise, so the test can act while a
	/// request is genuinely in flight instead of hoping the thread pool is slow enough.
	/// </summary>
	private sealed class DecodeGate : IDisposable
	{
		private readonly ManualResetEventSlim _started = new(false);
		private readonly ManualResetEventSlim _released = new(false);
		private int _passThrough;

		/// <summary>Runs on the decoding thread. Blocks unless it has been waved past.</summary>
		public void Wait(string filePath)
		{
			if (Interlocked.Exchange(ref _passThrough, 0) != 0)
				return;

			_started.Set();
			_released.Wait(TimeSpan.FromSeconds(10));
		}

		public void WaitUntilDecodeStarted() => Assert.True(_started.Wait(TimeSpan.FromSeconds(10)));

		/// <summary>The next decode is not held — used for the request that supersedes.</summary>
		public void LetTheNextOneThrough() => Interlocked.Exchange(ref _passThrough, 1);

		public void Release() => _released.Set();

		public void Dispose()
		{
			_released.Set();
			_started.Dispose();
			_released.Dispose();
		}
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
