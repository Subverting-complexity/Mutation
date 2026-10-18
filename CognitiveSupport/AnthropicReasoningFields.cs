namespace CognitiveSupport;

/// <summary>
/// How a resolved reasoning level becomes fields on an Anthropic Messages request. Kept
/// apart from the HTTP code so the mapping can be read, and tested, as one table.
/// <list type="bullet">
/// <item>No level: no reasoning fields at all, and the model's temperature as before.</item>
/// <item>Off: the nearest to "no thinking" the model allows, from <see cref="AnthropicOffRule"/>.
/// Most models get <c>thinking.type = "disabled"</c>; Claude Sonnet 5.5 gets
/// <c>"between_tools"</c>; a model that always thinks gets no <c>thinking</c> field and
/// effort low. The temperature is kept unless the model is still thinking.</item>
/// <item>Low to Max: adaptive thinking (<c>thinking.type = "adaptive"</c>), so the model
/// decides when and how much to think, with <c>output_config.effort</c> setting how hard
/// it tries. No temperature, because the service refuses one while the model thinks.</item>
/// </list>
/// </summary>
public static class AnthropicReasoningFields
{
	/// <summary>The value of <c>thinking.type</c>, or null to send no <c>thinking</c> field.</summary>
	/// <param name="level">The resolved level, or null for none.</param>
	/// <param name="modelName">The model the request goes to, which decides what Off sends.</param>
	public static string? ThinkingType(ReasoningLevel? level, string modelName) =>
		level switch
		{
			ReasoningLevel.Off => AnthropicOffRule.For(modelName) switch
			{
				AnthropicOffSetting.BetweenTools => "between_tools",
				AnthropicOffSetting.LowEffort => null,
				_ => "disabled",
			},
			_ when Effort(level, modelName) is not null => "adaptive",
			_ => null,
		};

	/// <summary>The value of <c>output_config.effort</c>, or null to send no effort.</summary>
	/// <param name="level">The resolved level, or null for none.</param>
	/// <param name="modelName">The model the request goes to, which decides what Off sends.</param>
	public static string? Effort(ReasoningLevel? level, string modelName) =>
		level switch
		{
			ReasoningLevel.Off => AnthropicOffRule.For(modelName) == AnthropicOffSetting.LowEffort ? "low" : null,
			ReasoningLevel.Low => "low",
			ReasoningLevel.Medium => "medium",
			ReasoningLevel.High => "high",
			ReasoningLevel.ExtraHigh => "xhigh",
			ReasoningLevel.Max => "max",
			// None and Minimal are OpenAI levels, which the resolver never hands an
			// Anthropic model.
			_ => null,
		};

	/// <summary>
	/// Whether the model's own temperature may go on the request: always, except while the
	/// request makes the model think, which is whenever it sends an effort.
	/// </summary>
	/// <param name="level">The resolved level, or null for none.</param>
	/// <param name="modelName">The model the request goes to, which decides what Off sends.</param>
	public static bool SendsTemperature(ReasoningLevel? level, string modelName) => Effort(level, modelName) is null;
}
