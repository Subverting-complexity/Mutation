using System.ClientModel;
using System.ClientModel.Primitives;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using CognitiveSupport;

namespace Mutation.Tests;

/// <summary>
/// The reasoning level on an OpenAI request: the reasoning effort each level sends, the
/// wait it gets, and the one retry without a level when the model refuses it. Built like
/// <see cref="AnthropicReasoningTests"/>, so the two services can be read side by side.
/// </summary>
public class OpenAiReasoningTests : IDisposable
{
	private const string Model = "gpt-5.6";
	private const string SuccessBody =
		"""{"id":"c1","object":"chat.completion","created":1,"model":"gpt-5.6","choices":[{"index":0,"finish_reason":"stop","message":{"role":"assistant","content":"done"}}]}""";
	private const string EffortRefused =
		"""{"error":{"message":"Unsupported value: 'reasoning_effort' does not support 'xhigh' with this model. Supported values are: 'low', 'medium', and 'high'.","type":"invalid_request_error","param":"reasoning_effort","code":"unsupported_value"}}""";
	private const string ReasoningRefused =
		"""{"error":{"message":"This model does not support reasoning.","type":"invalid_request_error","param":null,"code":null}}""";
	private const string TierRefused =
		"""{"error":{"message":"Invalid value: 'fast'. Supported values are: 'auto', 'default', 'flex' and 'priority'.","type":"invalid_request_error","param":"service_tier"}}""";
	private const int UserWaitSeconds = 5;

	private readonly List<HttpClient> _clients = new();

	public void Dispose()
	{
		foreach (var client in _clients)
			client.Dispose();
	}

	private LlmService CreateService(
		HttpMessageHandler handler,
		IEnumerable<ReasoningLevel>? ticked = null,
		int retryCount = 0,
		TimeProvider? clock = null)
	{
		var client = new HttpClient(handler);
		_clients.Add(client);

		var model = new LlmModelConfig(Model, LlmProvider.OpenAI, customTemperature: 0.3m)
		{
			ReasoningLevels = (ticked ?? ReasoningLevelCatalog.FullList(LlmProvider.OpenAI)).ToList(),
		};

		return new LlmService(
			"test-key",
			new[] { model },
			timeoutSeconds: UserWaitSeconds,
			retryCount: retryCount,
			transport: new HttpClientPipelineTransport(client),
			timeProvider: clock);
	}

	private static IList<LlmChatMessage> Messages() =>
		new List<LlmChatMessage>
		{
			new(LlmChatRole.System, "be brief"),
			new(LlmChatRole.User, "hello"),
		};

	private static JsonElement Root(string body) => JsonDocument.Parse(body).RootElement.Clone();

	private static JsonElement Root(FakeHttpMessageHandler.CapturedRequest request) => Root(request.Body);

	private static string? Effort(JsonElement root) =>
		root.TryGetProperty("reasoning_effort", out var e) ? e.GetString() : null;

	private static double? Temperature(JsonElement root) =>
		root.TryGetProperty("temperature", out var t) ? t.GetDouble() : null;

	private static string? Tier(JsonElement root) =>
		root.TryGetProperty("service_tier", out var s) ? s.GetString() : null;

	// ----- The mapping, as one table. -----

	[Theory]
	[InlineData(ReasoningLevel.None, "none")]
	[InlineData(ReasoningLevel.Minimal, "minimal")]
	[InlineData(ReasoningLevel.Low, "low")]
	[InlineData(ReasoningLevel.Medium, "medium")]
	[InlineData(ReasoningLevel.High, "high")]
	[InlineData(ReasoningLevel.ExtraHigh, "xhigh")]
	public void Effort_MapsEachOpenAiLevelToItsValue(ReasoningLevel level, string expected)
	{
#pragma warning disable OPENAI001 // Evaluation-only SDK surface; see OpenAiReasoningFields.
		Assert.Equal(expected, OpenAiReasoningFields.Effort(level)!.Value.ToString());
#pragma warning restore OPENAI001
	}

	[Theory]
	[InlineData(null)]
	[InlineData(ReasoningLevel.Off)]
	[InlineData(ReasoningLevel.Max)]
	public void Effort_IsNullForNoLevelAndForAnthropicLevels(ReasoningLevel? level)
	{
		Assert.Null(OpenAiReasoningFields.Effort(level));
		Assert.True(OpenAiReasoningFields.SendsTemperature(level));
	}

