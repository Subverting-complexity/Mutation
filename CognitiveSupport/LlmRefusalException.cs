namespace CognitiveSupport;

/// <summary>
/// The model answered the request but declined to do it. Raised instead of returning an
/// empty string, because an empty "success" would paste nothing over the user's text and
/// report the run as done. Not a transient failure: sending the same request again gets
/// the same answer, so no retry policy handles it.
/// </summary>
public sealed class LlmRefusalException : Exception
{
	/// <summary>The message the user reads or hears.</summary>
	public const string UserMessage =
		"The AI model declined to answer this request, so it returned no text. "
		+ "Try rewording the prompt, or choose a different model for it.";

	public LlmRefusalException()
		: base(UserMessage)
	{
	}
}
