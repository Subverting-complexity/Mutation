namespace CognitiveSupport;

/// <summary>
/// Words the lines both LLM services write about each request: one when it starts, and
/// one when it completes, fails or is cancelled. Each line names the entry point, the
/// provider and the requested model, so the log can confirm or rule out Mutation as the
/// sender of a request that shows up on a provider's usage page (issue #392).
/// <para>
/// Built from settings and reply metadata only. Prompt text, clipboard content and API
/// keys are never passed in, so no line can carry them.
/// </para>
/// </summary>
internal static class LlmRequestLog
{
	private const string Unknown = "unknown";

	public static string Started(
		LlmRequestOptions options,
		LlmProvider provider,
		string requestedModel,
		ReasoningLevel? level)
		=> $"LLM request starting ({Who(options, provider, requestedModel)}, " +
			$"fastMode={options.FastMode}, reasoningLevel={LevelText(level)}).";

	/// <param name="fastModeSent">Whether the send that succeeded asked for Fast mode, after any fallback.</param>
	/// <param name="levelSent">The level the send that succeeded carried, after any fallback.</param>
	public static string Completed(
		LlmRequestOptions options,
		LlmProvider provider,
		string requestedModel,
		LlmServedDetails served,
		bool fastModeSent,
		ReasoningLevel? levelSent)
		=> $"LLM request completed ({Who(options, provider, requestedModel)}, " +
			$"servedModel={Text(served.Model)}, serviceTier={Text(served.ServiceTier)}, " +
			$"inputTokens={Number(served.InputTokens)}, outputTokens={Number(served.OutputTokens)}, " +
			$"fastModeSent={fastModeSent}, reasoningLevelSent={LevelText(levelSent)}).";

	/// <summary>
	/// Names the exception type and message only. The caller logs the full exception as
	/// it always has; this line is what ties that error to the model it was for.
	/// </summary>
	public static string Failed(
		LlmRequestOptions options,
		LlmProvider provider,
		string requestedModel,
		Exception exception)
		=> $"LLM request failed ({Who(options, provider, requestedModel)}): " +
			$"{exception.GetType().Name}: {exception.Message}";

	public static string Cancelled(
		LlmRequestOptions options,
		LlmProvider provider,
		string requestedModel)
		=> $"LLM request cancelled ({Who(options, provider, requestedModel)}).";

	private static string Who(LlmRequestOptions options, LlmProvider provider, string requestedModel)
		=> $"entryPoint={Text(options.EntryPoint)}, provider={provider}, requestedModel={requestedModel}";

	private static string LevelText(ReasoningLevel? level) => level?.ToString() ?? "none";

	private static string Text(string? value) => string.IsNullOrWhiteSpace(value) ? Unknown : value;

	private static string Number(int? value) => value?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? Unknown;
}
