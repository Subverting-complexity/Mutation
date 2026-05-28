using System;
using CognitiveSupport;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.Storage.Pickers;
using WinRT.Interop;

namespace Mutation.Ui.Views.SettingsUi.Pages;

public sealed partial class AudioSettingsPage : UserControl
{
	private readonly Settings _settings;
	private bool _suppressEvents;

	public AudioSettingsPage(Settings settings)
	{
		_settings = settings;
		InitializeComponent();
		LoadValues();
		HkMute.HotkeyCommitted += (_, value) =>
		{
			_settings.AudioSettings ??= new AudioSettings();
			_settings.AudioSettings.MicrophoneToggleMuteHotKey = string.IsNullOrWhiteSpace(value) ? null : value;
		};
	}

	private void LoadValues()
	{
		_suppressEvents = true;
		try
		{
			var aud = _settings.AudioSettings ??= new AudioSettings();
			ToggleMicViz.IsOn = aud.EnableMicrophoneVisualization;
			HkMute.Hotkey = aud.MicrophoneToggleMuteHotKey ?? string.Empty;

			var beeps = aud.CustomBeepSettings ??= new AudioSettings.CustomBeepSettingsData();
			ToggleUseCustomBeeps.IsOn = beeps.UseCustomBeeps;
			BeepFilesPanel.Visibility = beeps.UseCustomBeeps ? Visibility.Visible : Visibility.Collapsed;

			TxtBeepSuccess.Text = beeps.BeepSuccessFile ?? string.Empty;
			TxtBeepFailure.Text = beeps.BeepFailureFile ?? string.Empty;
			TxtBeepStart.Text = beeps.BeepStartFile ?? string.Empty;
			TxtBeepEnd.Text = beeps.BeepEndFile ?? string.Empty;
			TxtBeepMute.Text = beeps.BeepMuteFile ?? string.Empty;
			TxtBeepUnmute.Text = beeps.BeepUnmuteFile ?? string.Empty;
		}
		finally { _suppressEvents = false; }
	}

	private void BtnResetMicViz_Click(object sender, RoutedEventArgs e)
	{
		ToggleMicViz.IsOn = SettingsDefaults.Audio.EnableMicrophoneVisualization;
	}

	private void ToggleMicViz_Toggled(object sender, RoutedEventArgs e)
	{
		if (_suppressEvents) return;
		_settings.AudioSettings ??= new AudioSettings();
		_settings.AudioSettings.EnableMicrophoneVisualization = ToggleMicViz.IsOn;
	}

	private void ToggleUseCustomBeeps_Toggled(object sender, RoutedEventArgs e)
	{
		if (_suppressEvents) return;
		_settings.AudioSettings ??= new AudioSettings();
		_settings.AudioSettings.CustomBeepSettings ??= new AudioSettings.CustomBeepSettingsData();
		_settings.AudioSettings.CustomBeepSettings.UseCustomBeeps = ToggleUseCustomBeeps.IsOn;
		BeepFilesPanel.Visibility = ToggleUseCustomBeeps.IsOn ? Visibility.Visible : Visibility.Collapsed;
	}

	private void BeepText_Changed(object sender, TextChangedEventArgs e)
	{
		if (_suppressEvents) return;
		if (sender is not TextBox tb) return;
		string? path = tb.Text;
		var beeps = (_settings.AudioSettings ??= new AudioSettings()).CustomBeepSettings ??= new AudioSettings.CustomBeepSettingsData();
		switch (tb.Tag as string)
		{
			case "success": beeps.BeepSuccessFile = path; break;
			case "failure": beeps.BeepFailureFile = path; break;
			case "start": beeps.BeepStartFile = path; break;
			case "end": beeps.BeepEndFile = path; break;
			case "mute": beeps.BeepMuteFile = path; break;
			case "unmute": beeps.BeepUnmuteFile = path; break;
		}
	}

	private async void BrowseBeep_Click(object sender, RoutedEventArgs e)
	{
		if (sender is not Button btn) return;
		string? key = btn.Tag as string;
		if (string.IsNullOrEmpty(key)) return;

		try
		{
			var picker = new FileOpenPicker();
			picker.FileTypeFilter.Add(".wav");

			var window = (Application.Current as App)?.MainAppWindow;
			if (window is null) return;
			var hwnd = WindowNative.GetWindowHandle(window);
			InitializeWithWindow.Initialize(picker, hwnd);
			var file = await picker.PickSingleFileAsync();
			if (file is null) return;

			TextBox target = key switch
			{
				"success" => TxtBeepSuccess,
				"failure" => TxtBeepFailure,
				"start" => TxtBeepStart,
				"end" => TxtBeepEnd,
				"mute" => TxtBeepMute,
				"unmute" => TxtBeepUnmute,
				_ => TxtBeepSuccess,
			};
			target.Text = file.Path;
		}
		catch (Exception ex)
		{
			System.Diagnostics.Debug.WriteLine($"BrowseBeep failed: {ex.Message}");
		}
	}
}
