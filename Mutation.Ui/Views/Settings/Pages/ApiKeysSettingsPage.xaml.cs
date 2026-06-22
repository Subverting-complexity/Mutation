using CognitiveSupport;
using Microsoft.UI.Xaml.Controls;

namespace Mutation.Ui.Views.SettingsUi.Pages;

public sealed partial class ApiKeysSettingsPage : UserControl
{
	private readonly Settings _settings;
	private bool _suppressEvents;

	public ApiKeysSettingsPage(Settings settings)
	{
		_settings = settings;
		InitializeComponent();
		LoadValues();

		TxtOpenAiKey.RegisterPropertyChangedCallback(Controls.SecretBox.SecretProperty, (_, _) =>
		{
			if (_suppressEvents) return;
			(_settings.ApiKeys ??= new ApiKeys()).OpenAiApiKey = TxtOpenAiKey.Secret;
		});

		TxtAnthropicKey.RegisterPropertyChangedCallback(Controls.SecretBox.SecretProperty, (_, _) =>
		{
			if (_suppressEvents) return;
			(_settings.ApiKeys ??= new ApiKeys()).AnthropicApiKey = TxtAnthropicKey.Secret;
		});

		TxtDeepgramKey.RegisterPropertyChangedCallback(Controls.SecretBox.SecretProperty, (_, _) =>
		{
			if (_suppressEvents) return;
			(_settings.ApiKeys ??= new ApiKeys()).DeepgramApiKey = TxtDeepgramKey.Secret;
		});
	}

	private void LoadValues()
	{
		_suppressEvents = true;
		try
		{
			var keys = _settings.ApiKeys ??= new ApiKeys();
			TxtOpenAiKey.Secret = keys.OpenAiApiKey ?? string.Empty;
			TxtAnthropicKey.Secret = keys.AnthropicApiKey ?? string.Empty;
			TxtDeepgramKey.Secret = keys.DeepgramApiKey ?? string.Empty;
		}
		finally { _suppressEvents = false; }
	}
}
