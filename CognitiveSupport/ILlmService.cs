namespace CognitiveSupport;

public interface ILlmService
{
	/// <param name="options">
	/// Per-request knobs such as Fast mode. Null means <see cref="LlmRequestOptions.Default"/>
	/// (standard speed), which keeps existing call sites unchanged.
	/// </param>
	Task<string> CreateChatCompletion(
		IList<LlmChatMessage> messages,
		string llmModelName,
		LlmRequestOptions? options = null);
}
