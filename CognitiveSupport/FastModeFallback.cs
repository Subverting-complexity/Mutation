namespace CognitiveSupport;

/// <summary>
/// A Fast mode request that was retried at standard speed, carrying both the reason
/// (which drives the user-facing wording) and the provider's original message (kept
/// for the error log, never shown raw).
/// </summary>
/// <param name="Reason">Why Fast mode could not be used.</param>
/// <param name="ProviderMessage">The provider's own error text, for diagnosis.</param>
public sealed record FastModeFallback(FastModeFallbackReason Reason, string ProviderMessage);

/// <summary>
/// Why a Fast mode request had to fall back to standard speed. Each value exists
/// because it demands a different action from the user; collapsing any two of them
/// would send someone off to fix the wrong thing.
/// </summary>
public enum FastModeFallbackReason
{
	/// <summary>The account lacks Fast mode access — the user must request it.</summary>
	Unavailable = 1,

	/// <summary>Fast mode is rate limited or out of capacity — the user should retry later.</summary>
	Busy = 2,

	/// <summary>
	/// The account may well have Fast mode, but this model does not offer it — the user
	/// must switch models, and telling them to go request access would waste their time.
	/// </summary>
	UnsupportedModel = 3,
}
