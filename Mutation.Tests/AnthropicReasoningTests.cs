using System.Net;
using System.Net.Http;
using System.Text.Json;
using CognitiveSupport;

namespace Mutation.Tests;

/// <summary>
/// The reasoning level on an Anthropic request: which fields each level puts in the body,
/// the limits it gets, and the one retry without a level when the model refuses it.
/// </summary>
public class AnthropicReasoningTests : IDisposable
{
	private const string Model = "claude-opus-5";
	private const string SuccessBody = """{"content":[{"type":"text","text":"done"}],"stop_reason":"end_turn"}""";
	private const string ThinkingRefused = """{"error":{"type":"invalid_request_error","message":"adaptive thinking is not supported on this model"}}""";
	private const string EffortRefused = """{"error":{"type":"invalid_request_error","message":"output_config.effort: 'max' is not supported for this model"}}""";
	private const string SpeedRefused = """{"error":{"type":"invalid_request_error","message":"speed is not supported for model claude-opus-5"}}""";
	private const int UserWaitSeconds = 5;

	private readonly List<HttpClient> _clients = new();

	public void Dispose()
	{
		foreach (var client in _clients)
			client.Dispose();
	}

	private AnthropicLlmService CreateService(
		FakeHttpMessageHandler handler,
		IEnumerable<ReasoningLevel>? ticked = null,
		int retryCount = 0,
		TimeProvider? clock = null)
	{
		var client = new HttpClient(handler);
		_clients.Add(client);

		var model = new LlmModelConfig(Model, LlmProvider.Anthropic, customTemperature: 0.3m)
		{
			ReasoningLevels = (ticked ?? ReasoningLevelCatalog.FullList(LlmProvider.Anthropic)).ToList(),
		};

		return new AnthropicLlmService(
			"test-key",
			new[] { model },
			client,
			timeoutSeconds: UserWaitSeconds,
			retryCount: retryCount,
			timeProvider: clock);
	}

	private static IList<LlmChatMessage> Messages() =>
		new List<LlmChatMessage>
		{
			new(LlmChatRole.System, "be brief"),
			new(LlmChatRole.User, "hello"),
		};

	private static JsonElement Root(FakeHttpMessageHandler.CapturedRequest request) =>
		JsonDocument.Parse(request.Body).RootElement.Clone();

	private static string? ThinkingType(JsonElement root) =>
		root.TryGetProperty("thinking", out var thinking) ? thinking.GetProperty("type").GetString() : null;

	private static string? Effort(JsonElement root) =>
		root.TryGetProperty("output_config", out var config) ? config.GetProperty("effort").GetString() : null;

	private static double? Temperature(JsonElement root) =>
		root.TryGetProperty("temperature", out var t) ? t.GetDouble() : null;

	private static int MaxTokens(JsonElement root) => root.GetProperty("max_tokens").GetInt32();

	private static string? Speed(JsonElement root) =>
		root.TryGetProperty("speed", out var s) ? s.GetString() : null;

	[Theory]
	[InlineData(ReasoningLevel.Low, "low", 8_000)]
	[InlineData(ReasoningLevel.Medium, "medium", 12_000)]
	[InlineData(ReasoningLevel.High, "high", 16_000)]
	[InlineData(ReasoningLevel.ExtraHigh, "xhigh", 16_000)]
	[InlineData(ReasoningLevel.Max, "max", 16_000)]
	public async Task ThinkingLevel_SendsAdaptiveThinkingAndEffortWithoutTemperature(
		ReasoningLevel level, string effort, int maxTokens)
	{
		var handler = new FakeHttpMessageHandler().Respond(HttpStatusCode.OK, SuccessBody);

		string result = await CreateService(handler).CreateChatCompletion(
			Messages(), Model, new LlmRequestOptions { ReasoningLevel = level });

		Assert.Equal("done", result);
		var root = Root(Assert.Single(handler.Requests));
		Assert.Equal("adaptive", ThinkingType(root));
		Assert.Equal(effort, Effort(root));
		Assert.Null(Temperature(root));
		Assert.Equal(maxTokens, MaxTokens(root));
	}

	[Fact]
	public async Task Off_TurnsThinkingOffAndKeepsTheTemperature()
	{
		var handler = new FakeHttpMessageHandler().Respond(HttpStatusCode.OK, SuccessBody);

		await CreateService(handler).CreateChatCompletion(
			Messages(), Model, new LlmRequestOptions { ReasoningLevel = ReasoningLevel.Off });

		var root = Root(Assert.Single(handler.Requests));
		Assert.Equal("disabled", ThinkingType(root));
		Assert.Null(Effort(root));
		Assert.Equal(0.3, Temperature(root)!.Value, precision: 6);
		Assert.Equal(8_000, MaxTokens(root));
	}

