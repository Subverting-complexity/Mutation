using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using CognitiveSupport;
using Concentus.Enums;
using Concentus.Oggfile;
using Concentus.Structs;

namespace Mutation.Tests;

/// <summary>
/// Splitting a real Ogg/Opus recording into upload-sized files. Both ends of the round
/// trip are pure Concentus, so nothing here needs codec support from the host OS.
/// </summary>
public class OggAudioChunkWriterTests : IDisposable
{
	private const int SampleRate = 48000;
	private const int FrameSize = 960; // 20ms
	private const int FixtureSeconds = 6;

	// Matches the writer's own planning estimate (24 kbps plus container overhead), so a
	// limit here can be expressed as "about this many seconds of audio".
	private const double PlanningBytesPerSecond = 24000 / 8.0 * 1.15;

	private readonly string _workingDirectory = Directory.CreateTempSubdirectory("ogg-chunk-tests").FullName;

	public void Dispose()
	{
		try { Directory.Delete(_workingDirectory, recursive: true); } catch { }
	}

	private static long LimitForSeconds(double seconds) => (long)(seconds * PlanningBytesPerSecond);

	private string OutputDirectory(string name)
	{
		string path = Path.Combine(_workingDirectory, name);
		Directory.CreateDirectory(path);
		return path;
	}

	/// <summary>Six seconds of 440Hz tone as a single Ogg/Opus file.</summary>
	private string WriteFixture(string fileName)
	{
		string path = Path.Combine(_workingDirectory, fileName);

		using var outStream = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None);
#pragma warning disable CS0618 // Concentus.OggFile 1.0.6 requires the concrete OpusEncoder type.
		var encoder = new OpusEncoder(SampleRate, 1, OpusApplication.OPUS_APPLICATION_AUDIO)
		{
			Bitrate = 32000,
			UseDTX = false
		};
#pragma warning restore CS0618
		var oggStream = new OpusOggWriteStream(encoder, outStream, new OpusTags());

		var frame = new short[FrameSize];
		for (int sample = 0; sample < FrameSize; sample++)
			frame[sample] = (short)(12000 * Math.Sin(2 * Math.PI * 440 * sample / SampleRate));

		for (int i = 0; i < FixtureSeconds * SampleRate / FrameSize; i++)
			oggStream.WriteSamples(frame, 0, frame.Length);

		oggStream.Finish();
		return path;
	}

	private static double DurationSeconds(string oggPath) =>
		OggAudioChunkWriter.MeasureDuration(oggPath).TotalSeconds;

	private IReadOnlyList<string> Split(string source, long maxChunkBytes, string outputName, params SilenceRemovalPoint[] points) =>
		new OggAudioChunkWriter().WriteChunks(source, points, maxChunkBytes, OutputDirectory(outputName), CancellationToken.None);

	[Fact]
	public void MeasureDuration_ReadsTheLengthOfTheRecording()
	{
		Assert.Equal(FixtureSeconds, DurationSeconds(WriteFixture("measure.ogg")), 1);
	}

	[Fact]
	public void AudioInsideTheLimit_IsWrittenAsOneChunk()
	{
		string source = WriteFixture("small.ogg");

		var chunks = Split(source, LimitForSeconds(60), "small-out");

		string only = Assert.Single(chunks);
		Assert.Equal(FixtureSeconds, DurationSeconds(only), 1);
	}

	[Fact]
	public void OversizedAudio_IsSplitAtThePlannedBoundaries()
	{
		string source = WriteFixture("split.ogg");

		var chunks = Split(source, LimitForSeconds(3), "split-out");

		Assert.Equal(2, chunks.Count);
		Assert.Equal(3, DurationSeconds(chunks[0]), 1);
		Assert.Equal(3, DurationSeconds(chunks[1]), 1);
	}

	[Fact]
	public void ARemovedSilenceInTheFinalFifth_MovesTheCutBackToThePause()
	{
		string source = WriteFixture("silence-cut.ogg");

		// The candidate chunk ends at ~4s, so its search window is ~3.2s to 4s.
		var chunks = Split(
			source,
			LimitForSeconds(4),
			"silence-cut-out",
			new SilenceRemovalPoint(TimeSpan.FromSeconds(3.5), TimeSpan.FromSeconds(4)));

		Assert.Equal(2, chunks.Count);
		Assert.Equal(3.5, DurationSeconds(chunks[0]), 1);
		Assert.Equal(2.5, DurationSeconds(chunks[1]), 1);
	}

	[Fact]
	public void EveryChunkIsAPlayableOggOpusFile()
	{
		string source = WriteFixture("playable.ogg");

		var chunks = Split(source, LimitForSeconds(2), "playable-out");

		Assert.True(chunks.Count > 1, "A 6s recording at a 2s limit has to be split.");
		foreach (string chunk in chunks)
		{
			Assert.True(OggOpusPcmDecoder.IsOggOpus(chunk), $"{Path.GetFileName(chunk)} is not a readable Ogg/Opus file.");
			Assert.True(DurationSeconds(chunk) > 0, $"{Path.GetFileName(chunk)} decoded to no audio.");
		}
	}

	[Theory]
	[InlineData(1.0)]
	[InlineData(2.5)]
	[InlineData(4.0)]
	public void NoChunkEverExceedsTheLimit(double limitSeconds)
	{
		string source = WriteFixture($"limit-{limitSeconds}.ogg");
		long limit = LimitForSeconds(limitSeconds);

		var chunks = Split(source, limit, $"limit-{limitSeconds}-out");

		foreach (string chunk in chunks)
		{
			long length = new FileInfo(chunk).Length;
			Assert.True(length <= limit, $"{Path.GetFileName(chunk)} is {length} bytes, over the {limit} byte limit.");
		}
	}

	[Fact]
	public void TheChunksTogetherAreTheWholeRecording()
	{
		string source = WriteFixture("whole.ogg");

		var chunks = Split(source, LimitForSeconds(2.5), "whole-out");

		// Each chunk closes on a whole Opus frame, so a few milliseconds of padding is
		// added per cut. Nothing may go missing; a little silence at a seam is fine.
		double total = chunks.Sum(DurationSeconds);
		Assert.InRange(total, FixtureSeconds, FixtureSeconds + 0.25);
	}

	[Fact]
	public void ChunksAreNamedInPlaybackOrder()
	{
		string source = WriteFixture("ordered.ogg");

		var chunks = Split(source, LimitForSeconds(2), "ordered-out");

		Assert.Equal(chunks.OrderBy(p => p, StringComparer.Ordinal).ToArray(), chunks.ToArray());
		Assert.Equal("chunk-001.ogg", Path.GetFileName(chunks[0]));
	}

	[Fact]
	public void CancellationStopsTheSplitPartWayThrough()
	{
		string source = WriteFixture("cancelled.ogg");
		using var cts = new CancellationTokenSource();
		cts.Cancel();

		Assert.ThrowsAny<OperationCanceledException>(() =>
			new OggAudioChunkWriter().WriteChunks(
				source, Array.Empty<SilenceRemovalPoint>(), LimitForSeconds(2), OutputDirectory("cancelled-out"), cts.Token));
	}
}
