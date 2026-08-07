using System;
using System.Collections.Generic;
using System.Linq;
using CognitiveSupport;

namespace Mutation.Tests;

/// <summary>
/// Boundary selection for splitting an oversized recording. A round 1000 bytes a second
/// is used throughout so a byte limit reads directly as a duration: 10,000 bytes is ten
/// seconds of audio.
/// </summary>
public class AudioChunkPlannerTests
{
	private const double BytesPerSecond = 1000;

	private static SilenceRemovalPoint At(double positionSeconds, double removedSeconds) =>
		new(TimeSpan.FromSeconds(positionSeconds), TimeSpan.FromSeconds(removedSeconds));

	private static IReadOnlyList<AudioChunkRange> Plan(
		double totalSeconds,
		double limitSeconds,
		params SilenceRemovalPoint[] points) =>
		AudioChunkPlanner.Plan(
			TimeSpan.FromSeconds(totalSeconds),
			(long)(limitSeconds * BytesPerSecond),
			BytesPerSecond,
			points);

	private static double[] Ends(IReadOnlyList<AudioChunkRange> chunks) =>
		chunks.Select(c => Math.Round(c.End.TotalSeconds, 6)).ToArray();

	[Fact]
	public void AudioInsideTheLimit_IsOneChunk()
	{
		var chunks = Plan(totalSeconds: 30, limitSeconds: 60);

		var chunk = Assert.Single(chunks);
		Assert.Equal(TimeSpan.Zero, chunk.Start);
		Assert.Equal(TimeSpan.FromSeconds(30), chunk.End);
	}

	[Fact]
	public void ZeroLengthAudio_PlansNothing()
	{
		Assert.Empty(AudioChunkPlanner.Plan(TimeSpan.Zero, 1000, BytesPerSecond, Array.Empty<SilenceRemovalPoint>()));
	}

	[Fact]
	public void NoLimit_IsOneChunk()
	{
		var chunks = AudioChunkPlanner.Plan(TimeSpan.FromHours(3), maxChunkBytes: 0, BytesPerSecond, null);

		Assert.Equal(TimeSpan.FromHours(3), Assert.Single(chunks).End);
	}

	[Fact]
	public void WithoutRemovalPoints_CutsAtTheFurthestSafePosition()
	{
		var chunks = Plan(totalSeconds: 25, limitSeconds: 10);

		Assert.Equal(new[] { 10d, 20d, 25d }, Ends(chunks));
	}

	[Fact]
	public void RemovalPointInTheFinalFifth_BecomesTheCut()
	{
		// The window for a 10s candidate chunk is (8, 10).
		var chunks = Plan(totalSeconds: 15, limitSeconds: 10, At(8.5, removedSeconds: 3));

		Assert.Equal(new[] { 8.5, 15d }, Ends(chunks));
	}

	[Fact]
	public void RemovalPointBeforeTheFinalFifth_IsIgnored()
	{
		var chunks = Plan(totalSeconds: 15, limitSeconds: 10, At(4, removedSeconds: 30));

		Assert.Equal(new[] { 10d, 15d }, Ends(chunks));
	}

	[Fact]
	public void LongestRemovedSilenceWins_NotTheNearestOrTheLatest()
	{
		var chunks = Plan(
			totalSeconds: 15,
			limitSeconds: 10,
			At(8.2, removedSeconds: 1),
			At(8.7, removedSeconds: 9),   // the longest pause in the window
			At(9.6, removedSeconds: 2));

		Assert.Equal(8.7, chunks[0].End.TotalSeconds, 6);
	}

	[Fact]
	public void EquallyLongSilences_CutAtTheLaterOne()
	{
		// Same-sized pause either way, so take the one that carries more audio per chunk.
		var chunks = Plan(
			totalSeconds: 15,
			limitSeconds: 10,
			At(8.3, removedSeconds: 4),
			At(9.4, removedSeconds: 4));

		Assert.Equal(9.4, chunks[0].End.TotalSeconds, 6);
	}

	[Fact]
	public void ZeroLengthRemoval_IsNotACandidate()
	{
		var chunks = Plan(totalSeconds: 15, limitSeconds: 10, At(8.5, removedSeconds: 0));

		Assert.Equal(10d, chunks[0].End.TotalSeconds, 6);
	}

	[Fact]
	public void EachChunkGetsItsOwnSearchWindow_MeasuredFromWhereItStarted()
	{
		// First cut lands early at 8.5, so the second candidate runs 8.5 -> 18.5 and its
		// window is (16.5, 18.5). The point at 15.9 is inside the second *chunk* but
		// before its window, so it must not be chosen.
		var chunks = Plan(
			totalSeconds: 30,
			limitSeconds: 10,
			At(8.5, removedSeconds: 5),
			At(15.9, removedSeconds: 20),
			At(17.2, removedSeconds: 2));

		Assert.Equal(new[] { 8.5, 17.2, 27.2, 30d }, Ends(chunks));
	}

	[Fact]
	public void ChunksAreContiguousAndCoverTheWholeRecording()
	{
		var points = new[] { At(9.1, 4), At(18.4, 6), At(26.9, 2), At(35.5, 8) };
		var chunks = AudioChunkPlanner.Plan(TimeSpan.FromSeconds(47), 10_000, BytesPerSecond, points);

		Assert.True(chunks.Count > 1, "A 47s recording at a 10s limit has to be split.");
		Assert.Equal(TimeSpan.Zero, chunks[0].Start);
		Assert.Equal(TimeSpan.FromSeconds(47), chunks[^1].End);

		for (int i = 1; i < chunks.Count; i++)
			Assert.Equal(chunks[i - 1].End, chunks[i].Start);

		foreach (var chunk in chunks)
			Assert.True(chunk.Duration > TimeSpan.Zero, "A planned chunk must carry audio.");
	}

	[Fact]
	public void NoChunkExceedsTheLimit()
	{
		var points = new[] { At(9.5, 3), At(18.9, 7), At(27.5, 1) };
		var chunks = AudioChunkPlanner.Plan(TimeSpan.FromSeconds(40), 10_000, BytesPerSecond, points);

		foreach (var chunk in chunks)
			Assert.True(chunk.Duration <= TimeSpan.FromSeconds(10),
				$"Chunk {chunk.Start}-{chunk.End} is {chunk.Duration}, over the 10s the limit allows.");
	}

	[Fact]
	public void AScrapOfAudioLeftOver_IsFoldedIntoTheChunkBeforeIt()
	{
		// 20.1s at a 10s limit would otherwise end with a 0.1s chunk: a whole request
		// for a tenth of a second.
		var chunks = Plan(totalSeconds: 20.1, limitSeconds: 10);

		Assert.Equal(new[] { 10d, 20.1 }, Ends(chunks));
	}

	[Fact]
	public void AnAbsurdlySmallLimit_StillTerminates()
	{
		// One byte a chunk cannot be honoured; the floor keeps this from planning
		// millions of ranges rather than pretending the limit is achievable.
		var chunks = AudioChunkPlanner.Plan(TimeSpan.FromSeconds(30), maxChunkBytes: 1, BytesPerSecond, null);

		Assert.Equal(30, chunks.Count);
		Assert.Equal(TimeSpan.FromSeconds(30), chunks[^1].End);
	}
}
