using System.ClientModel;
using System.ClientModel.Primitives;
using System.Net;
using System.Net.Http;
using CognitiveSupport;

namespace Mutation.Tests;

public class LlmServiceFastModeTests
{
	private const string Model = "gpt-5.6";
	private const string SuccessBody =
		"""{"id":"c1","object":"chat.completion","created":1,"model":"gpt-5.6","choices":[{"index":0,"finish_reason":"stop","message":{"role":"assistant","content":"done"}}]}""";

	private static readonly LlmModelConfig Config =
		new(Model, LlmProvider.OpenAI, customTemperature: null);

	// The SDK's own HttpClient-backed transport, pointed at the fake handler, so the
	// request under assertion is the one the SDK really serializes.
	private static LlmService CreateService(FakeHttpMessageHandler handler, int retryCount = 0) =>
		new(
			"test-key",
			new[] { Config },
			timeoutSeconds: 5,
			retryCount: retryCount,
			transport: new HttpClientPipelineTransport(new HttpClient(handler)));

	private static IList<LlmChatMessage> Messages() =>
		new List<LlmChatMessage> { new(LlmChatRole.User, "hello") };

	[Fact]
	public void BuildChatOptions_SetsFastServiceTier_WhenFastModeOn()
	{
#pragma warning disable OPENAI001 // Evaluation-only SDK surface; see LlmService.BuildChatOptions.
		var options = LlmService.BuildChatOptions(Config, fastMode: true);

		Assert.NotNull(options.ServiceTier);
		Assert.Equal("fast", options.ServiceTier!.Value.ToString());
#pragma warning restore OPENAI001
	}

	[Fact]
	public void BuildChatOptions_LeavesServiceTierUnset_WhenFastModeOff()
	{
#pragma warning disable OPENAI001
		Assert.Null(LlmService.BuildChatOptions(Config, fastMode: false).ServiceTier);
#pragma warning restore OPENAI001
	}

	[Fact]
	public void BuildChatOptions_DefaultsToStandardSpeed()
	{
#pragma warning disable OPENAI001
		Assert.Null(LlmService.BuildChatOptions(Config).ServiceTier);
#pragma warning restore OPENAI001
	}

	[Fact]
	public void BuildChatOptions_FastModeDoesNotDisturbTemperature()
	{
		var withTemperature = new LlmModelConfig(Model, LlmProvider.OpenAI, customTemperature: 0.25m);

		var options = LlmService.BuildChatOptions(withTemperature, fastMode: true);

		Assert.Equal(0.25f, options.Temperature!.Value, precision: 4);
	}

	// ----- On the wire. BuildChatOptions alone would still pass if CreateChatCompletion
	// stopped threading the flag into it, so these assert the serialized request. -----

	[Fact]
	public async Task FastModeOn_SendsTheFastServiceTier()
	{
		var handler = new FakeHttpMessageHandler().Respond(HttpStatusCode.OK, SuccessBody);

		string result = await CreateService(handler).CreateChatCompletion(
			Messages(), Model, new LlmRequestOptions { FastMode = true });

		Assert.Equal("done", result);
		Assert.Contains("\"service_tier\":\"fast\"", Assert.Single(handler.Requests).Body);
	}

	[Fact]
	public async Task FastModeOff_SendsNoServiceTier()
	{
		var handler = new FakeHttpMessageHandler().Respond(HttpStatusCode.OK, SuccessBody);

		await CreateService(handler).CreateChatCompletion(
			Messages(), Model, new LlmRequestOptions { FastMode = false });

		Assert.DoesNotContain("service_tier", Assert.Single(handler.Requests).Body);
	}

	[Fact]
	public async Task NoRequestOptions_BehavesAsFastModeOff()
	{
		var handler = new FakeHttpMessageHandler().Respond(HttpStatusCode.OK, SuccessBody);

		await CreateService(handler).CreateChatCompletion(Messages(), Model);

		Assert.DoesNotContain("service_tier", Assert.Single(handler.Requests).Body);
	}

	[Fact]
	public async Task RejectedServiceTier_RetriesOnceWithoutItAndReportsIt()
	{
		// Dropping the tier is the cheaper direction, so recovering this way cannot
		// surprise anyone's bill — and it beats losing a dictated transcript.
		var handler = new FakeHttpMessageHandler()
			.Respond(HttpStatusCode.BadRequest, """{"error":{"message":"Invalid value: 'fast'. Supported values are: 'auto', 'default', 'flex' and 'priority'.","type":"invalid_request_error","param":"service_tier"}}""")
			.Respond(HttpStatusCode.OK, SuccessBody);

		FastModeFallback? reported = null;
		string result = await CreateService(handler).CreateChatCompletion(
			Messages(),
			Model,
			new LlmRequestOptions { FastMode = true, OnFastModeFallback = f => reported = f });

		Assert.Equal("done", result);
		Assert.Equal(2, handler.Requests.Count);
		Assert.Contains("\"service_tier\":\"fast\"", handler.Requests[0].Body);
		Assert.DoesNotContain("service_tier", handler.Requests[1].Body);
		Assert.Equal(FastModeFallbackReason.UnsupportedModel, reported!.Reason);
	}

	[Fact]
	public async Task UnrelatedBadRequest_IsReportedAsIsWithNoRetry()
	{
		var handler = new FakeHttpMessageHandler()
			.Respond(HttpStatusCode.BadRequest, """{"error":{"message":"max_tokens must be greater than 0","type":"invalid_request_error"}}""");

		FastModeFallback? reported = null;
		await Assert.ThrowsAsync<ClientResultException>(() =>
			CreateService(handler).CreateChatCompletion(
				Messages(),
				Model,
				new LlmRequestOptions { FastMode = true, OnFastModeFallback = f => reported = f }));

		Assert.Null(reported);
		Assert.Single(handler.Requests);
	}

	[Fact]
	public async Task BadApiKey_IsNotTreatedAsAFastModeFailure()
	{
		var handler = new FakeHttpMessageHandler()
			.Respond(HttpStatusCode.Unauthorized, """{"error":{"message":"Incorrect API key provided","type":"invalid_request_error"}}""");

		FastModeFallback? reported = null;
		await Assert.ThrowsAsync<ClientResultException>(() =>
			CreateService(handler).CreateChatCompletion(
				Messages(),
				Model,
				new LlmRequestOptions { FastMode = true, OnFastModeFallback = f => reported = f }));

		Assert.Null(reported);
		Assert.Single(handler.Requests);
	}
}
