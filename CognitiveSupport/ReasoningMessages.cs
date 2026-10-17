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
	/// The request was retried without a reasoning level because the model refused the
	/// level it was sent.
	/// </summary>
	/// <param name="modelName">The configured model name, as the user typed it.</param>
	/// <param name="level">The level the model did not accept.</param>
	public static string Fallback(string modelName, ReasoningLevel level) =>
		$"This prompt ran without a reasoning level, because {modelName} did not accept "
		+ $"{ReasoningLevelCatalog.DisplayName(level)}.";
}
