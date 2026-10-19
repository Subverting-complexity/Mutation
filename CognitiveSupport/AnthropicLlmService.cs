using Polly;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace CognitiveSupport;

public class AnthropicLlmService : ILlmService
{
	private const string ApiUrl = "https://api.anthropic.com/v1/messages";
	// Still the current Messages API version: newer features such as thinking and effort
	// are switched on through request fields and beta headers, not a new version date.
	private const string AnthropicVersion = "2023-06-01";

	/// <summary>Opt-in header for the Fast mode research preview.</summary>
	internal const string FastModeBeta = "fast-mode-2026-02-01";

	/// <summary>Value of the request body's <c>speed</c> parameter when Fast mode is on.</summary>
	internal const string FastSpeed = "fast";

	private readonly string _apiKey;
	private readonly HttpClient _httpClient;
	private readonly Dictionary<string, LlmModelConfig> _modelConfigs;
	private readonly int _timeoutSeconds;
	private readonly ResiliencePipeline _retryPipeline;
	private readonly TimeProvider _timeProvider;
	private readonly Action<string> _requestLog;

	/// <param name="timeProvider">
	/// Test seam only; null in production. Drives both the retry backoff and each
	/// attempt's deadline, so a test can read back the waits that were armed instead of
	/// sitting through them.
	/// </param>
	/// <param name="requestLog">
	/// Test seam only; null in production, which writes the request log lines to
	/// <see cref="ErrorLogger"/>. Lets a test read back the lines for its own request
	/// rather than search a log file other tests write to at the same time.
	/// </param>
	public AnthropicLlmService(
		string apiKey,
		IEnumerable<LlmModelConfig> models,
		HttpClient httpClient,
		int timeoutSeconds = 60,
		int retryCount = 3,
		TimeProvider? timeProvider = null,
		Action<string>? requestLog = null)
	{
		if (string.IsNullOrEmpty(apiKey)) throw new ArgumentNullException(nameof(apiKey));

		var modelList = LlmModelIndex.Validate(models, nameof(models));

		_apiKey = apiKey;
		_httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
		// HttpClient's default 100 s Timeout would silently cap the escalating
		// per-attempt timeouts below; the per-attempt CancellationTokenSource in
		// CreateChatCompletion is the sole timeout authority.
		_httpClient.Timeout = Timeout.InfiniteTimeSpan;
		_timeoutSeconds = timeoutSeconds > 0 ? timeoutSeconds : 60;
		_timeProvider = timeProvider ?? TimeProvider.System;
		_requestLog = requestLog ?? (line => ErrorLogger.LogInfo("LLM", line));
		_retryPipeline = TransientRetry.Pipeline(retryCount, TransientRetry.Transient(), timeProvider);
		_modelConfigs = modelList.ToDictionary(m => m.Name, m => m, LlmModelIndex.NameComparer);
	}

	public async Task<string> CreateChatCompletion(
		IList<LlmChatMessage> messages,
		string llmModelName,
		LlmRequestOptions? options = null,
		CancellationToken cancellationToken = default)
	{
		options ??= LlmRequestOptions.Default;

		if (!_modelConfigs.TryGetValue(llmModelName, out var config))
			throw new ArgumentException(
				$"{llmModelName} is not one of the configured Anthropic models. Available: {string.Join(",", _modelConfigs.Keys)}",
				nameof(llmModelName));

		// Extract system message (Anthropic uses a top-level "system" parameter)
		string? systemMessage = messages
			.Where(m => m.Role == LlmChatRole.System)
			.Select(m => m.Content)
			.FirstOrDefault();

		var conversationMessages = messages
			.Where(m => m.Role != LlmChatRole.System)
			.Select(m => new AnthropicMessage
			{
				Role = m.Role == LlmChatRole.User ? "user" : "assistant",
				Content = m.Content
			})
			.ToArray();

		if (conversationMessages.Length == 0)
			throw new ArgumentException("At least one non-system message is required for Anthropic API calls.", nameof(messages));

		// The service settles the prompt's level against the levels ticked for this model,
		// so every caller gets the same answer without resolving it first.
		ReasoningLevel? level = options.ReasoningLevel is { } asked
			? ReasoningLevelResolver.Resolve(config, asked)
			: null;

		var request = new AnthropicRequest
		{
			Model = llmModelName,
			System = systemMessage,
			Messages = conversationMessages
		};

		_requestLog(LlmRequestLog.Started(options, LlmProvider.Anthropic, llmModelName, level));

		AnthropicReply reply;
		RequestShape sent;
		try
		{
			string responseBody;
			(responseBody, sent) = await SendWithFallbacksAsync(
				request,
				config,
				new RequestShape(level, options.FastMode),
				options,
				cancellationToken).ConfigureAwait(false);
			// Inside the try, so a reply that is not valid JSON is logged as the failure
			// it is rather than leaving a start line with no outcome.
			reply = AnthropicReply.Parse(responseBody);
		}
		catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
		{
			// Only the caller's own cancel is a cancel. A per-attempt timeout is also an
			// OperationCanceledException, and is logged below as the failure it is.
			_requestLog(LlmRequestLog.Cancelled(options, LlmProvider.Anthropic, llmModelName));
			throw;
		}
		catch (Exception ex)
		{
			_requestLog(LlmRequestLog.Failed(options, LlmProvider.Anthropic, llmModelName, ex));
			throw;
		}

		// Logged before the reply is read, so a refusal still records what was served.
		_requestLog(LlmRequestLog.Completed(
			options, LlmProvider.Anthropic, llmModelName, reply.Served, sent.FastMode, sent.Level));

		return ReadAnswer(reply, options, ReasoningLimits.For(sent.Level).AnthropicMaxTokens);
	}

