namespace CognitiveSupport;

public interface ILlmService
{
	Task<string> CreateChatCompletion(IList<LlmChatMessage> messages, string llmModelName);
}
