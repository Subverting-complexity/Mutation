namespace CognitiveSupport;

public interface ILlmService
{
	/// <param name="options">
	/// Per-request knobs such as Fast mode. Null means <see cref="LlmRequestOptions.Default"/>
	/// (standard speed), which keeps existing call sites unchanged.
	/// </param>
	/// <param name="cancellationToken">
	/// Cuts the whole call short, retries included. Without it a provider outage runs the
	/// full escalating-timeout ladder — minutes of it, and twice over when a Fast mode
	/// request falls back to standard speed — with no way for the user or a closing window
	/// to abandon it (issue #256). Matches <see cref="ISpeechToTextService"/> and
	/// <see cref="IOcrService"/>, which have always taken one.
	/// </param>
	Task<string> CreateChatCompletion(
		IList<LlmChatMessage> messages,
		string llmModelName,
		LlmRequestOptions? options = null,
		CancellationToken cancellationToken = default);
}