	/// <summary>
	/// What a fallback can drop from a request: the reasoning level, with the model's
	/// temperature, and Fast mode. Everything else about the request stays the same on
	/// every send.
	/// </summary>
	/// <param name="Level">The resolved level to send, or null to send none.</param>
	/// <param name="FastMode">Whether to ask for Fast mode.</param>
	/// <param name="SendsTemperature">
	/// Whether the model's temperature may go on the request. False on every send after a
	/// reasoning fallback; see <see cref="SendWithFallbacksAsync"/>.
	/// </param>
	private readonly record struct RequestShape(ReasoningLevel? Level, bool FastMode, bool SendsTemperature = true);

	/// <summary>
	/// Turns a successful reply into the text handed back to the caller. A refusal is a
	/// failure, never an empty success; an answer that hit the length limit is returned,
	/// and the caller is told so it can say the end is missing.
	/// </summary>
	private static string ReadAnswer(AnthropicReply reply, LlmRequestOptions options, int maxTokens)
	{
		if (reply.WasRefused)
		{
			ErrorLogger.LogInfo("LLM", $"Anthropic declined the request (stop_reason=refusal, {reply.Text.Length} characters of text discarded).");
			throw new LlmRefusalException();
		}

		if (reply.WasCutShort)
		{
			ErrorLogger.LogInfo("LLM", $"Anthropic answer stopped at max_tokens ({maxTokens}); returning the {reply.Text.Length} characters that arrived.");
			options.OnCutShort?.Invoke();
		}

		return reply.Text;
	}

