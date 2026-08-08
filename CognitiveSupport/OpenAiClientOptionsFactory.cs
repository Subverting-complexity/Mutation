using OpenAI;
using System.ClientModel.Primitives;

namespace CognitiveSupport;

/// <summary>
/// Builds <see cref="OpenAIClientOptions"/> for every OpenAI SDK client the app
/// creates. The SDK's default network timeout is 100 seconds, which silently
/// caps the configurable escalating per-attempt timeouts (e.g. 300 s file
/// transcription). The transport timeout is disabled here; per-attempt
/// CancellationTokenSources remain the sole timeout authority.
/// </summary>
public static class OpenAiClientOptionsFactory
{
	/// <param name="transport">
	/// Replaces the HTTP transport. Left null in production; a test supplies a fake so
	/// the exact request the SDK puts on the wire can be asserted, the way
	/// <see cref="AnthropicLlmService"/>'s injected HttpClient already allows.
	/// </param>
	/// <param name="maxRetries">
	/// Caps the SDK's <em>own</em> retry policy, which is a separate loop from the one in
	/// <see cref="TransientRetry"/>. Left null in production, where the SDK's default
	/// three retries are wanted. A test that counts requests passes 0: otherwise a single
	/// transient reply is retried by the SDK before the caller-level pipeline ever sees
	/// it, and the request count measures two nested retry loops at once rather than the
	/// one under test (issue #311).
	/// </param>
	public static OpenAIClientOptions Create(
		Uri? endpoint = null,
		PipelineTransport? transport = null,
		int? maxRetries = null)
	{
		var options = new OpenAIClientOptions
		{
			NetworkTimeout = Timeout.InfiniteTimeSpan
		};
		if (endpoint is not null)
			options.Endpoint = endpoint;
		if (transport is not null)
			options.Transport = transport;
		if (maxRetries is not null)
			options.RetryPolicy = new ClientRetryPolicy(maxRetries.Value);
		return options;
	}
}
