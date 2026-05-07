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

	public LlmModelConfig() { }

	public LlmModelConfig(string name, LlmProvider provider, decimal? customTemperature = null)
	{
		Name = name;
		Provider = provider;
		CustomTemperature = customTemperature;
	}
}
