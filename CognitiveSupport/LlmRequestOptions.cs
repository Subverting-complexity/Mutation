namespace CognitiveSupport;

/// <summary>
/// Per-request knobs that travel with a single chat completion, separate from the
/// per-model configuration in <see cref="LlmModelConfig"/>. Introduced so a
/// per-prompt setting (Fast mode) can reach the provider without every caller
/// growing a new positional parameter each time another knob is added.
/// Deliberately free of provider/HTTP types so the domain boundary stays intact.
/// </summary>
public sealed class LlmRequestOptions
{
	/// <summary>Standard speed, no notification callback. Used when a caller passes null.</summary>
	public static readonly LlmRequestOptions Default = new();

	/// <summary>
	/// Run the same model at premium inference speed, billed at roughly twice the
	/// standard token price. Off by default.
	/// </summary>
	public bool FastMode { get; init; }

	/// <summary>
	/// Invoked when a request that asked for Fast mode could not run in Fast mode and
	/// was retried at standard speed. The caller decides how to surface it — the
	/// service never blocks, prompts, or rewrites the user's setting.
	/// </summary>
	public Action<FastModeFallback>? OnFastModeFallback { get; init; }
}

/// <summary>Why a Fast mode request had to fall back to standard speed.</summary>
public enum FastModeFallbackReason
{
	/// <summary>The account lacks Fast mode access — the user must request it.</summary>
	Unavailable = 1,

	/// <summary>Fast mode is rate limited or out of capacity — the user should retry later.</summary>
	Busy = 2,
}

/// <summary>
/// A Fast mode request that was retried at standard speed, carrying both the reason
/// (which drives the user-facing wording) and the provider's original message (kept
/// for the error log, never shown raw).
/// </summary>
/// <param name="Reason">Why Fast mode could not be used.</param>
/// <param name="ProviderMessage">The provider's own error text, for diagnosis.</param>
public sealed record FastModeFallback(FastModeFallbackReason Reason, string ProviderMessage);
