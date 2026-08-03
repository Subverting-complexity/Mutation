using CognitiveSupport;
using CognitiveSupport.Extensions;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Mutation.Ui;

public class TranscriptFormatter
{
	private readonly Settings _settings;
	private readonly ILlmService _llmService;

	public TranscriptFormatter(Settings settings, ILlmService llmService)
	{
		_settings = settings ?? throw new ArgumentNullException(nameof(settings));
		_llmService = llmService ?? throw new ArgumentNullException(nameof(llmService));
	}

	public string ApplyRules(string transcript, bool manualPunctuation)
	{
		if (transcript is null)
			return transcript!;

		string text = transcript;
		if (manualPunctuation)
		{
			text = text.RemoveSubstrings(",", ".", ";", ":", "?", "!", "...", "…");
			text = text.Replace("  ", " ");
		}

		var rules = _settings.TranscriptFormatRules ?? new List<TranscriptFormatRule>();
		text = text.FormatWithRules(rules);
		text = text.CleanupPunctuation();
		return text;
	}

	/// <param name="options">
	/// Per-request knobs (Fast mode, and the callback that reports a fall back to
	/// standard speed) forwarded to the language-model service unchanged.
	/// </param>
	public async Task<string> ProcessWithLlmAsync(
		string transcript,
		string systemPrompt,
		string modelName,
		LlmRequestOptions? options = null)
	{
		if (string.IsNullOrWhiteSpace(transcript))
			return transcript;

		var messages = new List<LlmChatMessage>
		  {
				new LlmChatMessage(LlmChatRole.System, systemPrompt),
				new LlmChatMessage(LlmChatRole.User, transcript)
		  };

		string formattedText = await _llmService.CreateChatCompletion(messages, modelName, options);
		return formattedText.FixNewLines();
	}
}
