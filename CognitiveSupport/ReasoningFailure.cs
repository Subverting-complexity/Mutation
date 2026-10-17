namespace CognitiveSupport;

/// <summary>
/// Decides whether a failed request that carried a reasoning level failed <em>because of</em>
/// that level, so the caller can retry once without it instead of losing the user's text.
/// Built like <see cref="FastModeFailure"/>, and for the same reason it is not a model
/// list: model names are free text here and a fixed list would go out of date. The service
/// says what it refused; this only reads the answer.
/// <para>
/// A misread is bounded. If the failure had nothing to do with the level, the retry
/// without it fails the same way and the service's real error reaches the user; the
/// fallback notice runs only when that retry succeeds.
/// </para>
/// </summary>
public static class ReasoningFailure
{
	// The request fields a reasoning level is sent through, and the words the service
	// uses when it refuses one of them. "max_tokens" is deliberately absent: a bad answer
	// limit is not the level's fault, and naming it alone must not trigger a retry.
	private static readonly string[] ReasoningTerms =
	[
		"thinking",
		"effort",
		"output_config",
		"budget_tokens",
		"adaptive",
		"reasoning",
	];

	/// <summary>
	/// True when a non-transient failure looks like the service refusing the reasoning
	/// setting. Only a 4xx qualifies, and only when the service's message names the
	/// reasoning surface, so an unrelated 400 is reported as it is. 401 never qualifies:
	/// that is a bad API key, and a retry without a level would fail the same way. Nor
	/// does 429: a rate limit is the retry policy's business, whatever its text says.
	/// </summary>
	/// <param name="statusCode">The HTTP status the service answered with.</param>
	/// <param name="providerMessage">The service's error text.</param>
	public static bool IsCause(int statusCode, string? providerMessage)
	{
		if (statusCode is 401 or 429 || statusCode < 400 || statusCode >= 500)
			return false;

		return MentionsReasoning(providerMessage);
	}

	/// <summary>Whether the service's error text names a reasoning field or setting.</summary>
	/// <param name="providerMessage">The service's error text.</param>
	public static bool MentionsReasoning(string? providerMessage)
	{
		if (string.IsNullOrWhiteSpace(providerMessage))
			return false;

		foreach (var term in ReasoningTerms)
		{
			if (providerMessage.Contains(term, StringComparison.OrdinalIgnoreCase))
				return true;
		}
		return false;
	}
}