	// ----- On the wire. -----

	[Theory]
	[InlineData(ReasoningLevel.Minimal, "minimal")]
	[InlineData(ReasoningLevel.Low, "low")]
	[InlineData(ReasoningLevel.Medium, "medium")]
	[InlineData(ReasoningLevel.High, "high")]
	[InlineData(ReasoningLevel.ExtraHigh, "xhigh")]
	public async Task ReasoningLevel_SendsTheEffortWithoutTemperature(ReasoningLevel level, string effort)
	{
		var handler = new FakeHttpMessageHandler().Respond(HttpStatusCode.OK, SuccessBody);

		string result = await CreateService(handler).CreateChatCompletion(
			Messages(), Model, new LlmRequestOptions { ReasoningLevel = level });

		Assert.Equal("done", result);
		var root = Root(Assert.Single(handler.Requests));
		Assert.Equal(effort, Effort(root));
		Assert.Null(Temperature(root));
		// The OpenAI answer limit is not changed by a level.
		Assert.False(root.TryGetProperty("max_completion_tokens", out _));
		Assert.False(root.TryGetProperty("max_tokens", out _));
	}

	[Fact]
	public async Task None_SendsEffortNoneAndKeepsTheTemperature()
	{
		var handler = new FakeHttpMessageHandler().Respond(HttpStatusCode.OK, SuccessBody);

		await CreateService(handler).CreateChatCompletion(
			Messages(), Model, new LlmRequestOptions { ReasoningLevel = ReasoningLevel.None });

		var root = Root(Assert.Single(handler.Requests));
		Assert.Equal("none", Effort(root));
		Assert.Equal(0.3, Temperature(root)!.Value, precision: 6);
	}

	[Fact]
	public async Task NoLevel_SendsNoEffortAndKeepsTheTemperature()
	{
		var handler = new FakeHttpMessageHandler().Respond(HttpStatusCode.OK, SuccessBody);

		await CreateService(handler).CreateChatCompletion(Messages(), Model, new LlmRequestOptions());

		var root = Root(Assert.Single(handler.Requests));
		Assert.False(root.TryGetProperty("reasoning_effort", out _));
		Assert.Equal(0.3, Temperature(root)!.Value, precision: 6);
	}

	[Fact]
	public async Task LevelNotTickedForTheModel_SendsTheLightestTickedLevel()
	{
		var handler = new FakeHttpMessageHandler().Respond(HttpStatusCode.OK, SuccessBody);
		var service = CreateService(handler, ticked: [ReasoningLevel.None, ReasoningLevel.Low, ReasoningLevel.High]);

		await service.CreateChatCompletion(
			Messages(), Model, new LlmRequestOptions { ReasoningLevel = ReasoningLevel.ExtraHigh });

		Assert.Equal("low", Effort(Root(Assert.Single(handler.Requests))));
	}

	[Fact]
	public async Task ModelWithNothingTicked_SendsNoLevel()
	{
		var handler = new FakeHttpMessageHandler().Respond(HttpStatusCode.OK, SuccessBody);
		var service = CreateService(handler, ticked: []);

		await service.CreateChatCompletion(
			Messages(), Model, new LlmRequestOptions { ReasoningLevel = ReasoningLevel.High });

		var root = Root(Assert.Single(handler.Requests));
		Assert.Null(Effort(root));
		Assert.NotNull(Temperature(root));
	}

	// ----- Rejection and the one retry. -----

	[Theory]
	[InlineData(EffortRefused)]
	[InlineData(ReasoningRefused)]
	public async Task LevelRefused_RetriesOnceWithNoLevelAndReportsTheRefusedLevel(string errorBody)
	{
		var handler = new FakeHttpMessageHandler()
			.Respond(HttpStatusCode.BadRequest, errorBody)
			.Respond(HttpStatusCode.OK, SuccessBody);

		var reported = new List<ReasoningLevel>();
		string result = await CreateService(handler).CreateChatCompletion(
			Messages(),
			Model,
			new LlmRequestOptions { ReasoningLevel = ReasoningLevel.ExtraHigh, OnReasoningFallback = reported.Add });

		Assert.Equal("done", result);
		Assert.Equal(2, handler.Requests.Count);
		Assert.Equal("xhigh", Effort(Root(handler.Requests[0])));
		Assert.Null(Temperature(Root(handler.Requests[0])));

		// The retry sends no effort and no temperature: a reasoning model still reasons at
		// its default with no effort, and would refuse the temperature (issue #438).
		var retry = Root(handler.Requests[1]);
		Assert.Null(Effort(retry));
		Assert.Null(Temperature(retry));
		Assert.Equal([ReasoningLevel.ExtraHigh], reported);
	}

