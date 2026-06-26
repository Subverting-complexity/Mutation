namespace CognitiveSupport;

// Pure threshold-selection for the gapless progress weave. Given where a read
// begins and which thresholds have already been announced, decides which
// progress markers to bake into the upcoming utterance and at which sentence
// boundary each one lands. Side-effect free and static so the selection rules
// are unit-testable without a synthesizer.
public static class ProgressWeavePlanner
{
	// A single progress marker to weave into the spoken text.
	public readonly record struct WeavePoint(int RealOffset, int Percent);

	// Plan the markers for a read that starts at real offset `fromReal`.
	//
	// For every threshold above `lastAnnouncedPercent` (and strictly below 100),
	// the threshold's character offset is snapped forward to the next sentence
	// boundary that is also past `fromReal`. A threshold whose snapped point is
	// past the last boundary (it falls in the final sentence, which has no
	// following boundary) is dropped — completion speaks for itself there.
	//
	// Several thresholds can snap to the same boundary (long sentences, or a
	// forward skip that vaults past lower thresholds). Those collapse to the
	// single highest threshold at that boundary, so the listener hears "50%, ..."
	// once rather than a stale "25%, ..." immediately followed by "50%, ...".
	//
	// Returns the weave points in ascending boundary order. Empty when announcing
	// is gated off (`stepPercent <= 0`) or no threshold qualifies.
	public static IReadOnlyList<WeavePoint> Plan(
		int length, IReadOnlyList<int> sentenceStarts, int fromReal, int lastAnnouncedPercent, int stepPercent)
	{
		var points = new List<WeavePoint>();
		if (stepPercent <= 0 || length <= 0 || sentenceStarts is null || sentenceStarts.Count == 0)
			return points;

		int maxThreshold = ReadingProgress.MaxThreshold(stepPercent);

		// Highest threshold already chosen for a given boundary, so later (higher)
		// thresholds at the same boundary overwrite earlier ones.
		for (int threshold = stepPercent; threshold <= maxThreshold; threshold += stepPercent)
		{
			if (threshold <= lastAnnouncedPercent) continue;

			// First real offset whose floor-percentage reaches this threshold.
			int thresholdOffset = (int)Math.Ceiling(threshold * (double)length / 100d);

			// Snap forward to a sentence boundary that is also strictly past the
			// start position, so a marker never lands at or behind the current read.
			int wantFrom = Math.Max(thresholdOffset, fromReal + 1);
			int boundary = SentenceBoundaries.FirstStartAtOrAfter(sentenceStarts, wantFrom);
			if (boundary < 0) continue; // threshold falls inside the final sentence

			if (points.Count > 0 && points[^1].RealOffset == boundary)
				points[^1] = new WeavePoint(boundary, threshold); // collapse: keep the highest
			else
				points.Add(new WeavePoint(boundary, threshold));
		}

		return points;
	}
}
