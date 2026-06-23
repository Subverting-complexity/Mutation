namespace CognitiveSupport;

// Pure decision for the periodic progress announcements. Kept side-effect free and
// static so the threshold-crossing rules can be unit-tested without a synthesizer.
public static class ReadingProgress
{
	// Returns the highest progress threshold (a multiple of stepPercent, strictly
	// below 100) that the read has just crossed and has not yet announced, or 0 when
	// there is nothing new to announce.
	//
	// When a single jump crosses several thresholds at once (e.g. a skip-forward that
	// leaps 25% -> 75%), only the highest is returned; the caller stores it as the new
	// "last announced" value, which silently marks the skipped lower thresholds as
	// passed so they never stack up as queued utterances. 100% is deliberately never a
	// threshold — end-of-text has its own announcement.
	public static int HighestThresholdCrossed(int lastAnnouncedPercent, int currentPercent, int stepPercent)
	{
		if (stepPercent <= 0) return 0;
		if (currentPercent <= lastAnnouncedPercent) return 0;

		// Largest step multiple that is still strictly below 100 (e.g. 75 for a step of
		// 25, 90 for a step of 30) so we never announce completion as progress.
		int maxThreshold = ((100 - 1) / stepPercent) * stepPercent;

		// Highest step multiple at or below the current percentage.
		int reached = (currentPercent / stepPercent) * stepPercent;

		int highest = Math.Min(reached, maxThreshold);
		return highest > lastAnnouncedPercent && highest > 0 ? highest : 0;
	}
}
