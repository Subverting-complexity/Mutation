using System.Net;
using System.Net.Http;
using System.Text.Json;
using CognitiveSupport;

namespace Mutation.Tests;

public class AnthropicFastModeTests
{
	private const string Model = "claude-opus-5";
	private const string SuccessBody = """{"content":[{"type":"text","text":"done"}]}""";

	private static AnthropicLlmService CreateService(FakeHttpMessageHandler handler, int retryCount = 0) =>
		new(
			"test-key",
			new[] { new LlmModelConfig(Model, LlmProvider.Anthropic, customTemperature: null) },
			new HttpClient(handler),
			timeoutSeconds: 5,
			retryCount: retryCount);

	private static IList<LlmChatMessage> Messages() =>
		new List<LlmChatMessage>
		{
			new(LlmChatRole.System, "be brief"),
			new(LlmChatRole.User, "hello"),
		};

	private static string? SpeedOf(string requestBody)
	{
		using var doc = JsonDocument.Parse(requestBody);
		return doc.RootElement.TryGetProperty("speed", out var speed) ? speed.GetString() : null;
	}

	[Fact]
	public async Task FastModeOn_SendsSpeedAndBetaHeader()
	{
		var handler = new FakeHttpMessageHandler().Respond(HttpStatusCode.OK, SuccessBody);
		var service = CreateService(handler);

		string result = await service.CreateChatCompletion(
			Messages(), Model, new LlmRequestOptions { FastMode = true });

		Assert.Equal("done", result);
		var request = Assert.Single(handler.Requests);
		Assert.Equal("fast", SpeedOf(request.Body));
		Assert.Equal("fast-mode-2026-02-01", request.Header("anthropic-beta"));
	}

	[Fact]
	public async Task FastModeOff_SendsNeitherSpeedNorBetaHeader()
	{
		var handler = new FakeHttpMessageHandler().Respond(HttpStatusCode.OK, SuccessBody);
		var service = CreateService(handler);

		await service.CreateChatCompletion(Messages(), Model, new LlmRequestOptions { FastMode = false });

		var request = Assert.Single(handler.Requests);
		Assert.Null(SpeedOf(request.Body));
		Assert.DoesNotContain("speed", request.Body);
		Assert.False(request.HasHeader("anthropic-beta"));
	}

	[Fact]
	public async Task NoRequestOptions_BehavesAsFastModeOff()
	{
		var handler = new FakeHttpMessageHandler().Respond(HttpStatusCode.OK, SuccessBody);
		var service = CreateService(handler);

		await service.CreateChatCompletion(Messages(), Model);

		var request = Assert.Single(handler.Requests);
		Assert.Null(SpeedOf(request.Body));
		Assert.False(request.HasHeader("anthropic-beta"));
	}

	[Theory]
	[InlineData(HttpStatusCode.Forbidden, """{"error":{"type":"permission_error","message":"Your account is not approved for the fast-mode-2026-02-01 beta."}}""")]
	[InlineData(HttpStatusCode.BadRequest, """{"error":{"type":"invalid_request_error","message":"The speed parameter is not available for this account."}}""")]
	public async Task FastModeUnavailable_RetriesOnceAtStandardSpeedAndReportsIt(HttpStatusCode status, string errorBody)
	{
		var handler = new FakeHttpMessageHandler()
			.Respond(status, errorBody)
			.Respond(HttpStatusCode.OK, SuccessBody);
		var service = CreateService(handler);

		FastModeFallback? reported = null;
		string result = await service.CreateChatCompletion(
			Messages(),
			Model,
			new LlmRequestOptions { FastMode = true, OnFastModeFallback = f => reported = f });

		Assert.Equal("done", result);
		Assert.Equal(2, handler.Requests.Count);

		// First attempt asked for Fast mode; the single retry did not.
		Assert.Equal("fast", SpeedOf(handler.Requests[0].Body));
		Assert.True(handler.Requests[0].HasHeader("anthropic-beta"));
		Assert.Null(SpeedOf(handler.Requests[1].Body));
		Assert.False(handler.Requests[1].HasHeader("anthropic-beta"));

		Assert.NotNull(reported);
		Assert.Equal(FastModeFallbackReason.Unavailable, reported!.Reason);
		Assert.Contains("not", reported.ProviderMessage, StringComparison.OrdinalIgnoreCase);
	}

