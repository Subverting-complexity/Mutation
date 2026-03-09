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

	public AnthropicLlmService(string apiKey, HttpClient httpClient)
	{
		if (string.IsNullOrEmpty(apiKey)) throw new ArgumentNullException(nameof(apiKey));
		_apiKey = apiKey;
		_httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
	}

	public async Task<string> CreateChatCompletion(
		IList<LlmChatMessage> messages,
		string llmModelName,
		decimal temperature = 0.7m)
	{
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
			Temperature = (double)temperature,
			System = systemMessage,
			Messages = conversationMessages
		};

		var jsonOptions = new JsonSerializerOptions
		{
			DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
		};
		string jsonBody = JsonSerializer.Serialize(request, jsonOptions);

		using var httpRequest = new HttpRequestMessage(HttpMethod.Post, ApiUrl);
		httpRequest.Headers.Add("x-api-key", _apiKey);
		httpRequest.Headers.Add("anthropic-version", AnthropicVersion);
		httpRequest.Content = new StringContent(jsonBody, Encoding.UTF8, "application/json");

		using var httpResponse = await _httpClient.SendAsync(httpRequest);

		string responseBody = await httpResponse.Content.ReadAsStringAsync();

		if (!httpResponse.IsSuccessStatusCode)
		{
			string errorMessage = $"Anthropic API returned {(int)httpResponse.StatusCode} {httpResponse.StatusCode}";
			try
			{
				var errorResponse = JsonSerializer.Deserialize<AnthropicErrorResponse>(responseBody);
				if (errorResponse?.Error != null)
				{
					errorMessage = $"Anthropic API error ({errorResponse.Error.Type}): {errorResponse.Error.Message}";
				}
			}
			catch (JsonException)
			{
				// If we can't parse the error, use the raw status code message
			}
			throw new HttpRequestException(errorMessage);
		}

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
		[JsonPropertyName("temperature")] public double Temperature { get; set; }
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
