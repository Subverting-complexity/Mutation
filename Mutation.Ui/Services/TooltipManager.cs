using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using CognitiveSupport;
using System;
using System.Linq;

namespace Mutation.Ui.Services;

public class TooltipManager
{
    private readonly Settings _settings;

    public TooltipManager(Settings settings)
    {
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
    }

    public void SetupTooltips(Control speechBox, Control transcriptBox)
    {
        string speechPromptTip =
            "You can use a prompt to improve transcription. " +
            "Include capitalization and punctuation in the prompt for better output.";

        ToolTipService.SetToolTip(speechBox, speechPromptTip);

        var rules = _settings.TranscriptFormatRules?.Select(r =>
        {
            string replace = r.ReplaceWith?.Replace("\r", " ").Replace("\n", " <nl> ") ?? string.Empty;
            return $"{r.Find} = {replace} (Match: {r.MatchType}, Case Sensitive: {r.CaseSensitive})";
        }) ?? Array.Empty<string>();

        if (rules.Any())
        {
            string rulesText = string.Join("\n", rules);
            string formatTip = $"Voice commands:\n\n{rulesText}";
            ToolTipService.SetToolTip(transcriptBox, formatTip);
        }
    }
}
