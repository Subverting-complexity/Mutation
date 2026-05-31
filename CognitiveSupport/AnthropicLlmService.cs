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
		_timeoutSeconds = timeoutSeconds > 0 ? timeoutSeconds : 60;
		_retryCount = retryCount < 0 ? 0 : retryCount;
		_modelConfigs = models.ToDictionary(m => m.Name, m => m, StringComparer.OrdinalIgnoreCase);
	}

	public async Task<string> CreateChatCompletion(
		IList<LlmChatMessage> messages,
		string llmModelName)
	{
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

		var jsonOptions = new JsonSerializerOptions
		{
			DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
		};
		string jsonBody = JsonSerializer.Serialize(request, jsonOptions);

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
				if (LlmHttpStatus.IsTransient(statusCode))
					throw new HttpRequestException(errorMessage);
				throw new NonTransientLlmException(errorMessage, statusCode);
			}

			return body;
		}, pollyContext).ConfigureAwait(false);

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
