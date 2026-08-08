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
	public void Create_TurnsTheSdkSOwnRetryPolicyOff_ByDefault()
	{
		// It used to be left alone, so the SDK's three retries ran inside our four and one
		// outage cost up to sixteen requests. Production takes the same setting as the
		// tests now, which is what makes a counted request count mean anything (issue #318).
		Assert.IsType<ClientRetryPolicy>(OpenAiClientOptionsFactory.Create().RetryPolicy);
	}

	[Fact]
	public void Create_LetsACallerAskForTheSdkSRetriesBack()
	{
		var options = OpenAiClientOptionsFactory.Create(maxRetries: 3);

		Assert.IsType<ClientRetryPolicy>(options.RetryPolicy);
	}
}