	[Fact]
	public async Task NoLevel_SendsNoReasoningFieldsAndKeepsTheTemperature()
	{
		var handler = new FakeHttpMessageHandler().Respond(HttpStatusCode.OK, SuccessBody);

		await CreateService(handler).CreateChatCompletion(Messages(), Model, new LlmRequestOptions());

		var request = Assert.Single(handler.Requests);
		var root = Root(request);
		Assert.False(root.TryGetProperty("thinking", out _));
		Assert.False(root.TryGetProperty("output_config", out _));
		Assert.Equal(0.3, Temperature(root)!.Value, precision: 6);
		Assert.Equal(8_000, MaxTokens(root));
		Assert.False(request.HasHeader("anthropic-beta"));
	}

	[Fact]
	public async Task LevelNotTickedForTheModel_SendsTheLightestTickedLevel()
	{
		var handler = new FakeHttpMessageHandler().Respond(HttpStatusCode.OK, SuccessBody);
		var service = CreateService(handler, ticked: [ReasoningLevel.Off, ReasoningLevel.Medium, ReasoningLevel.High]);

		await service.CreateChatCompletion(
			Messages(), Model, new LlmRequestOptions { ReasoningLevel = ReasoningLevel.Max });

		Assert.Equal("medium", Effort(Root(Assert.Single(handler.Requests))));
	}

	[Fact]
	public async Task ModelWithNothingTicked_SendsNoLevel()
	{
		var handler = new FakeHttpMessageHandler().Respond(HttpStatusCode.OK, SuccessBody);
		var service = CreateService(handler, ticked: []);

		await service.CreateChatCompletion(
			Messages(), Model, new LlmRequestOptions { ReasoningLevel = ReasoningLevel.High });

		var root = Root(Assert.Single(handler.Requests));
		Assert.Null(ThinkingType(root));
		Assert.Null(Effort(root));
		Assert.NotNull(Temperature(root));
	}

	[Theory]
	[InlineData(ThinkingRefused)]
	[InlineData(EffortRefused)]
	public async Task LevelRefused_RetriesOnceWithNoLevelAndReportsTheRefusedLevel(string errorBody)
	{
		var handler = new FakeHttpMessageHandler()
			.Respond(HttpStatusCode.BadRequest, errorBody)
			.Respond(HttpStatusCode.OK, SuccessBody);
		var options = new List<ReasoningLevel>();

		string result = await CreateService(handler).CreateChatCompletion(
			Messages(),
			Model,
			new LlmRequestOptions
			{
				ReasoningLevel = ReasoningLevel.Max,
				OnReasoningFallback = level => options.Add(level),
			});

		Assert.Equal("done", result);
		Assert.Equal(2, handler.Requests.Count);

		var first = Root(handler.Requests[0]);
		Assert.Equal("max", Effort(first));

		// The retry is the request with no level: no reasoning fields, the model's
		// temperature back, and the lightest limits.
		var retry = Root(handler.Requests[1]);
		Assert.Null(ThinkingType(retry));
		Assert.Null(Effort(retry));
		Assert.Equal(0.3, Temperature(retry)!.Value, precision: 6);
		Assert.Equal(8_000, MaxTokens(retry));

		Assert.Equal([ReasoningLevel.Max], options);
	}

	[Fact]
	public async Task LevelRefusedAndRetryFails_ReportsTheFailureAndNoFallback()
	{
		var handler = new FakeHttpMessageHandler()
			.Respond(HttpStatusCode.BadRequest, ThinkingRefused)
			.Respond(HttpStatusCode.BadRequest, """{"error":{"type":"invalid_request_error","message":"messages: text content blocks must be non-empty"}}""");
		bool reported = false;

		var ex = await Assert.ThrowsAsync<NonTransientLlmException>(() =>
			CreateService(handler).CreateChatCompletion(
				Messages(),
				Model,
				new LlmRequestOptions { ReasoningLevel = ReasoningLevel.High, OnReasoningFallback = _ => reported = true }));

		Assert.Contains("non-empty", ex.Message);
		Assert.False(reported);
		Assert.Equal(2, handler.Requests.Count);
	}

	[Fact]
	public async Task RetryWithNoLevelIsRefusedForTheSameReason_IsNotRetriedAgain()
	{
		var handler = new FakeHttpMessageHandler()
			.Respond(HttpStatusCode.BadRequest, ThinkingRefused)
			.Respond(HttpStatusCode.BadRequest, ThinkingRefused);
		bool reported = false;

		await Assert.ThrowsAsync<NonTransientLlmException>(() =>
			CreateService(handler).CreateChatCompletion(
				Messages(),
				Model,
				new LlmRequestOptions { ReasoningLevel = ReasoningLevel.High, OnReasoningFallback = _ => reported = true }));

		Assert.False(reported);
		Assert.Equal(2, handler.Requests.Count);
	}

