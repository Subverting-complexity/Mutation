namespace CognitiveSupport;

public enum LlmProvider
{
	OpenAI = 1,
	Anthropic = 2,
}

public class LlmModelConfig
{
	public string Name { get; set; } = "";
	public LlmProvider Provider { get; set; } = LlmProvider.OpenAI;

	// Per-model temperature. Leave null (omit from JSON) for models that
	// only accept the API default — the request will be sent without a
	// temperature parameter.
	public decimal? CustomTemperature { get; set; }

	/// <summary>
	/// The reasoning levels the user ticked for this model, from its service's list in
	/// <see cref="ReasoningLevelCatalog"/>. Empty means the model does not reason and
	/// Mutation sends no level for it.
	/// <para>
	/// Deliberately starts empty, not full. The settings dialog copies settings by
	/// writing them to JSON and reading them back, and the reader adds saved items to a
	/// list that already has items, so a full starting list would duplicate every level.
	/// Settings files written before this existed get the full list once, on load.
	/// </para>
	/// </summary>
	public List<ReasoningLevel> ReasoningLevels { get; set; } = new();

	public LlmModelConfig() { }

	public LlmModelConfig(string name, LlmProvider provider, decimal? customTemperature = null)
	{
		Name = name;
		Provider = provider;
		CustomTemperature = customTemperature;
	}
}
