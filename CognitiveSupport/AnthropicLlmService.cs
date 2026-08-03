using Polly;
using Polly.Contrib.WaitAndRetry;
using Polly.Timeout;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace CognitiveSupport;

public class AnthropicLlmService : ILlmService
{
	private const string ApiUrl = "https://api.anthropic.com/v1/messages";
	private const string AnthropicVersion = "2023-06-01";
	private const int DefaultMaxTokens = 4096;

	/// <summary>Opt-in header for the Fast mode research preview.</summary>
	internal const string FastModeBeta = "fast-mode-2026-02-01";

	/// <summary>Value of the request body's <c>speed</c> parameter when Fast mode is on.</summary>
	internal const string FastSpeed = "fast";

	private readonly string _apiKey;
	private readonly HttpClient _httpClient;
	private readonly Dictionary<string, LlmModelConfig> _modelConfigs;
	private readonly int _timeoutSeconds;
	private readonly int _retryCount;

	public AnthropicLlmService(
		string apiKey,
		IEnumerable<LlmModelConfig> models,
		HttpClient httpClient,
		int timeoutSeconds = 60,
		int retryCount = 3)
	{
		if (string.IsNullOrEmpty(apiKey)) throw new ArgumentNullException(nameof(apiKey));
		if (models is null) throw new ArgumentNullException(nameof(models));

		_apiKey = apiKey;
		_httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
		// HttpClient's default 100 s Timeout would silently cap the escalating
		// per-attempt timeouts below; the per-attempt CancellationTokenSource in
		// CreateChatCompletion is the sole timeout authority.
		_httpClient.Timeout = Timeout.InfiniteTimeSpan;
		_timeoutSeconds = timeoutSeconds > 0 ? timeoutSeconds : 60;
		_retryCount = retryCount < 0 ? 0 : retryCount;
		_modelConfigs = models.ToDictionary(m => m.Name, m => m, StringComparer.OrdinalIgnoreCase);
	}

	public async Task<string> CreateChatCompletion(
		IList<LlmChatMessage> messages,
		string llmModelName,
		LlmRequestOptions? options = null)
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

		var request = new AnthropicRequest
		{
			Model = llmModelName,
			MaxTokens = DefaultMaxTokens,
			Temperature = config.CustomTemperature.HasValue ? (double?)config.CustomTemperature.Value : null,
			System = systemMessage,
			Messages = conversationMessages
		};

		string responseBody = options.FastMode
			? await SendWithFastModeFallbackAsync(request, options).ConfigureAwait(false)
			: await SendAsync(request, fastMode: false).ConfigureAwait(false);