	[Fact]
	public async Task FastModeUnavailable_MessageNamesTheFixAndKeepsProviderTextOutOfIt()
	{
		var handler = new FakeHttpMessageHandler()
			.Respond(HttpStatusCode.Forbidden, """{"error":{"type":"permission_error","message":"speed not permitted"}}""")
			.Respond(HttpStatusCode.OK, SuccessBody);
		var service = CreateService(handler);

		FastModeFallback? reported = null;
		await service.CreateChatCompletion(
			Messages(),
			Model,
			new LlmRequestOptions { FastMode = true, OnFastModeFallback = f => reported = f });

		string announced = FastModeMessages.Describe(reported!.Reason);
		Assert.Contains("request research-preview access", announced);
		Assert.Contains("turn Fast mode off", announced);
		Assert.DoesNotContain("speed not permitted", announced);
		Assert.Contains("speed not permitted", FastModeMessages.DescribeForLog(reported));
	}

	[Fact]
	public async Task FastModeCapacity_FallsBackWithADifferentMessage()
	{
		// 529 is transient, so the retry policy burns its one retry first; only then
		// does the fallback to standard speed happen.
		var handler = new FakeHttpMessageHandler()
			.Respond((HttpStatusCode)529, """{"error":{"type":"overloaded_error","message":"Fast mode is at capacity."}}""")
			.Respond((HttpStatusCode)529, """{"error":{"type":"overloaded_error","message":"Fast mode is at capacity."}}""")
			.Respond(HttpStatusCode.OK, SuccessBody);
		var service = CreateService(handler, retryCount: 1);

		FastModeFallback? reported = null;
		string result = await service.CreateChatCompletion(
			Messages(),
			Model,
			new LlmRequestOptions { FastMode = true, OnFastModeFallback = f => reported = f });

		Assert.Equal("done", result);
		Assert.Equal(FastModeFallbackReason.Busy, reported!.Reason);
		Assert.NotEqual(FastModeMessages.Unavailable, FastModeMessages.Describe(reported.Reason));

		// The final request ran at standard speed.
		Assert.Null(SpeedOf(handler.Requests[^1].Body));
		Assert.False(handler.Requests[^1].HasHeader("anthropic-beta"));
	}

	[Fact]
	public async Task BadApiKey_IsNotTreatedAsFastModeUnavailable()
	{
		// A 401 would fail identically at standard speed, so retrying would only waste
		// the user's time and hide the real problem behind the wrong message.
		var handler = new FakeHttpMessageHandler()
			.Respond(HttpStatusCode.Unauthorized, """{"error":{"type":"authentication_error","message":"invalid x-api-key"}}""");
		var service = CreateService(handler);

		FastModeFallback? reported = null;
		var ex = await Assert.ThrowsAsync<NonTransientLlmException>(() =>
			service.CreateChatCompletion(
				Messages(),
				Model,
				new LlmRequestOptions { FastMode = true, OnFastModeFallback = f => reported = f }));

		Assert.Equal(401, ex.StatusCode);
		Assert.Null(reported);
		Assert.Single(handler.Requests);
	}

	[Fact]
	public async Task UnrelatedBadRequest_IsNotTreatedAsFastModeUnavailable()
	{
		var handler = new FakeHttpMessageHandler()
			.Respond(HttpStatusCode.BadRequest, """{"error":{"type":"invalid_request_error","message":"max_tokens must be greater than 0"}}""");
		var service = CreateService(handler);

		FastModeFallback? reported = null;
		await Assert.ThrowsAsync<NonTransientLlmException>(() =>
			service.CreateChatCompletion(
				Messages(),
				Model,
				new LlmRequestOptions { FastMode = true, OnFastModeFallback = f => reported = f }));

		Assert.Null(reported);
		Assert.Single(handler.Requests);
	}

	[Fact]
	public async Task StandardSpeedRetryFails_ReportsTheFailureRatherThanASuccessNotice()
	{
		var handler = new FakeHttpMessageHandler()
			.Respond(HttpStatusCode.Forbidden, """{"error":{"type":"permission_error","message":"speed not permitted"}}""")
			.Respond(HttpStatusCode.BadRequest, """{"error":{"type":"invalid_request_error","message":"max_tokens must be greater than 0"}}""");
		var service = CreateService(handler);

		FastModeFallback? reported = null;
		await Assert.ThrowsAsync<NonTransientLlmException>(() =>
			service.CreateChatCompletion(
				Messages(),
				Model,
				new LlmRequestOptions { FastMode = true, OnFastModeFallback = f => reported = f }));

		// Never tell the user "it ran at standard speed" for a request that then failed.
		Assert.Null(reported);
		Assert.Equal(2, handler.Requests.Count);
	}
}
