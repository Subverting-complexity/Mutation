namespace CognitiveSupport;

/// <summary>
/// Decides whether a failed request that asked for Fast mode failed *because of*
/// Fast mode, so the caller can retry once at standard speed instead of losing the
/// user's transcript. Deliberately not a model allow-list: model IDs are user-editable
/// free text in this app and any hard-coded list would rot as models come and go.
/// </summary>
public static class FastModeFailure
{
	/// <summary>
	/// Anthropic returns 529 when Fast mode capacity is exhausted; 429 is its dedicated
	/// Fast mode rate limit, separate from the standard Opus limit. Both are retryable
	/// by the normal policy first — this only classifies what is left once retries are
	/// exhausted, so the fallback message says "busy" rather than "not enabled".
	/// </summary>
	public static bool IsCapacity(int? statusCode) =>
		statusCode is 429 or 529;

	/// <summary>
	/// True when a non-transient failure looks like the account lacking Fast mode
	/// research-preview access. 403 Forbidden on a Fast mode request is treated as the
	/// entitlement gate; other 4xx statuses only qualify when the provider's message
	/// actually names the Fast mode surface, so an unrelated 400 (bad max_tokens, say)
	/// is not mislabelled as a missing entitlement. 401 is never included — that is a
	/// bad API key and retrying at standard speed would fail identically.
	/// </summary>
	public static bool IsUnavailable(int statusCode, string? providerMessage)
	{
		if (statusCode == 401 || statusCode < 400 || statusCode >= 500)
			return false;
		if (statusCode == 403)
			return true;
		return MentionsFastMode(providerMessage);
	}

	/// <summary>
	/// Whether the provider's error text refers to the Fast mode request surface —
	/// the <c>speed</c> body parameter or the Fast mode beta header.
	/// </summary>
	public static bool MentionsFastMode(string? providerMessage)
	{
		if (string.IsNullOrWhiteSpace(providerMessage))
			return false;

		return providerMessage.Contains("speed", StringComparison.OrdinalIgnoreCase)
			|| providerMessage.Contains("fast-mode", StringComparison.OrdinalIgnoreCase)
			|| providerMessage.Contains("fast mode", StringComparison.OrdinalIgnoreCase);
	}
}