		return ExtractText(responseBody);
	}

	/// <summary>
	/// Runs the request in Fast mode and, if Fast mode itself is what failed, retries the
	/// identical request once at standard speed rather than losing the user's text. The
	/// fallback direction is always cheaper, never more expensive, so it can never produce
	/// a billing surprise — which is what makes recovering without a prompt safe here.
	/// The user's Fast mode setting is never rewritten; access may be granted later.
	/// </summary>
	private async Task<string> SendWithFastModeFallbackAsync(AnthropicRequest request, LlmRequestOptions options)
	{
		FastModeFallback fallback;
		try
		{
			return await SendAsync(request, fastMode: true).ConfigureAwait(false);
		}
		catch (NonTransientLlmException ex)
		{
			var reason = FastModeFailure.Classify(ex.StatusCode, ex.Message);
			if (reason is null)
				throw; // Nothing to do with Fast mode; report it as-is.
			fallback = new FastModeFallback(reason.Value, ex.Message);
		}
		catch (HttpRequestException ex) when (FastModeFailure.IsCapacity((int?)ex.StatusCode))
		{
			// Reached only once the retry policy has exhausted its attempts on Fast mode's
			// dedicated 429 rate limit or a 529 capacity rejection.
			fallback = new FastModeFallback(FastModeFallbackReason.Busy, ex.Message);
		}

		ErrorLogger.LogInfo("LLM", FastModeMessages.DescribeForLog(fallback));
		string body = await SendAsync(request, fastMode: false).ConfigureAwait(false);

		// Announced only after the standard-speed retry succeeded, so the user is never
		// told "it ran at standard speed" for a request that then failed outright.
		options.OnFastModeFallback?.Invoke(fallback);
		return body;
	}

	private async Task<string> SendAsync(AnthropicRequest request, bool fastMode)
	{
		string jsonBody = Serialize(request, fastMode);

		// Retry policy mirrors OpenAiSpeechToTextService.cs so the cold-start path (slow
		// DNS/TLS/JIT warmup on the first call after a reboot) gets a few escalating-timeout
		// attempts instead of an unhandled failure.
		// Only transient failures (network/timeout, 429, 5xx) are surfaced as the
		// retryable HttpRequestException; permanent 4xx errors such as 401 Unauthorized
		// throw NonTransientLlmException, which the policy does not handle, so a bad API
		// key fails fast instead of after every retry.
		const string AttemptKey = "Attempt";

		var delay = Backoff.LinearBackoff(TimeSpan.FromMilliseconds(500), retryCount: _retryCount, factor: 1);
		var retryPolicy = Policy
			.Handle<HttpRequestException>()
			.Or<TimeoutRejectedException>()
			.Or<TaskCanceledException>()
				.WaitAndRetryAsync(
					delay,
					onRetry: (exception, timeSpan, attemptNumber, context) =>
					{
						int attempt = context.ContainsKey(AttemptKey) ? (int)context[AttemptKey] : 1;
						context[AttemptKey] = ++attempt;
					}
				);

		var pollyContext = new Context();
		pollyContext[AttemptKey] = 1;

		string responseBody = await retryPolicy.ExecuteAsync(async (ctx) =>
		{
			int attempt = ctx.ContainsKey(AttemptKey) ? (int)ctx[AttemptKey] : 1;
			int timeout = _timeoutSeconds * attempt;
			using var timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(timeout));

			// HttpRequestMessage is single-use; rebuild it for each attempt.
			using var httpRequest = new HttpRequestMessage(HttpMethod.Post, ApiUrl);
			httpRequest.Headers.Add("x-api-key", _apiKey);
			httpRequest.Headers.Add("anthropic-version", AnthropicVersion);
			// The header block is rebuilt per attempt, so gating it here covers every retry.
			if (fastMode)
				httpRequest.Headers.Add("anthropic-beta", FastModeBeta);
			httpRequest.Content = new StringContent(jsonBody, Encoding.UTF8, "application/json");

			using var httpResponse = await _httpClient.SendAsync(httpRequest, timeoutCts.Token).ConfigureAwait(false);

			string body = await httpResponse.Content.ReadAsStringAsync(timeoutCts.Token).ConfigureAwait(false);

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
		}, pollyContext).ConfigureAwait(false);

		return responseBody;
	}

	private static readonly JsonSerializerOptions JsonOptions = new()
	{
		DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
	};

	/// <summary>
	/// Renders the body for one attempt. Builds a fresh DTO rather than setting
	/// <c>Speed</c> on the caller's request, so the fast attempt and the standard-speed
	/// retry can never observe each other's value. WhenWritingNull drops <c>speed</c>
	/// entirely when Fast mode is off, leaving a standard request byte-for-byte what it
	/// was before this feature existed.
	/// </summary>
	private static string Serialize(AnthropicRequest request, bool fastMode) =>
		JsonSerializer.Serialize(
			new AnthropicRequest
			{
				Model = request.Model,
				MaxTokens = request.MaxTokens,
				Temperature = request.Temperature,
				System = request.System,
				Messages = request.Messages,
				Speed = fastMode ? FastSpeed : null,
			},
			JsonOptions);

	private static string ExtractText(string responseBody)
	{
		var response = JsonSerializer.Deserialize<AnthropicResponse>(responseBody);
		if (response?.Content != null && response.Content.Length > 0)
		{
			var textBlock = response.Content.FirstOrDefault(c => c.Type == "text");
			if (textBlock != null)
			{
				return textBlock.Text ?? string.Empty;
			}
		}

		return string.Empty;
	}

	// --- Internal DTOs for Anthropic API serialization ---

	private class AnthropicRequest
	{
		[JsonPropertyName("model")] public string Model { get; set; } = "";
		[JsonPropertyName("max_tokens")] public int MaxTokens { get; set; }
		[JsonPropertyName("temperature")] public double? Temperature { get; set; }
		[JsonPropertyName("system")] public string? System { get; set; }
		[JsonPropertyName("messages")] public AnthropicMessage[] Messages { get; set; } = [];
		/// <summary>Null (and therefore omitted) unless Fast mode is on for this request.</summary>
		[JsonPropertyName("speed")] public string? Speed { get; set; }
	}

	private class AnthropicMessage
	{
		[JsonPropertyName("role")] public string Role { get; set; } = "";
		[JsonPropertyName("content")] public string Content { get; set; } = "";
	}

	private class AnthropicResponse
	{
		[JsonPropertyName("content")] public AnthropicContentBlock[] Content { get; set; } = [];
	}

	private class AnthropicContentBlock
	{
		[JsonPropertyName("type")] public string Type { get; set; } = "";
		[JsonPropertyName("text")] public string? Text { get; set; }
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
