using CognitiveSupport;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Mutation.Ui.Views.SettingsUi.Pages;

public sealed partial class TtsSettingsPage : UserControl
{
	private readonly Settings _settings;
	private bool _suppressEvents;

	public TtsSettingsPage(Settings settings)
	{
		_settings = settings;
		InitializeComponent();
		LoadValues();
	}

	private void LoadValues()
	{
		_suppressEvents = true;
		try
		{
			var tts = _settings.TextToSpeechSettings ??= new TextToSpeechSettings();
			ToggleSpeechPreprocessing.IsOn = tts.EnableSpeechPreprocessing;
		}
		finally { _suppressEvents = false; }
	}

	private void ToggleSpeechPreprocessing_Toggled(object sender, RoutedEventArgs e)
	{
		if (_suppressEvents) return;
		(_settings.TextToSpeechSettings ??= new TextToSpeechSettings()).EnableSpeechPreprocessing = ToggleSpeechPreprocessing.IsOn;
	}

	private void BtnResetPreproc_Click(object sender, RoutedEventArgs e)
	{
		ToggleSpeechPreprocessing.IsOn = SettingsDefaults.Tts.EnableSpeechPreprocessing;
	}
}
