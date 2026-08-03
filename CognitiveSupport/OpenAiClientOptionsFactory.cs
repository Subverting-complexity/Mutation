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
	public static OpenAIClientOptions Create(Uri? endpoint = null, PipelineTransport? transport = null)
	{
		var options = new OpenAIClientOptions
		{
			NetworkTimeout = Timeout.InfiniteTimeSpan
		};
		if (endpoint is not null)
			options.Endpoint = endpoint;
		if (transport is not null)
			options.Transport = transport;
		return options;
	}
}