	[Fact]
	public async Task FastModeRefusedWithNoLevel_RetryKeepsTheTemperature()
	{
		var handler = new FakeHttpMessageHandler()
			.Respond(HttpStatusCode.BadRequest, TierRefused)
			.Respond(HttpStatusCode.OK, SuccessBody);

		string result = await CreateService(handler).CreateChatCompletion(
			Messages(), Model, new LlmRequestOptions { FastMode = true });

		Assert.Equal("done", result);
		Assert.Equal(2, handler.Requests.Count);
		var retry = Root(handler.Requests[1]);
		Assert.Null(Tier(retry));
		Assert.Equal(0.3, Temperature(retry)!.Value, precision: 6);
	}

	[Fact]
	public async Task LevelRefusedAndRetryFails_ReportsTheFailureAndNoFallback()
	{
		var handler = new FakeHttpMessageHandler()
			.Respond(HttpStatusCode.BadRequest, EffortRefused)
			.Respond(HttpStatusCode.Unauthorized, """{"error":{"message":"Incorrect API key provided","type":"invalid_request_error"}}""");

		bool reported = false;
		await Assert.ThrowsAsync<ClientResultException>(() =>
			CreateService(handler).CreateChatCompletion(
				Messages(),
				Model,
				new LlmRequestOptions { ReasoningLevel = ReasoningLevel.High, OnReasoningFallback = _ => reported = true }));

		Assert.False(reported);
		Assert.Equal(2, handler.Requests.Count);
	}

	[Fact]
	public async Task RetryWithNoLevelIsRefusedForTheSameReason_IsNotRetriedAgain()
	{
		// The second refusal still names reasoning, but there is no level left to drop.
		var handler = new FakeHttpMessageHandler().Respond(HttpStatusCode.BadRequest, EffortRefused);

		bool reported = false;
		await Assert.ThrowsAsync<ClientResultException>(() =>
			CreateService(handler).CreateChatCompletion(
				Messages(),
				Model,
				new LlmRequestOptions { ReasoningLevel = ReasoningLevel.High, OnReasoningFallback = _ => reported = true }));

		Assert.False(reported);
		Assert.Equal(2, handler.Requests.Count);
	}

	[Theory]
	[InlineData(HttpStatusCode.Unauthorized, """{"error":{"message":"Incorrect API key provided","type":"invalid_request_error"}}""")]
	[InlineData(HttpStatusCode.BadRequest, """{"error":{"message":"max_tokens must be greater than 0","type":"invalid_request_error"}}""")]
	[InlineData(HttpStatusCode.NotFound, """{"error":{"message":"The model 'gpt-5.6' does not exist","type":"invalid_request_error"}}""")]
	public async Task OtherClientErrors_AreReportedAsTheyAreWithoutARetry(HttpStatusCode status, string errorBody)
	{
		var handler = new FakeHttpMessageHandler().Respond(status, errorBody);

		bool reported = false;
		await Assert.ThrowsAsync<ClientResultException>(() =>
			CreateService(handler).CreateChatCompletion(
				Messages(),
				Model,
				new LlmRequestOptions { ReasoningLevel = ReasoningLevel.High, OnReasoningFallback = _ => reported = true }));

		Assert.False(reported);
		Assert.Single(handler.Requests);
	}

