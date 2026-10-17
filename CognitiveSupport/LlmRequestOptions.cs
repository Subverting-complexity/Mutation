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

	/// <summary>
	/// Invoked when the answer stopped at the model's length limit. The text that did
	/// arrive is still returned; the caller tells the user the end is missing, using
	/// <see cref="CutShortMessages"/>.
	/// </summary>
	public Action? OnCutShort { get; init; }

	/// <summary>
	/// The reasoning level the prompt asks for. The service settles it against the levels
	/// ticked for the model with <see cref="ReasoningLevelResolver"/>, so a caller may pass
	/// the prompt's stored level as it is. Null sends no level, so the request carries no
	/// reasoning fields, as before reasoning levels existed.
	/// </summary>
	public ReasoningLevel? ReasoningLevel { get; init; }

	/// <summary>
	/// Invoked when the model refused the reasoning level and the request was retried
	/// without one. It receives the level that was refused, and runs only after the retry
	/// succeeded. The caller words it with <see cref="ReasoningMessages.Fallback"/>; the
	/// user's saved level is never changed.
	/// </summary>
	public Action<ReasoningLevel>? OnReasoningFallback { get; init; }
}
