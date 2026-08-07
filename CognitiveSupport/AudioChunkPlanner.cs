using System;
using System.Collections.Generic;

namespace CognitiveSupport;

/// <summary>
/// Decides where to cut a processed recording so every chunk fits inside the upload
/// limit the transcription service imposes.
///
/// Pure arithmetic over a timeline: no files, no codec, no clock. That is deliberate —
/// boundary selection is the part worth testing exhaustively, and it can be, without
/// audio hardware or a network.
///
/// For each chunk it walks forward to the furthest end position that still encodes
/// inside the limit, then looks back over the last
/// <see cref="SilenceSearchWindowFraction"/> of that candidate for a point where the
/// trimmer removed a silence. The longest removed silence in that window wins, because
/// the longer the original pause, the more likely the cut falls between sentences
/// rather than mid-word. With no such point the cut stays at the furthest safe
/// position — a slightly awkward boundary beats an oversized upload.
/// </summary>
public static class AudioChunkPlanner
{
	/// <summary>How much of the end of a candidate chunk is searched for a silence cut.</summary>
	public const double SilenceSearchWindowFraction = 0.2;

	// Floor on a chunk's length. Only reachable with an absurdly small byte limit, and
	// only there to keep the loop below from planning millions of chunks.
	private const double MinimumChunkSeconds = 1.0;

	// A leftover tail shorter than this is folded into the chunk before it instead of
	// being sent on its own: a fraction-of-a-second upload costs a whole round trip and
	// comes back with nothing worth having. The overshoot that allows — half a second of
	// audio, under two kilobytes encoded — is well inside the headroom a writer already
	// reserves for the container.
	private const double MinimumTrailingChunkSeconds = 0.5;

	/// <param name="totalDuration">Length of the processed audio.</param>
	/// <param name="maxChunkBytes">Upload limit per chunk. Zero or less means no limit.</param>
	/// <param name="encodedBytesPerSecond">
	/// How many bytes a second of audio takes once written as a chunk. This is the
	/// encoder's own output rate, not the source file's — a chunk is always re-encoded,
	/// so what the source happens to be compressed at says nothing about the result.
	/// </param>
	/// <param name="removalPoints">
	/// Silences the trimmer removed, positioned on this same processed timeline. May be
	/// empty (an older recording, or stripping switched off), in which case every cut
	/// falls at the furthest safe position.
	/// </param>
	public static IReadOnlyList<AudioChunkRange> Plan(
		TimeSpan totalDuration,
		long maxChunkBytes,
		double encodedBytesPerSecond,
		IReadOnlyList<SilenceRemovalPoint>? removalPoints)
	{
		if (totalDuration <= TimeSpan.Zero)
			return Array.Empty<AudioChunkRange>();

		var whole = new[] { new AudioChunkRange(TimeSpan.Zero, totalDuration) };

		if (maxChunkBytes <= 0 || encodedBytesPerSecond <= 0 || double.IsNaN(encodedBytesPerSecond))
			return whole;

		double maxChunkSeconds = Math.Max(maxChunkBytes / encodedBytesPerSecond, MinimumChunkSeconds);
		double totalSeconds = totalDuration.TotalSeconds;
		if (maxChunkSeconds >= totalSeconds)
			return whole;

		var points = removalPoints ?? Array.Empty<SilenceRemovalPoint>();
		var chunks = new List<AudioChunkRange>();

		double start = 0;
		while (true)
		{
			double candidateEnd = start + maxChunkSeconds;
			if (candidateEnd >= totalSeconds - MinimumTrailingChunkSeconds)
			{
				chunks.Add(new AudioChunkRange(TimeSpan.FromSeconds(start), totalDuration));
				break;
			}

			double end = ChooseCut(points, start, candidateEnd);
			chunks.Add(new AudioChunkRange(TimeSpan.FromSeconds(start), TimeSpan.FromSeconds(end)));
			start = end;
		}

		return chunks;
	}

	/// <summary>
	/// Picks the cut for a chunk running from <paramref name="start"/> to at most
	/// <paramref name="candidateEnd"/>: the longest removed silence inside the trailing
	/// search window, else <paramref name="candidateEnd"/> itself.
	/// </summary>
	private static double ChooseCut(IReadOnlyList<SilenceRemovalPoint> points, double start, double candidateEnd)
	{
		double windowStart = candidateEnd - (candidateEnd - start) * SilenceSearchWindowFraction;

		bool found = false;
		double bestPosition = 0;
		TimeSpan bestRemoved = TimeSpan.MinValue;

		foreach (var point in points)
		{
			double position = point.Position.TotalSeconds;

			// A cut at or before the chunk's own start would not advance, and one at or
			// past the candidate end would blow the limit the candidate was chosen for.
			if (position <= start || position <= windowStart || position >= candidateEnd)
				continue;
			if (point.RemovedDuration <= TimeSpan.Zero)
				continue;

			// Ties go to the later point: same-sized pause, fewer chunks overall.
			if (found && point.RemovedDuration < bestRemoved)
				continue;
			if (found && point.RemovedDuration == bestRemoved && position <= bestPosition)
				continue;

			found = true;
			bestPosition = position;
			bestRemoved = point.RemovedDuration;
		}

		return found ? bestPosition : candidateEnd;
	}
}
