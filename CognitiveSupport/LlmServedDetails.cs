namespace CognitiveSupport;

/// <summary>
/// What the provider said it served for one request, read from its reply. Every value
/// is null when the reply did not carry it, so a missing field is logged as unknown
/// rather than guessed.
/// </summary>
/// <param name="Model">The model the provider says answered, which can differ from the one asked for.</param>
/// <param name="ServiceTier">The service tier the provider says it served the request at.</param>
/// <param name="InputTokens">Tokens the provider counted in the request.</param>
/// <param name="OutputTokens">Tokens the provider counted in the answer.</param>
internal sealed record LlmServedDetails(
	string? Model,
	string? ServiceTier,
	int? InputTokens,
	int? OutputTokens)
{
	public static readonly LlmServedDetails Unknown = new(null, null, null, null);
}
