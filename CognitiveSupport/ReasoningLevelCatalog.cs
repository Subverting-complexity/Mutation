namespace CognitiveSupport;

/// <summary>
/// The fixed list of reasoning levels each service offers, lightest first, and the
/// name a person sees for each level. The order matters: the resolver falls back to
/// the lightest ticked level, and "lightest" means earliest in this list.
/// </summary>
public static class ReasoningLevelCatalog
{
	private static readonly IReadOnlyList<ReasoningLevel> AnthropicLevels = new[]
	{
		ReasoningLevel.Off,
		ReasoningLevel.Low,
		ReasoningLevel.Medium,
		ReasoningLevel.High,
		ReasoningLevel.ExtraHigh,
		ReasoningLevel.Max,
	};

	private static readonly IReadOnlyList<ReasoningLevel> OpenAiLevels = new[]
	{
		ReasoningLevel.None,
		ReasoningLevel.Minimal,
		ReasoningLevel.Low,
		ReasoningLevel.Medium,
		ReasoningLevel.High,
		ReasoningLevel.ExtraHigh,
	};

	/// <summary>The service's full list, lightest first.</summary>
	public static IReadOnlyList<ReasoningLevel> FullList(LlmProvider provider) =>
		provider == LlmProvider.Anthropic ? AnthropicLevels : OpenAiLevels;

	/// <summary>
	/// True for the levels that mean "do not reason" (Anthropic Off, OpenAI None).
	/// The fallback skips them: a prompt that asked for some reasoning should get the
	/// lightest real reasoning the model allows, not none at all.
	/// </summary>
	public static bool IsNoReasoning(ReasoningLevel level) =>
		level is ReasoningLevel.Off or ReasoningLevel.None;

	/// <summary>The name shown to the user, for example "Extra high".</summary>
	public static string DisplayName(ReasoningLevel level) =>
		level switch
		{
			ReasoningLevel.Off => "Off",
			ReasoningLevel.None => "None",
			ReasoningLevel.Minimal => "Minimal",
			ReasoningLevel.Low => "Low",
			ReasoningLevel.Medium => "Medium",
			ReasoningLevel.High => "High",
			ReasoningLevel.ExtraHigh => "Extra high",
			ReasoningLevel.Max => "Max",
			_ => level.ToString(),
		};
}
