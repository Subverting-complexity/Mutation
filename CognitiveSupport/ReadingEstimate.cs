namespace CognitiveSupport;

// Single source of truth for the characters -> reading-time estimate shared by the
// startup reading-time announcement, the speak-position hotkey, and the periodic
// progress announcements. Keeping the constants and arithmetic in one pure, static
// place stops the three callers from drifting apart.
public static class ReadingEstimate
{
	// Rough average word length (including the trailing space) and an adult reading
	// pace. These mirror the values the original BuildLengthAnnouncement used so the
	// startup announcement keeps producing the same minute estimate.
	public const int CharsPerWord = 5;
	public const int WordsPerMinute = 180;

	// Estimated reading time, in minutes, as a raw (unrounded) value. Used for the
	// threshold comparisons ("only announce when the read is longer than N minutes")
	// where rounding to a whole minute first would distort the cutoff.
	public static double EstimateMinutes(int charCount)
	{
		if (charCount <= 0) return 0d;
		double words = (double)charCount / CharsPerWord;
		return words / WordsPerMinute;
	}

	// Whole-minute estimate for phrases that always name at least one minute, e.g.
	// the startup "Reading approximately N minutes of text" announcement.
	public static int SpokenWholeMinutes(int charCount)
	{
		int minutes = (int)Math.Round(EstimateMinutes(charCount));
		return minutes < 1 ? 1 : minutes;
	}

	// Whole-minute estimate of how much reading remains. Allowed to be 0 so callers
	// can say "less than a minute left" near the end of a read.
	public static int RemainingWholeMinutes(int remainingChars)
	{
		int minutes = (int)Math.Round(EstimateMinutes(remainingChars));
		return minutes < 0 ? 0 : minutes;
	}

	// Shared gate for the "only announce when the read is longer than N minutes"
	// thresholds used by both the startup and periodic announcements.
	public static bool ExceedsMinutes(int charCount, int minimumMinutes)
		=> EstimateMinutes(charCount) > minimumMinutes;
}
