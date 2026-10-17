namespace CognitiveSupport;

/// <summary>
/// How a resolved reasoning level becomes fields on an Anthropic Messages request. Kept
/// apart from the HTTP code so the mapping can be read, and tested, as one table.
/// <list type="bullet">
/// <item>No level: no reasoning fields at all, and the model's temperature as before.</item>
/// <item>Off: thinking switched off (<c>thinking.type = "disabled"</c>), temperature kept.</item>
/// <item>Low to Max: adaptive thinking (<c>thinking.type = "adaptive"</c>), so the model
/// decides when and how much to think, with <c>output_config.effort</c> setting how hard
/// it tries. No temperature, because the service refuses one while the model thinks.</item>
/// </list>
/// </summary>
public static class AnthropicReasoningFields
{
	/// <summary>The value of <c>thinking.type</c>, or null to send no <c>thinking</c> field.</summary>
	/// <param name="level">The resolved level, or null for none.</param>
	public static string? ThinkingType(ReasoningLevel? level) =>
		level switch
		{
			ReasoningLevel.Off => "disabled",
			_ when Effort(level) is not null => "adaptive",
			_ => null,
		};

	/// <summary>The value of <c>output_config.effort</c>, or null to send no effort.</summary>
	/// <param name="level">The resolved level, or null for none.</param>
	public static string? Effort(ReasoningLevel? level) =>
		level switch
		{
			ReasoningLevel.Low => "low",
			ReasoningLevel.Medium => "medium",
			ReasoningLevel.High => "high",
			ReasoningLevel.ExtraHigh => "xhigh",
			ReasoningLevel.Max => "max",
			// Off sends no effort; None and Minimal are OpenAI levels, which the resolver
			// never hands an Anthropic model.
			_ => null,
		};

	/// <summary>
	/// Whether the model's own temperature may go on the request: always, except while a
	/// level that makes the model think is sent.
	/// </summary>
	/// <param name="level">The resolved level, or null for none.</param>
	public static bool SendsTemperature(ReasoningLevel? level) => Effort(level) is null;
}