	[Fact]
	public async Task ServerError_IsRetriedByTheNormalPolicyAndNotAsAReasoningFallback()
	{
		// The text names reasoning, but a 5xx is the retry policy's business.
		var handler = new FakeHttpMessageHandler()
			.Respond(HttpStatusCode.InternalServerError, """{"error":{"message":"reasoning backend overloaded","type":"server_error"}}""");

		bool reported = false;
		await Assert.ThrowsAsync<ClientResultException>(() =>
			CreateService(handler, retryCount: 1, clock: new RecordingClock()).CreateChatCompletion(
				Messages(),
				Model,
				new LlmRequestOptions { ReasoningLevel = ReasoningLevel.High, OnReasoningFallback = _ => reported = true }));

		Assert.False(reported);
		Assert.Equal(2, handler.Requests.Count);
		// Both tries kept the level: a server error is not the level's fault.
		Assert.All(handler.Requests, r => Assert.Equal("high", Effort(Root(r))));
	}

	// ----- Wait time. -----

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
			.Respond(HttpStatusCode.BadRequest, EffortRefused)
			.Respond(HttpStatusCode.OK, SuccessBody);

		await CreateService(handler, clock: clock).CreateChatCompletion(
			Messages(), Model, new LlmRequestOptions { ReasoningLevel = ReasoningLevel.ExtraHigh });

