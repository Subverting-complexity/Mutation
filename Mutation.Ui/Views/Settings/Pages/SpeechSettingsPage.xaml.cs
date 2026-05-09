using System;
using System.Linq;
using CognitiveSupport;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.Storage.Pickers;
using WinRT.Interop;

namespace Mutation.Ui.Views.SettingsUi.Pages;

public sealed partial class SpeechSettingsPage : UserControl
{
	private readonly Settings _settings;
	private bool _suppressEvents;

	public SpeechSettingsPage(Settings settings)
	{
		_settings = settings;
		InitializeComponent();
		LoadValues();

		HkSendAfterTranscription.HotkeyCommitted += (_, value) =>
		{
			(_settings.SpeechToTextSettings ??= new SpeechToTextSettings()).SendHotkeyAfterTranscriptionOperation =
				string.IsNullOrWhiteSpace(value) ? null : value;
		};
	}

	private void LoadValues()
	{
		_suppressEvents = true;
		try
		{
			var stt = _settings.SpeechToTextSettings ??= new SpeechToTextSettings();

			CmbActiveService.Items.Clear();
			foreach (var s in stt.Services ?? Array.Empty<SpeechToTextServiceSettings>())
			{
				if (!string.IsNullOrWhiteSpace(s.Name))
					CmbActiveService.Items.Add(s.Name);
			}
			if (!string.IsNullOrWhiteSpace(stt.ActiveSpeechToTextService))
				CmbActiveService.SelectedItem = stt.ActiveSpeechToTextService;

			NbFileTimeout.Value = stt.FileTranscriptionTimeoutSeconds > 0
				? stt.FileTranscriptionTimeoutSeconds
				: SettingsDefaults.Speech.FileTranscriptionTimeoutSeconds;
			TxtTempDir.Text = stt.TempDirectory ?? string.Empty;
			HkSendAfterTranscription.Hotkey = stt.SendHotkeyAfterTranscriptionOperation ?? string.Empty;
		}
		finally { _suppressEvents = false; }
	}

	private void CmbActiveService_SelectionChanged(object sender, SelectionChangedEventArgs e)
	{
		if (_suppressEvents) return;
		(_settings.SpeechToTextSettings ??= new SpeechToTextSettings()).ActiveSpeechToTextService = CmbActiveService.SelectedItem as string;
	}

	private void NbFileTimeout_ValueChanged(NumberBox sender, NumberBoxValueChangedEventArgs args)
	{
		if (_suppressEvents) return;
		if (double.IsNaN(args.NewValue)) return;
		(_settings.SpeechToTextSettings ??= new SpeechToTextSettings()).FileTranscriptionTimeoutSeconds = (int)args.NewValue;
	}

	private void TxtTempDir_TextChanged(object sender, TextChangedEventArgs e)
	{
		if (_suppressEvents) return;
		(_settings.SpeechToTextSettings ??= new SpeechToTextSettings()).TempDirectory = TxtTempDir.Text;
	}

	private void BtnResetFileTimeout_Click(object sender, RoutedEventArgs e) =>
		NbFileTimeout.Value = SettingsDefaults.Speech.FileTranscriptionTimeoutSeconds;

	private void BtnResetTempDir_Click(object sender, RoutedEventArgs e) =>
		TxtTempDir.Text = SettingsDefaults.Speech.TempDirectory;

	private async void BtnBrowseTempDir_Click(object sender, RoutedEventArgs e)
	{
		try
		{
			var picker = new FolderPicker();
			picker.FileTypeFilter.Add("*");
			var window = (Application.Current as App)?.MainAppWindow;
			if (window is null) return;
			var hwnd = WindowNative.GetWindowHandle(window);
			InitializeWithWindow.Initialize(picker, hwnd);
			var folder = await picker.PickSingleFolderAsync();
			if (folder is null) return;
			TxtTempDir.Text = folder.Path;
		}
		catch (Exception ex)
		{
			System.Diagnostics.Debug.WriteLine($"BrowseTempDir failed: {ex.Message}");
		}
	}
}
