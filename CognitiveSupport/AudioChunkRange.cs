using System;

namespace CognitiveSupport;

/// <summary>
/// A half-open slice of an audio timeline, <c>[Start, End)</c>, to be written out as
/// one upload-sized chunk. Ranges produced by <see cref="AudioChunkPlanner"/> are
/// contiguous and in playback order, so concatenating them reproduces the original.
/// </summary>
public readonly record struct AudioChunkRange(TimeSpan Start, TimeSpan End)
{
	public TimeSpan Duration => End - Start;
}
