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
			return transcript;

		string text = transcript;
		if (manualPunctuation)
		{
			text = text.RemoveSubstrings(",", ".", ";", ":", "?", "!", "...", "…");
			text = text.Replace("  ", " ");
		}

		var rules = _settings.LlmSettings?.TranscriptFormatRules ?? new List<LlmSettings.TranscriptFormatRule>();
		text = text.FormatWithRules(rules);
		text = text.CleanupPunctuation();
		return text;
	}

	public async Task<string> FormatWithLlmAsync(string transcript, string systemPrompt, string modelName)
	{
		if (string.IsNullOrWhiteSpace(transcript))
			return transcript;

		var messages = new List<LlmChatMessage>
		  {
				new LlmChatMessage(LlmChatRole.System, systemPrompt),
				new LlmChatMessage(LlmChatRole.User, $"Reformat the following transcript: {transcript}")
		  };

		string formattedText = await _llmService.CreateChatCompletion(messages, modelName);
		return formattedText.FixNewLines();
	}
}
