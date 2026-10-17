namespace CognitiveSupport;

/// <summary>
/// What the prompt editor's reasoning level list shows for one model: the levels the user
/// may choose from, and the one selected. The editor holds no rule of its own, so the level
/// it shows is the level a run actually sends.
/// </summary>
/// <param name="Levels">The levels ticked for the model, lightest first. Empty when the model does not reason.</param>
/// <param name="Selected">The selected level, or null when <paramref name="Levels"/> is empty.</param>
public sealed record ReasoningLevelPick(IReadOnlyList<ReasoningLevel> Levels, ReasoningLevel? Selected)
{
	/// <summary>True when the model has at least one level ticked, so the list is turned on.</summary>
	public bool Reasons => Levels.Count > 0;

	/// <summary>The position of <see cref="Selected"/> in <see cref="Levels"/>, or -1 when nothing is selected.</summary>
	public int SelectedIndex =>
		Selected is { } selected
			? Levels.Select((level, index) => (level, index)).First(x => x.level == selected).index
			: -1;

	/// <summary>
	/// The list for a model, with <paramref name="wanted"/> selected when the model has it
	/// ticked. Otherwise the selection moves the way <see cref="ReasoningLevelResolver"/>
	/// moves a run: to the lightest ticked level, not counting Off or None.
	/// </summary>
	/// <param name="model">The model chosen in the editor; null when it is not in the Models list.</param>
	/// <param name="wanted">The level the prompt asks for.</param>
	public static ReasoningLevelPick For(LlmModelConfig? model, ReasoningLevel wanted)
	{
		if (model is null)
			return new ReasoningLevelPick(Array.Empty<ReasoningLevel>(), null);

		var levels = ReasoningLevelCatalog.InServiceOrder(model.Provider, model.ReasoningLevels);
		if (levels.Count == 0)
			return new ReasoningLevelPick(levels, null);

		// The resolver returns null only when Off or None are the only ticked levels and the
		// prompt asked for something else. A run then sends no level, which is what Off and
		// None mean, so the list shows the first of them rather than a blank.
		ReasoningLevel selected = ReasoningLevelResolver.Resolve(model.Provider, levels, wanted) ?? levels[0];
		return new ReasoningLevelPick(levels, selected);
	}

	/// <summary>
	/// True when the list cannot show <paramref name="wanted"/> and shows another level in
	/// its place, so the editor must say what changed.
	/// </summary>
	public bool MovedFrom(ReasoningLevel wanted) => Selected is { } selected && selected != wanted;
}
