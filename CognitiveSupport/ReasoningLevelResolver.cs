namespace CognitiveSupport;

/// <summary>
/// Decides the reasoning level a prompt actually runs at on a given model. The prompt
/// stores one level and the model stores the levels the user ticked; the two are edited
/// on different pages, so they can disagree, and this is the one place that settles it.
/// </summary>
public static class ReasoningLevelResolver
{
	/// <summary>
	/// The level to send, or null to send no level at all.
	/// <list type="number">
	/// <item>The prompt's level, when it is ticked for the model.</item>
	/// <item>Otherwise the lightest ticked level, not counting Off or None.</item>
	/// <item>Otherwise the ticked Off or None, when that is all that is ticked. It is sent
	/// as it is rather than left out, because a request with no level does not mean "do not
	/// reason": an OpenAI model then runs at its own default effort, and most current
	/// Anthropic models think by default.</item>
	/// <item>Otherwise null: nothing usable is ticked, so the model does not reason.</item>
	/// </list>
	/// Ticked levels that are not in the service's full list (a hand-edited file, or a
	/// model moved to the other service) are ignored, because the service would refuse them.
	/// </summary>
	/// <param name="model">The model the prompt runs on.</param>
	/// <param name="promptLevel">The level the prompt asks for.</param>
	public static ReasoningLevel? Resolve(LlmModelConfig model, ReasoningLevel promptLevel)
	{
		ArgumentNullException.ThrowIfNull(model);
		return Resolve(model.Provider, model.ReasoningLevels, promptLevel);
	}

	/// <inheritdoc cref="Resolve(LlmModelConfig, ReasoningLevel)"/>
	/// <param name="provider">The model's service, which fixes the full list.</param>
	/// <param name="ticked">The levels ticked for the model, in any order. Null counts as none.</param>
	/// <param name="promptLevel">The level the prompt asks for.</param>
	public static ReasoningLevel? Resolve(
		LlmProvider provider,
		IEnumerable<ReasoningLevel>? ticked,
		ReasoningLevel promptLevel)
	{
		var usable = ReasoningLevelCatalog.InServiceOrder(provider, ticked);

		if (usable.Contains(promptLevel))
			return promptLevel;

		foreach (var level in usable)
		{
			if (!ReasoningLevelCatalog.IsNoReasoning(level))
				return level;
		}

		// Only Off or None is ticked (each service offers one of them, so there is at most
		// one), or nothing usable is ticked at all.
		return usable.Count > 0 ? usable[0] : null;
	}
}
