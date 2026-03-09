using OpenAI.Chat;
using System.ClientModel;

namespace CognitiveSupport;

public class LlmService : ILlmService
{
	private readonly Dictionary<string, ChatClient> _chatClients;

	public LlmService(
		string apiKey,
		List<string> models)
	{
		if (string.IsNullOrEmpty(apiKey)) throw new ArgumentNullException(nameof(apiKey));
		if (models is null || !models.Any())
			throw new ArgumentNullException(nameof(models));

		_chatClients = new Dictionary<string, ChatClient>();

		foreach (var model in models)
		{
			_chatClients[model] = new ChatClient(model, apiKey);
		}
	}

	public async Task<string> CreateChatCompletion(
		IList<LlmChatMessage> messages,
		string llmModelName,
		decimal temperature = 0.7m)
	{
		if (!_chatClients.ContainsKey(llmModelName))
			throw new ArgumentException($"{llmModelName} is not one of the configured models. The following are the available, configured models: {string.Join(",", _chatClients.Keys)}", nameof(llmModelName));

		var client = _chatClients[llmModelName];

		var openAiMessages = messages.Select(ToOpenAiMessage).ToList();

		ChatCompletionOptions options = new()
		{
			Temperature = (float)temperature
		};

		ClientResult<ChatCompletion> result = await client.CompleteChatAsync(openAiMessages, options);

		if (result.Value.Content.Count > 0)
		{
			return result.Value.Content[0].Text;
		}
		return string.Empty;
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
