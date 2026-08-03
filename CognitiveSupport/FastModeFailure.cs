namespace CognitiveSupport;

/// <summary>
/// Decides whether a failed request that asked for Fast mode failed *because of*
/// Fast mode, so the caller can retry once at standard speed instead of losing the
/// user's text — and, when it did, which of the three causes it was, since each one
/// asks something different of the user.
///
/// Deliberately not a model allow-list: model IDs are user-editable free text in this
/// app and any hard-coded list would rot as models come and go. The provider is left to
/// say which models it supports; this only reads the answer.
///
/// A misread is bounded. If the failure was in fact unrelated to Fast mode, the
/// standard-speed retry fails the same way and the provider's real error surfaces —
/// the fallback notice fires only when that retry succeeds.
/// </summary>
public static class FastModeFailure
{
	/// <summary>
	/// Anthropic returns 529 when Fast mode capacity is exhausted; 429 is its dedicated
	/// Fast mode rate limit, separate from the standard Opus limit. Both are retryable
	/// by the normal policy first — this only classifies what is left once retries are
	/// exhausted, so the fallback message says "rate limited or at capacity" rather than
	/// "not enabled". The wording covers an ordinary account-wide 429 too, which is
	/// indistinguishable from here and calls for the same action: wait and retry.
	/// </summary>
	public static bool IsCapacity(int? statusCode) =>
		statusCode is 429 or 529;

	/// <summary>
	/// Classifies a non-transient failure on a Fast mode request, or null when it does
	/// not look like Fast mode's doing and should be reported as-is.
	///
	/// 403 Forbidden on a Fast mode request is taken as the entitlement gate. Other 4xx
	/// statuses qualify only when the provider's message actually names the Fast mode
	/// surface, so an unrelated 400 (a bad max_tokens, say) is not relabelled. 401 is
	/// never included — that is a bad API key, and a standard-speed retry would fail
	/// identically.
	/// </summary>
	public static FastModeFallbackReason? Classify(int statusCode, string? providerMessage)
	{
		if (statusCode == 401 || statusCode < 400 || statusCode >= 500)
			return null;

		// Checked ahead of the 403 gate: a provider that says "this model does not
		// support fast mode" has told us the account is not the problem, whatever
		// status it chose to say it with.
		if (SaysUnsupported(providerMessage))
			return FastModeFallbackReason.UnsupportedModel;

		if (statusCode == 403)
			return FastModeFallbackReason.Unavailable;

		return MentionsFastMode(providerMessage) ? FastModeFallbackReason.Unavailable : null;
	}

	/// <summary>
	/// Whether the provider's error text refers to the Fast mode request surface — the
	/// <c>speed</c> body parameter, the Fast mode beta header, or the OpenAI service tier.
	/// </summary>
	public static bool MentionsFastMode(string? providerMessage)
	{
		if (string.IsNullOrWhiteSpace(providerMessage))
			return false;

		return providerMessage.Contains("speed", StringComparison.OrdinalIgnoreCase)
			|| providerMessage.Contains("fast-mode", StringComparison.OrdinalIgnoreCase)
			|| providerMessage.Contains("fast mode", StringComparison.OrdinalIgnoreCase)
			|| providerMessage.Contains("service_tier", StringComparison.OrdinalIgnoreCase)
			|| providerMessage.Contains("service tier", StringComparison.OrdinalIgnoreCase);
	}

	/// <summary>
	/// Whether the provider is saying the *model* has no Fast mode, rather than the
	/// account having no access. Anthropic phrases this as an invalid-request error
	/// naming the model; OpenAI rejects the tier as an unsupported enum value.
	/// </summary>
	private static bool SaysUnsupported(string? providerMessage)
	{
		if (!MentionsFastMode(providerMessage))
			return false;

		return providerMessage!.Contains("not supported", StringComparison.OrdinalIgnoreCase)
			|| providerMessage.Contains("unsupported", StringComparison.OrdinalIgnoreCase)
			|| providerMessage.Contains("does not support", StringComparison.OrdinalIgnoreCase)
			|| providerMessage.Contains("supported values", StringComparison.OrdinalIgnoreCase)
			|| providerMessage.Contains("invalid value", StringComparison.OrdinalIgnoreCase);
	}
}
