namespace CognitiveSupport;

// Builds the spoken wording for reading-position and progress announcements. Pure and
// static so the phrasing (singular/plural minutes, the "not reading" and "less than a
// minute" edge cases) is unit-testable and lives in one place.
public static class ReadingAnnouncements
{
	public const string NotReading = "Not currently reading.";

	// "Sentence 4 of 22, about 40% through, about 2 minutes left."
	public static string Position(ReadingPosition position)
	{
		if (!position.IsReading)
			return NotReading;

		return $"Sentence {position.SentenceNumber} of {position.SentenceCount}, " +
			$"about {position.PercentComplete}% through, {MinutesLeftPhrase(position.MinutesRemaining)}.";
	}

	// "50%, about 3 minutes left."
	public static string Progress(int percentComplete, int minutesRemaining)
		=> $"{percentComplete}%, {MinutesLeftPhrase(minutesRemaining)}.";

	// "Reading approximately 3 minutes of text. " — trailing space because the caller
	// prepends it to the text being read.
	public static string StartupReadingTime(int minutes)
	{
		if (minutes < 1) minutes = 1;
		return $"Reading approximately {minutes} {MinuteUnit(minutes)} of text. ";
	}

	private static string MinutesLeftPhrase(int minutes)
		=> minutes <= 0 ? "less than a minute left" : $"about {minutes} {MinuteUnit(minutes)} left";

	private static string MinuteUnit(int minutes) => minutes == 1 ? "minute" : "minutes";
}
