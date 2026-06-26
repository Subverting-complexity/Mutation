namespace CognitiveSupport;

// Pure boundary-snapping helper for the gapless progress weave. A progress
// announcement is woven at the first sentence boundary at or after the point
// where a threshold falls, so it never lands mid-sentence. Side-effect free and
// static so the snapping rule is unit-testable without a synthesizer.
public static class SentenceBoundaries
{
	// The first sentence start at or after `offset`, or -1 when `offset` is past
	// the last sentence start (i.e. the threshold falls inside the final sentence,
	// where there is no following boundary to weave at). `starts` is the ascending
	// list of sentence-start offsets into the real text.
	public static int FirstStartAtOrAfter(IReadOnlyList<int> starts, int offset)
	{
		if (starts is null || starts.Count == 0) return -1;
		for (int i = 0; i < starts.Count; i++)
		{
			if (starts[i] >= offset) return starts[i];
		}
		return -1;
	}
}
