namespace CognitiveSupport;

/// <summary>
/// The one wording of a reasoning fallback, shared by the log and the message the user
/// hears, so the two cannot drift apart. Worded like <see cref="FastModeMessages"/>.
/// </summary>
public static class ReasoningMessages
{
	/// <summary>Short title for the status channel that carries the announcement.</summary>
	public const string Title = "Reasoning";

	/// <summary>
	/// What the prompt editor's reasoning level list says when the chosen model has no
	/// levels ticked, so the list is turned off.
	/// </summary>
	public const string NoReasoning = "This model does not reason";

	/// <summary>
	/// The request was retried without a reasoning level because the model refused the
	/// level it was sent.
	/// </summary>
	/// <param name="modelName">The configured model name, as the user typed it.</param>
	/// <param name="level">The level the model did not accept.</param>
	public static string Fallback(string modelName, ReasoningLevel level) =>
		$"This prompt ran without a reasoning level, because {modelName} did not accept "
		+ $"{ReasoningLevelCatalog.DisplayName(level)}.";

	/// <summary>
	/// The prompt editor's list could not show the prompt's level for the chosen model and
	/// shows another one instead.
	/// </summary>
	/// <param name="modelName">The model chosen in the editor.</param>
	/// <param name="from">The level the prompt asked for.</param>
	/// <param name="to">The level the list shows now, which is the one a run sends.</param>
	public static string Moved(string modelName, ReasoningLevel from, ReasoningLevel to) =>
		$"{modelName} does not have {ReasoningLevelCatalog.DisplayName(from)} ticked, so the "
		+ $"reasoning level is now {ReasoningLevelCatalog.DisplayName(to)}.";

	/// <summary>
	/// The prompt editor's list has only Off or None for the chosen model while the prompt
	/// asks for another level, so a run sends no level and the list selects nothing.
	/// </summary>
	/// <param name="modelName">The model chosen in the editor.</param>
	/// <param name="wanted">The level the prompt asks for, which it keeps.</param>
	public static string NoLevelSent(string modelName, ReasoningLevel wanted) =>
		$"{modelName} does not have {ReasoningLevelCatalog.DisplayName(wanted)} ticked, so this "
		+ "prompt runs without a reasoning level.";

	/// <summary>The prompt editor list's placeholder while a run sends no level.</summary>
	public const string NoLevelSelected = "No level is sent";

	/// <summary>
	/// Folds the fallback notice into the message announcing the run's outcome, for the same
	/// reason as <see cref="FastModeMessages.AppendTo"/>: the status channel supersedes rather
	/// than queues, so a notice said on its own would be talked over by the outcome.
	/// </summary>
	/// <param name="outcome">The outcome line.</param>
	/// <param name="notice">The worded notice, or null when there is nothing to report.</param>
	public static string AppendTo(string outcome, string? notice)
	{
		if (string.IsNullOrWhiteSpace(notice))
			return outcome;
		return string.IsNullOrWhiteSpace(outcome)
			? notice
			: $"{outcome.TrimEnd()} {notice}";
	}
}
