namespace CognitiveSupport;

// A pure, presentation-ready snapshot of where a read currently is. Computed from
// the raw character position and sentence segmentation so the speak-position hotkey
// can describe the location without the UI reaching into the synthesizer's state.
public readonly record struct ReadingPosition
{
	// False when nothing is being (or has been) read; the other fields are then 0.
	public bool IsReading { get; init; }

	// 1-based sentence number, natural to speak ("Sentence 4 of 22").
	public int SentenceNumber { get; init; }
	public int SentenceCount { get; init; }

	// Percentage through the text, already rounded to the nearest 5% — honest about
	// the estimate's precision and natural to say aloud.
	public int PercentComplete { get; init; }

	// Whole minutes of reading left; 0 means less than a minute remains.
	public int MinutesRemaining { get; init; }

	public static readonly ReadingPosition NotReading = new() { IsReading = false };

	public static ReadingPosition Create(int currentPosition, int textLength, int sentenceIndex, int sentenceCount)
	{
		if (textLength <= 0 || sentenceCount <= 0)
			return NotReading;

		int position = Math.Clamp(currentPosition, 0, textLength);
		double fraction = (double)position / textLength;
		int percent = (int)(Math.Round(fraction * 100d / 5d) * 5);
		percent = Math.Clamp(percent, 0, 100);

		int sentenceNumber = Math.Clamp(sentenceIndex + 1, 1, sentenceCount);
		int minutesRemaining = ReadingEstimate.RemainingWholeMinutes(textLength - position);

		return new ReadingPosition
		{
			IsReading = true,
			SentenceNumber = sentenceNumber,
			SentenceCount = sentenceCount,
			PercentComplete = percent,
			MinutesRemaining = minutesRemaining,
		};
	}
}
