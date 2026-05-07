namespace CognitiveSupport;

public class CompositeLlmService : ILlmService
{
	private readonly ILlmService? _openAiService;
	private readonly ILlmService? _anthropicService;
	private readonly IReadOnlyDictionary<string, LlmProvider> _modelProviders;

	public CompositeLlmService(
		ILlmService? openAiService,
		ILlmService? anthropicService,
		IReadOnlyDictionary<string, LlmProvider> modelProviders)
	{
		_openAiService = openAiService;
		_anthropicService = anthropicService;
		_modelProviders = modelProviders ?? throw new ArgumentNullException(nameof(modelProviders));
	}

	public Task<string> CreateChatCompletion(
		IList<LlmChatMessage> messages,
		string llmModelName)
	{
		if (!_modelProviders.TryGetValue(llmModelName, out var provider))
			throw new InvalidOperationException(
				$"Model '{llmModelName}' is not configured in LlmSettings.Models. " +
				$"Add it (with an explicit Provider) to Mutation.json and restart.");

		switch (provider)
		{
			case LlmProvider.Anthropic:
				if (_anthropicService == null)
					throw new InvalidOperationException(
						$"Model '{llmModelName}' is an Anthropic model but no Anthropic API key is configured. " +
						"Please set AnthropicApiKey in Mutation.json and restart.");
				return _anthropicService.CreateChatCompletion(messages, llmModelName);

			case LlmProvider.OpenAI:
				if (_openAiService == null)
					throw new InvalidOperationException(
						$"Model '{llmModelName}' is an OpenAI model but no OpenAI API key is configured. " +
						"Please set ApiKey in Mutation.json and restart.");
				return _openAiService.CreateChatCompletion(messages, llmModelName);

			default:
				throw new InvalidOperationException(
					$"Unsupported LlmProvider '{provider}' for model '{llmModelName}'.");
		}
	}
}
