namespace CognitiveSupport;

/// <summary>
/// How a request at Off asks an Anthropic model not to think. Not every model can turn
/// thinking off, and the ones that cannot refuse <c>thinking: {"type": "disabled"}</c> with
/// a 400. Sending it anyway would cost a failed request on every run, and the retry with no
/// level then runs with the model's default thinking, the opposite of Off.
/// </summary>
public enum AnthropicOffSetting
{
	/// <summary><c>thinking: {"type": "disabled"}</c>: the model accepts it.</summary>
	Disabled,

	/// <summary>
	/// <c>thinking: {"type": "between_tools"}</c>: Claude Sonnet 5.5's own way to turn
	/// thinking off. It takes no other thinking field, and no effort above high.
	/// </summary>
	BetweenTools,

	/// <summary>
	/// No <c>thinking</c> field, and <c>output_config.effort = "low"</c>: the model always
	/// thinks, so the lightest effort is the nearest it gets to Off.
	/// </summary>
	LowEffort,
}