	/// <summary>
	/// Sends the request and, if the reasoning level or Fast mode is what the service
	/// refused, sends it again without that one setting rather than losing the user's text.
	/// Each setting can be dropped once, so a request that asked for both is sent at most
	/// three times. Both fallbacks go in the cheaper direction (no thinking, standard
	/// speed), so recovering without asking can never surprise anyone's bill. The user's
	/// saved settings are never changed: access may be granted, or the model changed, later.
	/// <para>
	/// The callbacks run only after a send succeeded, so the user is never told "it ran
	/// without a reasoning level" about a request that then failed outright.
	/// </para>
	/// </summary>
	private async Task<(string Body, RequestShape Sent)> SendWithFallbacksAsync(
		AnthropicRequest request,
		LlmModelConfig config,
		RequestShape shape,
		LlmRequestOptions options,
		CancellationToken cancellationToken)
	{
		FastModeFallback? fastModeFallback = null;
		ReasoningLevel? refusedLevel = null;

		while (true)
		{
			try
			{
				string body = await SendAsync(request, config, shape, cancellationToken).ConfigureAwait(false);

				if (fastModeFallback is not null)
					options.OnFastModeFallback?.Invoke(fastModeFallback);
				if (refusedLevel is { } refused)
					options.OnReasoningFallback?.Invoke(refused);

				return (body, shape);
			}
			catch (NonTransientLlmException ex)
			{
				bool reasoningCause = shape.Level is not null
					&& ReasoningFailure.IsCause(ex.StatusCode, ex.Message);
				FastModeFallbackReason? fastModeCause = shape.FastMode
					? FastModeFailure.Classify(ex.StatusCode, ex.Message)
					: null;

				// With both settings on, a message that names Fast mode is taken at its word
				// even if it also names thinking; otherwise the reasoning level is the one
				// dropped. A 403 that names neither is left to the Fast mode rule, which reads
				// it as the Fast mode entitlement gate.
				bool dropReasoning = reasoningCause
					&& !(fastModeCause is not null && FastModeFailure.MentionsFastMode(ex.Message));

				if (dropReasoning)
				{
					refusedLevel = shape.Level;
					ErrorLogger.LogInfo("LLM", $"{ReasoningMessages.Fallback(config.Name, shape.Level!.Value)} Anthropic said: {ex.Message}");
					// The temperature goes too. A model that refused the level may still think
					// at its default with none, and refuse a temperature; issue #438 reports
					// this on Claude Opus 5.5, Sonnet 5.5 and Fable. That error names no
					// reasoning term, so the retry would fail and the user's text would be
					// lost after all. Not verified against the live API.
					shape = shape with { Level = null, SendsTemperature = false };
				}
				else if (fastModeCause is { } reason)
				{
					fastModeFallback = new FastModeFallback(reason, ex.Message);
					ErrorLogger.LogInfo("LLM", FastModeMessages.DescribeForLog(fastModeFallback));
					shape = shape with { FastMode = false };
				}
				else
				{
					throw; // Neither setting caused it; report it as it is.
				}
			}
			catch (HttpRequestException ex) when (shape.FastMode && FastModeFailure.IsCapacity((int?)ex.StatusCode))
			{
				// Reached only once the retry policy has exhausted its attempts on Fast mode's
				// dedicated 429 rate limit or a 529 capacity rejection.
				fastModeFallback = new FastModeFallback(FastModeFallbackReason.Busy, ex.Message);
				ErrorLogger.LogInfo("LLM", FastModeMessages.DescribeForLog(fastModeFallback));
				shape = shape with { FastMode = false };
			}
		}
	}

	private async Task<string> SendAsync(
		AnthropicRequest request,
		LlmModelConfig config,
		RequestShape shape,
		CancellationToken cancellationToken)
	{
		string jsonBody = Serialize(request, config, shape);
		bool fastMode = shape.FastMode;

		// Heavier reasoning takes longer, so each try may wait a multiple of the user's
		// wait time; the multiple comes from the one limits table both services share.
		TimeSpan waitPerTry = ReasoningLimits.For(shape.Level).WaitPerTry(TimeSpan.FromSeconds(_timeoutSeconds));

		// The retry shape lives in TransientRetry.cs, shared with the other four remote
		// services, so the cold-start path (slow DNS/TLS/JIT warmup on the first call
		// after a reboot) gets a few escalating-timeout attempts instead of an unhandled
		// failure. Only transient failures (network/timeout, 429, 5xx) are surfaced as the
		// retryable HttpRequestException; permanent 4xx errors such as 401 Unauthorized
		// throw NonTransientLlmException, which the pipeline does not handle, so a bad API
		// key fails fast instead of after every retry.
		//
		// Each send counts its own attempts, so the standard-speed retry after a Fast mode
		// fallback starts from 1 rather than inheriting the abandoned send's stretched
		// timeout — the attempt lives in Polly's per-execution context, not in the shared
		// pipeline.
		//
		// Handing the token to the pipeline is what makes the ladder escapable, and it is
		// the whole mechanism: Polly checks it at the head of every attempt and waits on it
		// during the backoff, so an outside cancel ends the retry loop there rather than
		// being re-read as one more transient blip (issue #256).
		string responseBody = await _retryPipeline.ExecuteWithAttemptAsync(async (attempt, overallToken) =>
		{
			using var thisTryCts = new CancellationTokenSource(waitPerTry * attempt, _timeProvider);
			using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(overallToken, thisTryCts.Token);

			// HttpRequestMessage is single-use; rebuild it for each attempt.
			using var httpRequest = new HttpRequestMessage(HttpMethod.Post, ApiUrl);
			httpRequest.Headers.Add("x-api-key", _apiKey);
			httpRequest.Headers.Add("anthropic-version", AnthropicVersion);
			// The header block is rebuilt per attempt, so gating it here covers every retry.
			if (fastMode)
				httpRequest.Headers.Add("anthropic-beta", FastModeBeta);
			httpRequest.Content = new StringContent(jsonBody, Encoding.UTF8, "application/json");

			using var httpResponse = await _httpClient.SendAsync(httpRequest, linkedCts.Token).ConfigureAwait(false);

			string body = await httpResponse.Content.ReadAsStringAsync(linkedCts.Token).ConfigureAwait(false);

			if (!httpResponse.IsSuccessStatusCode)
			{
				int statusCode = (int)httpResponse.StatusCode;
				string errorMessage = $"Anthropic API returned {statusCode} {httpResponse.StatusCode}";
				try
				{
					var errorResponse = JsonSerializer.Deserialize<AnthropicErrorResponse>(body);
					if (errorResponse?.Error != null)
					{
						errorMessage = $"Anthropic API error ({errorResponse.Error.Type}): {errorResponse.Error.Message}";
					}
				}
				catch (JsonException)
				{
					// If we can't parse the error, use the raw status code message
				}

				// Retry only transient failures (429, 5xx); fail fast on permanent
				// 4xx errors like 401 Unauthorized so a bad API key is reported at once.
				// The status travels on the exception so an exhausted Fast mode 429/529
				// can be told apart from an ordinary server error by the fallback path.
				if (LlmHttpStatus.IsTransient(statusCode))
					throw new HttpRequestException(errorMessage, inner: null, httpResponse.StatusCode);
				throw new NonTransientLlmException(errorMessage, statusCode);
			}

			return body;
		}, cancellationToken).ConfigureAwait(false);

		return responseBody;
	}