	[Theory]
	[InlineData(HttpStatusCode.Unauthorized, """{"error":{"type":"authentication_error","message":"invalid x-api-key"}}""")]
	[InlineData(HttpStatusCode.BadRequest, """{"error":{"type":"invalid_request_error","message":"max_tokens must be greater than 0"}}""")]
	[InlineData(HttpStatusCode.NotFound, """{"error":{"type":"not_found_error","message":"model: claude-opus-5"}}""")]
	public async Task OtherClientErrors_AreReportedAsTheyAreWithoutARetry(HttpStatusCode status, string errorBody)
	{
		var handler = new FakeHttpMessageHandler().Respond(status, errorBody);
		bool reported = false;

		var ex = await Assert.ThrowsAsync<NonTransientLlmException>(() =>
			CreateService(handler).CreateChatCompletion(
				Messages(),
				Model,
				new LlmRequestOptions { ReasoningLevel = ReasoningLevel.High, OnReasoningFallback = _ => reported = true }));

		Assert.Equal((int)status, ex.StatusCode);
		Assert.False(reported);
		Assert.Single(handler.Requests);
	}

	[Fact]
	public async Task ServerError_IsRetriedByTheNormalPolicyAndNotAsAReasoningFallback()
	{
		var handler = new FakeHttpMessageHandler()
			.Respond(HttpStatusCode.InternalServerError, """{"error":{"type":"api_error","message":"thinking took too long"}}""")
			.Respond(HttpStatusCode.InternalServerError, """{"error":{"type":"api_error","message":"thinking took too long"}}""");
		bool reported = false;

		await Assert.ThrowsAsync<HttpRequestException>(() =>
			CreateService(handler, retryCount: 1, clock: new RecordingClock()).CreateChatCompletion(
				Messages(),
				Model,
				new LlmRequestOptions { ReasoningLevel = ReasoningLevel.High, OnReasoningFallback = _ => reported = true }));

		Assert.False(reported);
		Assert.Equal(2, handler.Requests.Count);
		// Both tries kept the level: a server error is not the level's fault.
		Assert.All(handler.Requests, r => Assert.Equal("high", Effort(Root(r))));
	}

	[Fact]
	public async Task High_WaitsThreeTimesTheUserWaitPerTry()
	{
		var clock = new RecordingClock();
		var handler = new FakeHttpMessageHandler()
			.Respond(HttpStatusCode.ServiceUnavailable, "{}")
			.Respond(HttpStatusCode.OK, SuccessBody);

		await CreateService(handler, retryCount: 1, clock: clock).CreateChatCompletion(
			Messages(), Model, new LlmRequestOptions { ReasoningLevel = ReasoningLevel.High });

		// Three times the user's 5 seconds, and the second try still grows from there.
		Assert.Equal([15.0, 30.0], clock.DeadlineSeconds);
	}

	[Fact]
	public async Task NoLevel_WaitsTheUserWaitPerTry()
	{
		var clock = new RecordingClock();
		var handler = new FakeHttpMessageHandler().Respond(HttpStatusCode.OK, SuccessBody);

		await CreateService(handler, clock: clock).CreateChatCompletion(Messages(), Model);

		Assert.Equal([5.0], clock.DeadlineSeconds);
	}

	[Fact]
	public async Task RetryWithNoLevel_GoesBackToTheUserWait()
	{
		var clock = new RecordingClock();
		var handler = new FakeHttpMessageHandler()
			.Respond(HttpStatusCode.BadRequest, ThinkingRefused)
			.Respond(HttpStatusCode.OK, SuccessBody);

		await CreateService(handler, clock: clock).CreateChatCompletion(
			Messages(), Model, new LlmRequestOptions { ReasoningLevel = ReasoningLevel.ExtraHigh });

		Assert.Equal([20.0, 5.0], clock.DeadlineSeconds);
	}

