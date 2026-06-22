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
			NbSkipGrace.Value = tts.SkipSentenceGraceWindowMs;
			NbResumeRewindWords.Value = tts.ResumeRewindWordCount;
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

	private void NbSkipGrace_ValueChanged(NumberBox sender, NumberBoxValueChangedEventArgs args)
	{
		if (_suppressEvents) return;
		if (double.IsNaN(args.NewValue)) return;
		(_settings.TextToSpeechSettings ??= new TextToSpeechSettings()).SkipSentenceGraceWindowMs = (int)args.NewValue;
	}

	private void BtnResetSkipGrace_Click(object sender, RoutedEventArgs e)
	{
		NbSkipGrace.Value = SettingsDefaults.Tts.SkipSentenceGraceWindowMs;
	}

	private void NbResumeRewindWords_ValueChanged(NumberBox sender, NumberBoxValueChangedEventArgs args)
	{
		if (_suppressEvents) return;
		if (double.IsNaN(args.NewValue)) return;
		(_settings.TextToSpeechSettings ??= new TextToSpeechSettings()).ResumeRewindWordCount = (int)args.NewValue;
	}

	private void BtnResetResumeRewind_Click(object sender, RoutedEventArgs e)
	{
		NbResumeRewindWords.Value = SettingsDefaults.Tts.ResumeRewindWordCount;
	}
}
