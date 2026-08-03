namespace CognitiveSupport;

/// <summary>
/// The user-facing wording announced when a Fast mode request fell back to standard
/// speed. Kept out of the catch blocks that raise it so the phrasing a screen-reader
/// user hears is centralized and unit-testable, and so a missing-access failure can
/// never be worded as a capacity failure (they need different actions from the user).
/// </summary>
public static class FastModeMessages
{
	/// <summary>Short title for the status channel that carries the announcement.</summary>
	public const string Title = "Fast mode";

	/// <summary>
	/// The account is not approved for the Anthropic Fast mode research preview.
	/// Names the human action, because the code cannot take it for the user.
	/// </summary>
	public const string Unavailable =
		"Fast mode isn't enabled for your Anthropic account, so this prompt ran at standard speed. "
		+ "To use Fast mode, request research-preview access for your Anthropic account "
		+ "(via your account manager or the Fast mode waitlist), or turn Fast mode off for this prompt "
		+ "in the prompt editor.";

	/// <summary>
	/// Fast mode has its own rate limit and capacity pool, separate from standard Opus.
	/// Deliberately worded as a wait-and-retry, not as a missing entitlement.
	/// </summary>
	public const string Busy =
		"Fast mode is busy or at capacity, so this prompt ran at standard speed. "
		+ "Try again in a moment, or turn Fast mode off for this prompt in the prompt editor.";

	/// <summary>Announcement text for a fallback.</summary>
	public static string Describe(FastModeFallbackReason reason) =>
		reason switch
		{
			FastModeFallbackReason.Busy => Busy,
			_ => Unavailable,
		};

	/// <summary>
	/// The line written to the error log, keeping the provider's original message
	/// available for diagnosis without ever showing it raw to the user.
	/// </summary>
	public static string DescribeForLog(FastModeFallback fallback) =>
		$"Fast mode fell back to standard speed ({fallback.Reason}). Provider said: "
		+ (string.IsNullOrWhiteSpace(fallback.ProviderMessage) ? "(no message)" : fallback.ProviderMessage.Trim());
}
