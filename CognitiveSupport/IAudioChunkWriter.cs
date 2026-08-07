using System.Collections.Generic;
using System.Threading;

namespace CognitiveSupport;

/// <summary>
/// Splits an audio file into standalone files that each fit inside an upload limit.
///
/// Separated from <see cref="ChunkedTranscriber"/> so the "split, then upload in order"
/// flow can be tested without a codec, and so a different container could be slotted in
/// without touching that flow. Boundary selection itself lives in
/// <see cref="AudioChunkPlanner"/>, which an implementation is expected to use.
/// </summary>
public interface IAudioChunkWriter
{
	/// <summary>
	/// Writes <paramref name="audioFilePath"/> out as a sequence of playable files inside
	/// <paramref name="outputDirectory"/>, returned in playback order.
	///
	/// <paramref name="maxChunkBytes"/> is a hard ceiling enforced while writing, not just
	/// a planning input: a chunk growing towards it is closed early and the rest continues
	/// in the next file. <paramref name="removalPoints"/> says where silence was stripped
	/// out of this file, so a cut can land at a pause rather than mid-word.
	/// </summary>
	IReadOnlyList<string> WriteChunks(
		string audioFilePath,
		IReadOnlyList<SilenceRemovalPoint>? removalPoints,
		long maxChunkBytes,
		string outputDirectory,
		CancellationToken cancellationToken);
}
