namespace CognitiveSupport;

/// <summary>
/// The wording announced when the model's answer stopped at its length limit. Kept in one
/// place so every path that delivers an answer says the same thing, and so the phrase a
/// screen-reader user hears is unit-testable.
/// </summary>
public static class CutShortMessages
{
	/// <summary>Told to the user alongside the text that did arrive.</summary>
	public const string Notice =
		"The AI's answer was cut short because it reached its length limit, so the end of the text is missing.";

	/// <summary>
	/// Folds the notice into the message announcing the run's outcome. The status channel
	/// supersedes rather than queues, so a separate announcement would be talked over by
	/// the outcome — or would talk over it.
	/// </summary>
	/// <param name="cutShort">False leaves the outcome untouched.</param>
	public static string AppendTo(string outcome, bool cutShort)
	{
		if (!cutShort)
			return outcome;
		return string.IsNullOrWhiteSpace(outcome)
			? Notice
			: $"{outcome.TrimEnd()} {Notice}";
	}
}
