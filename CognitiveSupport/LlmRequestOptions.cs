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