	private static readonly JsonSerializerOptions JsonOptions = new()
	{
		DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
	};

	/// <summary>
	/// Renders the body for one send. Builds a fresh DTO rather than changing the caller's
	/// request, so a send and the fallback after it can never see each other's values.
	/// WhenWritingNull drops every field a setting does not use, so a request with no
	/// level and Fast mode off carries no reasoning or speed fields at all.
	/// </summary>
	private static string Serialize(AnthropicRequest request, LlmModelConfig config, RequestShape shape)
	{
		ReasoningLevel? level = shape.Level;
		string? effort = AnthropicReasoningFields.Effort(level, request.Model);

		return JsonSerializer.Serialize(
			new AnthropicRequest
			{
				Model = request.Model,
				MaxTokens = ReasoningLimits.For(level).AnthropicMaxTokens,
				// The service refuses a custom temperature while the model is thinking.
				Temperature = shape.SendsTemperature
					&& AnthropicReasoningFields.SendsTemperature(level, request.Model)
					&& config.CustomTemperature.HasValue
					? (double)config.CustomTemperature.Value
					: null,
				Thinking = AnthropicReasoningFields.ThinkingType(level, request.Model) is { } type
					? new AnthropicThinking { Type = type }
					: null,
				OutputConfig = effort is null ? null : new AnthropicOutputConfig { Effort = effort },
				System = request.System,
				Messages = request.Messages,
				Speed = shape.FastMode ? FastSpeed : null,
			},
			JsonOptions);
	}

	// --- Internal DTOs for Anthropic API serialization ---

	private class AnthropicRequest
	{
		[JsonPropertyName("model")] public string Model { get; set; } = "";
		[JsonPropertyName("max_tokens")] public int MaxTokens { get; set; }
		[JsonPropertyName("temperature")] public double? Temperature { get; set; }
		/// <summary>Null (and therefore omitted) when no reasoning level is sent.</summary>
		[JsonPropertyName("thinking")] public AnthropicThinking? Thinking { get; set; }
		/// <summary>Null (and therefore omitted) unless the request makes the model think.</summary>
		[JsonPropertyName("output_config")] public AnthropicOutputConfig? OutputConfig { get; set; }
		[JsonPropertyName("system")] public string? System { get; set; }
		[JsonPropertyName("messages")] public AnthropicMessage[] Messages { get; set; } = [];
		/// <summary>Null (and therefore omitted) unless Fast mode is on for this request.</summary>
		[JsonPropertyName("speed")] public string? Speed { get; set; }
	}

	private class AnthropicThinking
	{
		[JsonPropertyName("type")] public string Type { get; set; } = "";
	}

	private class AnthropicOutputConfig
	{
		[JsonPropertyName("effort")] public string Effort { get; set; } = "";
	}

	private class AnthropicMessage
	{
		[JsonPropertyName("role")] public string Role { get; set; } = "";
		[JsonPropertyName("content")] public string Content { get; set; } = "";
	}

	private class AnthropicErrorResponse
	{
		[JsonPropertyName("error")] public AnthropicErrorDetail? Error { get; set; }
	}

	private class AnthropicErrorDetail
	{
		[JsonPropertyName("type")] public string Type { get; set; } = "";
		[JsonPropertyName("message")] public string Message { get; set; } = "";
	}
}
