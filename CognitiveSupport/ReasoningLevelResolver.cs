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
		if (ticked is null)
			return null;

		var tickedSet = new HashSet<ReasoningLevel>(ticked);
		var usable = ReasoningLevelCatalog.FullList(provider).Where(tickedSet.Contains).ToList();

		if (usable.Contains(promptLevel))
			return promptLevel;

		foreach (var level in usable)
		{
			if (!ReasoningLevelCatalog.IsNoReasoning(level))
				return level;
		}

		// Nothing ticked, or only Off/None ticked while the prompt asked for something
		// else. Sending no level is what Off and None mean anyway.
		return null;
	}
}
