using OpenAI;
using System.ClientModel.Primitives;

namespace CognitiveSupport;

/// <summary>
/// Builds <see cref="OpenAIClientOptions"/> for every OpenAI SDK client the app
/// creates. The SDK's default network timeout is 100 seconds, which silently
/// caps the configurable escalating per-attempt timeouts (e.g. 300 s file
/// transcription). The transport timeout is disabled here; per-attempt
/// CancellationTokenSources remain the sole timeout authority.
/// <para>
/// <b>The SDK's own retry loop is off, and this is why (issue #318).</b> It retried three
/// times on 408, 429 and 5xx, inside the four attempts <see cref="TransientRetry"/> already
/// runs — so one outage cost up to sixteen requests rather than four, and a rate-limited
/// account was answered by asking sixteen more times. Two nested loops also make the
/// escalating per-attempt deadline meaningless, because the SDK's retries all happen inside
/// one of our attempts, sharing its deadline.
/// </para>
/// <para>
/// The one thing the SDK's loop did better was honour the <c>Retry-After</c> header a 429
/// arrives with. That moved to <see cref="RetryAfterHint"/>, which
/// <see cref="TransientRetry"/> consults on every retry — so it now benefits the Deepgram,
/// Anthropic and OCR paths too, not just the two OpenAI-SDK ones.
/// </para>
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
	/// <see cref="TransientRetry"/>. It defaults to none, in production as in tests, for the
	/// reasons above. A caller that wants the SDK's loop back has to say so and had better
	/// say why next to it, because the count it adds multiplies rather than adds.
	/// </param>
	public static OpenAIClientOptions Create(
		Uri? endpoint = null,
		PipelineTransport? transport = null,
		int maxRetries = 0)
	{
		var options = new OpenAIClientOptions
		{
			NetworkTimeout = Timeout.InfiniteTimeSpan,
			RetryPolicy = new ClientRetryPolicy(maxRetries)
		};
		if (endpoint is not null)
			options.Endpoint = endpoint;
		if (transport is not null)
			options.Transport = transport;
		return options;
	}
}
