using System.Text.RegularExpressions;

namespace CognitiveSupport;

/// <summary>
/// Picks the <see cref="AnthropicOffSetting"/> for a model from its name. Model names are
/// free text here, so this matches the model id wherever it appears in the name (a dated
/// snapshot, or a cloud prefix such as <c>anthropic.</c>), and any model it does not know
/// keeps <see cref="AnthropicOffSetting.Disabled"/>, which most models accept.
/// <para>
/// Checked against Anthropic's API reference on 2026-10-18. Claude Opus 5 and Claude Haiku
/// 5.5 accept <c>disabled</c> only at effort high or below; Off sends no effort, and both
/// models default to high or lower, so they stay on <c>disabled</c>.
/// </para>
/// </summary>
public static class AnthropicOffRule
{
	// Each id must not run on into another digit, so "claude-opus-5-5" does not match a
	// future "claude-opus-5-50", and "claude-fable-5" matches both Fable 5 and Fable 5.1.
	private static readonly Regex AlwaysThinks = new(
		@"claude-(opus-5-5|fable-5|mythos-5)(?!\d)",
		RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

	private static readonly Regex OffBetweenTools = new(
		@"claude-sonnet-5-5(?!\d)",
		RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

	/// <summary>The setting Off sends on this model.</summary>
	/// <param name="modelName">The configured model name, as the user typed it.</param>
	public static AnthropicOffSetting For(string? modelName)
	{
		if (string.IsNullOrWhiteSpace(modelName))
			return AnthropicOffSetting.Disabled;
		if (OffBetweenTools.IsMatch(modelName))
			return AnthropicOffSetting.BetweenTools;
		if (AlwaysThinks.IsMatch(modelName))
			return AnthropicOffSetting.LowEffort;
		return AnthropicOffSetting.Disabled;
	}
}
