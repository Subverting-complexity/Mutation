namespace CognitiveSupport;

public class CompositeLlmService : ILlmService
{
	private readonly ILlmService? _openAiService;
	private readonly ILlmService? _anthropicService;

	public CompositeLlmService(ILlmService? openAiService, ILlmService? anthropicService)
	{
		_openAiService = openAiService;
		_anthropicService = anthropicService;
	}

	public Task<string> CreateChatCompletion(
		IList<LlmChatMessage> messages,
		string llmModelName,
		decimal temperature = 0.7m)
	{
		if (IsAnthropicModel(llmModelName))
		{
			if (_anthropicService == null)
				throw new InvalidOperationException(
					$"Model '{llmModelName}' is an Anthropic model but no Anthropic API key is configured. " +
					"Please set AnthropicApiKey in Mutation.json and restart.");

			return _anthropicService.CreateChatCompletion(messages, llmModelName, temperature);
		}

		if (_openAiService == null)
			throw new InvalidOperationException(
				$"Model '{llmModelName}' is an OpenAI model but no OpenAI API key is configured. " +
				"Please set ApiKey in Mutation.json and restart.");

		return _openAiService.CreateChatCompletion(messages, llmModelName, temperature);
	}

	public static bool IsAnthropicModel(string modelName)
		=> modelName.StartsWith("claude", StringComparison.OrdinalIgnoreCase);
}
