using OpenAI;

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
	public static OpenAIClientOptions Create(Uri? endpoint = null)
	{
		var options = new OpenAIClientOptions
		{
			NetworkTimeout = Timeout.InfiniteTimeSpan
		};
		if (endpoint is not null)
			options.Endpoint = endpoint;
		return options;
	}
}