	[Fact]
	public async Task FastModeAndLevel_LevelRefused_RetriesWithoutTheLevelButStillInFastMode()
	{
		var handler = new FakeHttpMessageHandler()
			.Respond(HttpStatusCode.BadRequest, EffortRefused)
			.Respond(HttpStatusCode.OK, SuccessBody);
		ReasoningLevel? refused = null;
		FastModeFallback? fastFallback = null;

		await CreateService(handler).CreateChatCompletion(
			Messages(),
			Model,
			new LlmRequestOptions
			{
				FastMode = true,
				ReasoningLevel = ReasoningLevel.Max,
				OnReasoningFallback = l => refused = l,
				OnFastModeFallback = f => fastFallback = f,
			});

		Assert.Equal(2, handler.Requests.Count);
		var first = Root(handler.Requests[0]);
		Assert.Equal("fast", Speed(first));
		Assert.Equal("max", Effort(first));

		var retry = Root(handler.Requests[1]);
		Assert.Equal("fast", Speed(retry));
		Assert.True(handler.Requests[1].HasHeader("anthropic-beta"));
		Assert.Null(Effort(retry));

		Assert.Equal(ReasoningLevel.Max, refused);
		Assert.Null(fastFallback);
	}

	[Fact]
	public async Task FastModeAndLevel_FastModeRefused_RetriesAtStandardSpeedButKeepsTheLevel()
	{
		var handler = new FakeHttpMessageHandler()
			.Respond(HttpStatusCode.BadRequest, SpeedRefused)
			.Respond(HttpStatusCode.OK, SuccessBody);
		ReasoningLevel? refused = null;
		FastModeFallback? fastFallback = null;

		await CreateService(handler).CreateChatCompletion(
			Messages(),
			Model,
			new LlmRequestOptions
			{
				FastMode = true,
				ReasoningLevel = ReasoningLevel.High,
				OnReasoningFallback = l => refused = l,
				OnFastModeFallback = f => fastFallback = f,
			});

		Assert.Equal(2, handler.Requests.Count);
		var retry = Root(handler.Requests[1]);
		Assert.Null(Speed(retry));
		Assert.False(handler.Requests[1].HasHeader("anthropic-beta"));
		Assert.Equal("high", Effort(retry));

		Assert.Null(refused);
		Assert.Equal(FastModeFallbackReason.UnsupportedModel, fastFallback!.Reason);
	}

	[Fact]
	public async Task FastModeAndLevel_BothRefusedInTurn_DropsEachOnceAndReportsBoth()
	{
		var handler = new FakeHttpMessageHandler()
			.Respond(HttpStatusCode.BadRequest, SpeedRefused)
			.Respond(HttpStatusCode.BadRequest, ThinkingRefused)
			.Respond(HttpStatusCode.OK, SuccessBody);
		ReasoningLevel? refused = null;
		FastModeFallback? fastFallback = null;

		string result = await CreateService(handler).CreateChatCompletion(
			Messages(),
			Model,
			new LlmRequestOptions
			{
				FastMode = true,
				ReasoningLevel = ReasoningLevel.Low,
				OnReasoningFallback = l => refused = l,
				OnFastModeFallback = f => fastFallback = f,
			});

		Assert.Equal("done", result);
		Assert.Equal(3, handler.Requests.Count);
		var last = Root(handler.Requests[2]);
		Assert.Null(Speed(last));
		Assert.Null(Effort(last));
		Assert.Equal(ReasoningLevel.Low, refused);
		Assert.NotNull(fastFallback);
	}

	[Fact]
	public async Task FastModeAndLevel_ForbiddenNamingNeither_IsTakenAsTheFastModeGate()
	{
		var handler = new FakeHttpMessageHandler()
			.Respond(HttpStatusCode.Forbidden, """{"error":{"type":"permission_error","message":"Your account does not have access to this feature."}}""")
			.Respond(HttpStatusCode.OK, SuccessBody);
		ReasoningLevel? refused = null;
		FastModeFallback? fastFallback = null;

		await CreateService(handler).CreateChatCompletion(
			Messages(),
			Model,
			new LlmRequestOptions
			{
				FastMode = true,
				ReasoningLevel = ReasoningLevel.High,
				OnReasoningFallback = l => refused = l,
				OnFastModeFallback = f => fastFallback = f,
			});

		Assert.Equal("high", Effort(Root(handler.Requests[1])));
		Assert.Null(refused);
		Assert.Equal(FastModeFallbackReason.Unavailable, fastFallback!.Reason);
	}

	[Fact]
	public async Task CutShortAtAHeavyLevel_StillReturnsTheTextAndTellsTheCaller()
	{
		var handler = new FakeHttpMessageHandler().Respond(HttpStatusCode.OK,
			"""{"content":[{"type":"thinking","thinking":"..."},{"type":"text","text":"partial"}],"stop_reason":"max_tokens"}""");
		bool cutShort = false;

		string result = await CreateService(handler).CreateChatCompletion(
			Messages(),
			Model,
			new LlmRequestOptions { ReasoningLevel = ReasoningLevel.Max, OnCutShort = () => cutShort = true });

		Assert.Equal("partial", result);
		Assert.True(cutShort);
	}
}
