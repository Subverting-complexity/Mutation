namespace CognitiveSupport;

// Pure decision for the periodic progress announcements. Kept side-effect free and
// static so the threshold-crossing rules can be unit-tested without a synthesizer.
public static class ReadingProgress
{
	// Largest step multiple that is still strictly below 100 (e.g. 75 for a step of
	// 25, 90 for a step of 30) so progress never announces completion. 0 for a
	// non-positive step. Used by the gapless weave planner to enumerate the valid
	// progress thresholds. 100% is deliberately never one — end-of-text has its own
	// announcement.
	public static int MaxThreshold(int stepPercent)
		=> stepPercent <= 0 ? 0 : ((100 - 1) / stepPercent) * stepPercent;
}
