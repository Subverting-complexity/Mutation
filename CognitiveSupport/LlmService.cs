using OpenAI.Chat;
using System.ClientModel;

namespace CognitiveSupport;

public class LlmService : ILlmService
{
	private readonly Dictionary<string, ChatClient> _chatClients;
	private readonly Dictionary<string, LlmModelConfig> _modelConfigs;
	private readonly int _timeoutSeconds;

	public LlmService(
		string apiKey,
		IEnumerable<LlmModelConfig> models,
		int timeoutSeconds = 60)
	{
		if (string.IsNullOrEmpty(apiKey)) throw new ArgumentNullException(nameof(apiKey));
		if (models is null) throw new ArgumentNullException(nameof(models));

		var modelList = models.ToList();
		if (modelList.Count == 0)
			throw new ArgumentException("At least one model must be configured.", nameof(models));

		_chatClients = new Dictionary<string, ChatClient>();
		_modelConfigs = new Dictionary<string, LlmModelConfig>();
		_timeoutSeconds = timeoutSeconds > 0 ? timeoutSeconds : 60;

		foreach (var model in modelList)
		{
			_chatClients[model.Name] = new ChatClient(model.Name, apiKey);
			_modelConfigs[model.Name] = model;
		}
	}

	public async Task<string> CreateChatCompletion(
		IList<LlmChatMessage> messages,
		string llmModelName)
	{
		if (!_chatClients.ContainsKey(llmModelName))
			throw new ArgumentException($"{llmModelName} is not one of the configured models. The following are the available, configured models: {string.Join(",", _chatClients.Keys)}", nameof(llmModelName));

		var client = _chatClients[llmModelName];
		var config = _modelConfigs[llmModelName];

		var openAiMessages = messages.Select(ToOpenAiMessage).ToList();

		ChatCompletionOptions options = BuildChatOptions(config);

		using var timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(_timeoutSeconds));
		ClientResult<ChatCompletion> result = await client.CompleteChatAsync(openAiMessages, options, timeoutCts.Token);

		if (result.Value.Content.Count > 0)
		{
			return result.Value.Content[0].Text;
		}
		return string.Empty;
	}

	internal static ChatCompletionOptions BuildChatOptions(LlmModelConfig config)
	{
		var options = new ChatCompletionOptions();
		if (config.CustomTemperature.HasValue)
		{
			options.Temperature = (float)config.CustomTemperature.Value;
		}
		return options;
	}

	private static ChatMessage ToOpenAiMessage(LlmChatMessage msg)
		=> msg.Role switch
		{
			LlmChatRole.System => new SystemChatMessage(msg.Content),
			LlmChatRole.User => new UserChatMessage(msg.Content),
			LlmChatRole.Assistant => new AssistantChatMessage(msg.Content),
			_ => throw new ArgumentOutOfRangeException(nameof(msg.Role), msg.Role, "Unsupported chat role")
		};
}
