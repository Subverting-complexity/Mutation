using CognitiveSupport;

namespace Mutation.Tests;

public class OpenAiClientOptionsFactoryTests
{
	[Fact]
	public void Create_DisablesNetworkTimeout()
	{
		var options = OpenAiClientOptionsFactory.Create();

		Assert.Equal(Timeout.InfiniteTimeSpan, options.NetworkTimeout);
		Assert.Null(options.Endpoint);
	}

	[Fact]
	public void Create_SetsEndpoint_WhenProvided()
	{
		var endpoint = new Uri("https://example.com/v1/");

		var options = OpenAiClientOptionsFactory.Create(endpoint);

		Assert.Equal(Timeout.InfiniteTimeSpan, options.NetworkTimeout);
		Assert.Equal(endpoint, options.Endpoint);
	}
}
