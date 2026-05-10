using System.Linq;
using CognitiveSupport;
using Microsoft.UI.Xaml.Controls;

namespace Mutation.Ui.Views.SettingsUi.Pages;

public sealed partial class LlmSettingsPage : UserControl
{
	private readonly Settings _settings;
	private bool _suppressEvents;

	public LlmSettingsPage(Settings settings)
	{
		_settings = settings;
		InitializeComponent();
		LoadValues();

		TxtOpenAiKey.RegisterPropertyChangedCallback(Controls.SecretBox.SecretProperty, (_, _) =>
		{
			if (_suppressEvents) return;
			(_settings.LlmSettings ??= new LlmSettings()).OpenAiApiKey = TxtOpenAiKey.Secret;
		});

		TxtAnthropicKey.RegisterPropertyChangedCallback(Controls.SecretBox.SecretProperty, (_, _) =>
		{
			if (_suppressEvents) return;
			(_settings.LlmSettings ??= new LlmSettings()).AnthropicApiKey = TxtAnthropicKey.Secret;
		});
	}

	private void LoadValues()
	{
		_suppressEvents = true;
		try
		{
			var llm = _settings.LlmSettings ??= new LlmSettings();
			TxtOpenAiKey.Secret = llm.OpenAiApiKey ?? string.Empty;
			TxtAnthropicKey.Secret = llm.AnthropicApiKey ?? string.Empty;
			NbTimeout.Value = llm.TimeoutSeconds > 0 ? llm.TimeoutSeconds : SettingsDefaults.Llm.TimeoutSeconds;

			TxtModelsSummary.Text = llm.Models is { Count: > 0 }
				? string.Join("    ", llm.Models.Select(m => $"• {m.Name} ({m.Provider})"))
				: "(none)";
			TxtPromptsSummary.Text = llm.Prompts is { Count: > 0 }
				? string.Join("    ", llm.Prompts.Select(p => $"• {p.Name}"))
				: "(none)";
		}
		finally { _suppressEvents = false; }
	}

	private void NbTimeout_ValueChanged(NumberBox sender, NumberBoxValueChangedEventArgs args)
	{
		if (_suppressEvents) return;
		if (double.IsNaN(args.NewValue)) return;
		(_settings.LlmSettings ??= new LlmSettings()).TimeoutSeconds = (int)args.NewValue;
	}

	private void BtnResetTimeout_Click(object sender, Microsoft.UI.Xaml.RoutedEventArgs e) =>
		NbTimeout.Value = SettingsDefaults.Llm.TimeoutSeconds;
}
