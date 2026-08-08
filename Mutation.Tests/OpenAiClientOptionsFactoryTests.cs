using System.ClientModel.Primitives;
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

	[Fact]
	public void Create_LeavesTheSdkSOwnRetryPolicyAlone_ByDefault()
	{
		// Production wants the SDK's default retries. Only a test that counts requests
		// turns them off, and it has to ask (issue #311).
		Assert.Null(OpenAiClientOptionsFactory.Create().RetryPolicy);
	}

	[Fact]
	public void Create_CapsTheSdkSOwnRetries_WhenAsked()
	{
		var options = OpenAiClientOptionsFactory.Create(maxRetries: 0);

		Assert.IsType<ClientRetryPolicy>(options.RetryPolicy);
	}
}
