using CognitiveSupport;

namespace Mutation.Tests;

public class AnthropicLlmServiceTimeoutTests
{
	[Fact]
	public void Constructor_DisablesHttpClientTimeout()
	{
		using var httpClient = new HttpClient();
		Assert.Equal(TimeSpan.FromSeconds(100), httpClient.Timeout);

		_ = new AnthropicLlmService(
			"test-key",
			new[] { new LlmModelConfig("claude-fable-5", LlmProvider.Anthropic, customTemperature: null) },
			httpClient);

		Assert.Equal(Timeout.InfiniteTimeSpan, httpClient.Timeout);
	}
}
