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
			NbAnnounceReadingTimeMin.Value = tts.AnnounceReadingTimeMinimumMinutes;
			NbAnnounceProgressPercent.Value = tts.AnnounceProgressEveryPercent;
			NbAnnounceProgressMin.Value = tts.AnnounceProgressMinimumMinutes;
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

	private void NbAnnounceReadingTimeMin_ValueChanged(NumberBox sender, NumberBoxValueChangedEventArgs args)
	{
		if (_suppressEvents) return;
		if (double.IsNaN(args.NewValue)) return;
		(_settings.TextToSpeechSettings ??= new TextToSpeechSettings()).AnnounceReadingTimeMinimumMinutes = (int)args.NewValue;
	}

	private void BtnResetAnnounceReadingTimeMin_Click(object sender, RoutedEventArgs e)
	{
		NbAnnounceReadingTimeMin.Value = SettingsDefaults.Tts.AnnounceReadingTimeMinimumMinutes;
	}

	private void NbAnnounceProgressPercent_ValueChanged(NumberBox sender, NumberBoxValueChangedEventArgs args)
	{
		if (_suppressEvents) return;
		if (double.IsNaN(args.NewValue)) return;
		(_settings.TextToSpeechSettings ??= new TextToSpeechSettings()).AnnounceProgressEveryPercent = (int)args.NewValue;
	}

	private void BtnResetAnnounceProgressPercent_Click(object sender, RoutedEventArgs e)
	{
		NbAnnounceProgressPercent.Value = SettingsDefaults.Tts.AnnounceProgressEveryPercent;
	}

	private void NbAnnounceProgressMin_ValueChanged(NumberBox sender, NumberBoxValueChangedEventArgs args)
	{
		if (_suppressEvents) return;
		if (double.IsNaN(args.NewValue)) return;
		(_settings.TextToSpeechSettings ??= new TextToSpeechSettings()).AnnounceProgressMinimumMinutes = (int)args.NewValue;
	}

	private void BtnResetAnnounceProgressMin_Click(object sender, RoutedEventArgs e)
	{
		NbAnnounceProgressMin.Value = SettingsDefaults.Tts.AnnounceProgressMinimumMinutes;
	}
}
