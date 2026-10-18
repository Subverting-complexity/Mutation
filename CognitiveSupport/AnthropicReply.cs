using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace CognitiveSupport;

/// <summary>
/// What a successful Anthropic Messages reply said: the answer text and why the model
/// stopped. Parsed apart from the HTTP code so the rules for reading a reply can be
/// tested on raw JSON alone.
/// </summary>
/// <param name="Text">The joined text of every <c>text</c> block, in order.</param>
/// <param name="StopReason">The reply's <c>stop_reason</c>, or null when it carried none.</param>
internal sealed record AnthropicReply(string Text, string? StopReason)
{
	/// <summary>The answer reached the request's <c>max_tokens</c> and was cut off.</summary>
	public const string MaxTokens = "max_tokens";

	/// <summary>The model declined to answer.</summary>
	public const string Refusal = "refusal";

	public bool WasCutShort => StopReason == MaxTokens;

	public bool WasRefused => StopReason == Refusal;

	/// <summary>The served model, tier and token counts the reply reported, for the request log.</summary>
	public LlmServedDetails Served { get; init; } = LlmServedDetails.Unknown;

	/// <summary>
	/// Reads a reply body. Only <c>text</c> blocks count as the answer: with thinking on, a
	/// <c>thinking</c> or <c>redacted_thinking</c> block comes first, and taking the first
	/// block regardless of type would hand the user the model's reasoning — or, for a
	/// redacted block, an empty string — instead of its answer. All text blocks are joined,
	/// because a reply may split its answer across more than one.
	/// </summary>
	public static AnthropicReply Parse(string responseBody)
	{
		var response = JsonSerializer.Deserialize<ResponseDto>(responseBody);
		if (response is null)
			return new AnthropicReply(string.Empty, null);

		var text = new StringBuilder();
		foreach (var block in response.Content ?? [])
		{
			if (block?.Type == "text" && block.Text is not null)
				text.Append(block.Text);
		}

		return new AnthropicReply(text.ToString(), response.StopReason)
		{
			Served = new LlmServedDetails(
				response.Model,
				response.Usage?.ServiceTier,
				response.Usage?.InputTokens,
				response.Usage?.OutputTokens),
		};
	}

	private sealed class ResponseDto
	{
		[JsonPropertyName("content")] public ContentBlockDto?[]? Content { get; set; }
		[JsonPropertyName("stop_reason")] public string? StopReason { get; set; }
		[JsonPropertyName("model")] public string? Model { get; set; }
		[JsonPropertyName("usage")] public UsageDto? Usage { get; set; }
	}

	private sealed class UsageDto
	{
		[JsonPropertyName("input_tokens")] public int? InputTokens { get; set; }
		[JsonPropertyName("output_tokens")] public int? OutputTokens { get; set; }
		[JsonPropertyName("service_tier")] public string? ServiceTier { get; set; }
	}

	private sealed class ContentBlockDto
	{
		[JsonPropertyName("type")] public string? Type { get; set; }
		[JsonPropertyName("text")] public string? Text { get; set; }
	}
}