		Assert.Equal([20.0, 5.0], clock.DeadlineSeconds);
	}

	[Fact]
	public async Task TimeoutAtHigh_EachTryWaitsItsStretchedDeadlineAndKeepsTheLevel()
	{
		// The service never answers, so every try runs out its deadline. A timeout is not
		// the level's fault: both tries keep the level, nothing is reported as a fallback,
		// and the timeout reaches the caller as it does today.
		var clock = new ExpiringClock();
		var handler = new NeverAnswersHandler();

		bool reported = false;
		await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
			CreateService(handler, retryCount: 1, clock: clock).CreateChatCompletion(
				Messages(),
				Model,
				new LlmRequestOptions { ReasoningLevel = ReasoningLevel.High, OnReasoningFallback = _ => reported = true }));

		Assert.False(reported);
		Assert.Equal([15.0, 30.0], clock.DeadlineSeconds);
		Assert.Equal(2, handler.Bodies.Count);
		Assert.All(handler.Bodies, b => Assert.Equal("high", Effort(Root(b))));
	}

	// ----- Fast mode and a level together. -----

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
				ReasoningLevel = ReasoningLevel.High,
				FastMode = true,
				OnReasoningFallback = l => refused = l,
				OnFastModeFallback = f => fastFallback = f,
			});

		Assert.Equal(2, handler.Requests.Count);
		var retry = Root(handler.Requests[1]);
		Assert.Null(Effort(retry));
		Assert.Equal("fast", Tier(retry));
		Assert.Equal(ReasoningLevel.High, refused);
		Assert.Null(fastFallback);
	}

	[Fact]
	public async Task FastModeAndLevel_TierRefused_RetriesAtStandardSpeedButKeepsTheLevel()
	{
		var handler = new FakeHttpMessageHandler()
			.Respond(HttpStatusCode.BadRequest, TierRefused)
			.Respond(HttpStatusCode.OK, SuccessBody);

		ReasoningLevel? refused = null;
		FastModeFallback? fastFallback = null;
		await CreateService(handler).CreateChatCompletion(
			Messages(),
			Model,
			new LlmRequestOptions
			{
				ReasoningLevel = ReasoningLevel.High,
				FastMode = true,
				OnReasoningFallback = l => refused = l,
				OnFastModeFallback = f => fastFallback = f,
			});

		Assert.Equal(2, handler.Requests.Count);
		var retry = Root(handler.Requests[1]);
		Assert.Equal("high", Effort(retry));
		Assert.Null(Tier(retry));
		Assert.Null(refused);
		Assert.Equal(FastModeFallbackReason.UnsupportedModel, fastFallback!.Reason);
	}

	[Fact]
	public async Task FastModeRefusedThenLevelRefused_LastSendHasNoTemperature()
	{
		var handler = new FakeHttpMessageHandler()
			.Respond(HttpStatusCode.BadRequest, TierRefused)
			.Respond(HttpStatusCode.BadRequest, EffortRefused)
			.Respond(HttpStatusCode.OK, SuccessBody);

		string result = await CreateService(handler).CreateChatCompletion(
			Messages(),
			Model,
			new LlmRequestOptions { ReasoningLevel = ReasoningLevel.Medium, FastMode = true });

		Assert.Equal("done", result);
		Assert.Equal(3, handler.Requests.Count);
		var last = Root(handler.Requests[2]);
		Assert.Null(Effort(last));
		Assert.Null(Tier(last));
		Assert.Null(Temperature(last));
	}

	[Fact]
	public async Task FastModeAndLevel_BothRefusedInTurn_DropsEachOnceAndReportsBoth()
	{
		var handler = new FakeHttpMessageHandler()
			.Respond(HttpStatusCode.BadRequest, EffortRefused)
			.Respond(HttpStatusCode.BadRequest, TierRefused)
			.Respond(HttpStatusCode.OK, SuccessBody);

		ReasoningLevel? refused = null;
		FastModeFallback? fastFallback = null;
		string result = await CreateService(handler).CreateChatCompletion(
			Messages(),
			Model,
			new LlmRequestOptions
			{
				ReasoningLevel = ReasoningLevel.Medium,
				FastMode = true,
				OnReasoningFallback = l => refused = l,
				OnFastModeFallback = f => fastFallback = f,
			});

		Assert.Equal("done", result);
		Assert.Equal(3, handler.Requests.Count);
		var last = Root(handler.Requests[2]);
		Assert.Null(Effort(last));
		Assert.Null(Tier(last));
		Assert.Null(Temperature(last));
		Assert.Equal(ReasoningLevel.Medium, refused);
		Assert.NotNull(fastFallback);
	}

	[Fact]
	public async Task FastModeAndLevel_MessageNamingBoth_DropsOnlyTheTier()
	{
		// The message names the service tier, so it is taken at its word even though it
		// also mentions reasoning; the level stays on the retry.
		var handler = new FakeHttpMessageHandler()
			.Respond(HttpStatusCode.BadRequest, """{"error":{"message":"service_tier 'fast' is not supported together with reasoning on this model","type":"invalid_request_error"}}""")
			.Respond(HttpStatusCode.OK, SuccessBody);

		ReasoningLevel? refused = null;
		FastModeFallback? fastFallback = null;
		await CreateService(handler).CreateChatCompletion(
			Messages(),
			Model,
			new LlmRequestOptions
			{
				ReasoningLevel = ReasoningLevel.High,
				FastMode = true,
				OnReasoningFallback = l => refused = l,
				OnFastModeFallback = f => fastFallback = f,
			});

		var retry = Root(handler.Requests[1]);
		Assert.Equal("high", Effort(retry));
		Assert.Null(Tier(retry));
		Assert.Null(refused);
		Assert.NotNull(fastFallback);
	}

	/// <summary>
	/// Never replies: waits until the try's deadline cancels it, writing down each body it
	/// was sent so a test can check what every try carried.
	/// </summary>
	private sealed class NeverAnswersHandler : HttpMessageHandler
	{
		private readonly List<string> _bodies = [];

		internal IReadOnlyList<string> Bodies
		{
			get { lock (_bodies) return _bodies.ToArray(); }
		}

		protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
		{
			string body = request.Content is null
				? string.Empty
				: await request.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
			lock (_bodies) _bodies.Add(body);

			await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken).ConfigureAwait(false);
			throw new InvalidOperationException("unreachable");
		}
	}

	/// <summary>
	/// Like <see cref="RecordingClock"/>, but springs each per-try deadline at once instead
	/// of leaving it to a real timer, so a timeout test does not sit through 45 seconds.
	/// It still writes down the length each deadline was armed with.
	/// </summary>
	private sealed class ExpiringClock : TimeProvider
	{
		private readonly List<TimeSpan> _deadlines = [];

		internal IReadOnlyList<double> DeadlineSeconds
		{
			get { lock (_deadlines) return _deadlines.Select(d => d.TotalSeconds).ToArray(); }
		}

		public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
		{
			if (dueTime < TimeSpan.Zero)
				return base.CreateTimer(callback, state, dueTime, period);

			if (state is CancellationTokenSource)
			{
				lock (_deadlines) _deadlines.Add(dueTime);
				// A short real delay rather than zero, so the request reaches the handler
				// before its deadline fires.
				return base.CreateTimer(callback, state, TimeSpan.FromMilliseconds(200), period);
			}

			return base.CreateTimer(callback, state, TimeSpan.Zero, period);
		}
	}
}
