namespace CognitiveSupport;

public interface ILlmService
{
	Task<string> CreateChatCompletion(IList<LlmChatMessage> messages, string llmModelName, decimal temperature = 0.7M);
}
