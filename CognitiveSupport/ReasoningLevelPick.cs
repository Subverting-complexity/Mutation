namespace CognitiveSupport;

/// <summary>
/// What the prompt editor's reasoning level list shows for one model: the levels the user
/// may choose from, and the one selected. The editor holds no rule of its own, so the level
/// it shows is the level a run actually sends.
/// </summary>
/// <param name="Levels">The levels ticked for the model, lightest first. Empty when the model does not reason.</param>
/// <param name="Selected">
/// The selected level, or null when a run sends no level: the model does not reason, or only
/// Off or None are ticked and the prompt asks for something else.
/// </param>
public sealed record ReasoningLevelPick(IReadOnlyList<ReasoningLevel> Levels, ReasoningLevel? Selected)
{
	/// <summary>True when the model has at least one level ticked, so the list is turned on.</summary>
	public bool Reasons => Levels.Count > 0;

	/// <summary>
	/// True when the model reasons but a run sends no level, because only Off or None are
	/// ticked and the prompt asks for another level. The list then selects nothing: showing
	/// Off or None would make saving store it, and a stored Off or None is sent, which
	/// changes the request (what such a model should send is an open decision, so the
	/// editor must not settle it by accident).
	/// </summary>
	public bool SendsNoLevel => Reasons && Selected is null;

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

		// Null when only Off or None are ticked and the prompt asked for something else: a
		// run then sends no level, and the list selects nothing to match.
		return new ReasoningLevelPick(levels, ReasoningLevelResolver.Resolve(model.Provider, levels, wanted));
	}

	/// <summary>
	/// True when the list cannot show <paramref name="wanted"/> and shows another level in
	/// its place, so the editor must say what changed.
	/// </summary>
	public bool MovedFrom(ReasoningLevel wanted) => Selected is { } selected && selected != wanted;
}
