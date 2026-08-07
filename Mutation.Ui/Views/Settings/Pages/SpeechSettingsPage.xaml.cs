using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using CognitiveSupport;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.Storage.Pickers;
using WinRT.Interop;

namespace Mutation.Ui.Views.SettingsUi.Pages;

public sealed partial class SpeechSettingsPage : UserControl
{
	private const double BytesPerMb = 1024.0 * 1024.0;

	private readonly Settings _settings;
	private bool _suppressEvents;

	// The entry whose name currently equals ActiveSpeechToTextService, tracked by
	// reference so a rename of the active service can follow it (PropertyChanged
	// does not carry the previous name).
	private SpeechServiceEntry? _activeEntry;

	public ObservableCollection<SpeechServiceEntry> ServiceEntries { get; } = new();

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

			foreach (var existing in ServiceEntries)
				existing.PropertyChanged -= ServiceEntry_PropertyChanged;
			ServiceEntries.Clear();
			foreach (var s in stt.Services ?? Array.Empty<SpeechToTextServiceSettings>())
			{
				var entry = new SpeechServiceEntry(s);
				entry.PropertyChanged += ServiceEntry_PropertyChanged;
				ServiceEntries.Add(entry);
			}
			_activeEntry = ServiceEntries.FirstOrDefault(e =>
				string.Equals(e.Name, stt.ActiveSpeechToTextService, StringComparison.Ordinal));

			NbFileTimeout.Value = stt.FileTranscriptionTimeoutSeconds > 0
				? stt.FileTranscriptionTimeoutSeconds
				: SettingsDefaults.Speech.FileTranscriptionTimeoutSeconds;
			NbMaxRetainedSessions.Value = SpeechToTextSettings.ClampRetainedSessions(stt.MaxRetainedSessions);
			TxtTempDir.Text = stt.TempDirectory ?? string.Empty;
			HkSendAfterTranscription.Hotkey = stt.SendHotkeyAfterTranscriptionOperation ?? string.Empty;

			ChkStripSilence.IsChecked = stt.EnableSilenceStripping;
			NbMinSilence.Value = stt.MinSilenceSeconds;
			NbSilenceThreshold.Value = stt.SilenceThresholdDbFs;
			NbSilenceGuard.Value = stt.SilenceGuardMilliseconds;
			NbMaxUploadSizeMb.Value = stt.MaxTranscriptionUploadBytes > 0
				? Math.Round(stt.MaxTranscriptionUploadBytes / BytesPerMb, 1)
				: 0;
		}
		finally { _suppressEvents = false; }
	}

	// The active service is chosen from the main window, not here. When the user
	// renames the service that happens to be active, follow the rename so the stored
	// reference does not dangle (PropertyChanged does not carry the previous name).
	private void ServiceEntry_PropertyChanged(object? sender, PropertyChangedEventArgs e)
	{
		if (e.PropertyName != nameof(SpeechServiceEntry.Name)) return;
		if (sender is not SpeechServiceEntry entry) return;
		if (!ReferenceEquals(entry, _activeEntry)) return;

		(_settings.SpeechToTextSettings ??= new SpeechToTextSettings()).ActiveSpeechToTextService =
			string.IsNullOrWhiteSpace(entry.Name) ? null : entry.Name;
	}

	private void BtnAddService_Click(object sender, RoutedEventArgs e)
	{
		var model = new SpeechToTextServiceSettings
		{
			Name = SettingsDefaults.Speech.DefaultServiceName,
			Provider = SpeechToTextProviders.OpenAi,
			ModelId = SettingsDefaults.Speech.DefaultServiceModelId,
			BaseDomain = SettingsDefaults.Speech.DefaultServiceBaseDomain,
			SpeechToTextPrompt = SettingsDefaults.Speech.DefaultServicePrompt,
			TimeoutSeconds = SettingsDefaults.Speech.ServiceTimeoutSeconds,
		};
		var entry = new SpeechServiceEntry(model);
		entry.PropertyChanged += ServiceEntry_PropertyChanged;
		ServiceEntries.Add(entry);
		SyncServicesArray();
	}

	private void ServiceDelete_Click(object sender, RoutedEventArgs e)
	{
		if (sender is not Button { Tag: SpeechServiceEntry entry }) return;
		entry.PropertyChanged -= ServiceEntry_PropertyChanged;
		ServiceEntries.Remove(entry);

		if (ReferenceEquals(entry, _activeEntry))
		{
			// Repoint the active service to the first remaining named service, or clear it.
			string? next = ServiceEntries
				.Select(x => x.Name)
				.FirstOrDefault(n => !string.IsNullOrWhiteSpace(n));
			(_settings.SpeechToTextSettings ??= new SpeechToTextSettings()).ActiveSpeechToTextService = next;
			_activeEntry = next is null
				? null
				: ServiceEntries.FirstOrDefault(x => string.Equals(x.Name, next, StringComparison.Ordinal));
		}

		SyncServicesArray();
	}

	// Writes the entry-backed models back as the Services array. Field edits mutate
	// the wrapped models in place, so only add/delete need to rebuild the array.
	private void SyncServicesArray() =>
		(_settings.SpeechToTextSettings ??= new SpeechToTextSettings()).Services =
			ServiceEntries.Select(x => x.Model).ToArray();

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

	private void NbMaxRetainedSessions_ValueChanged(NumberBox sender, NumberBoxValueChangedEventArgs args)
	{
		if (_suppressEvents) return;
		if (double.IsNaN(args.NewValue)) return;
		(_settings.SpeechToTextSettings ??= new SpeechToTextSettings()).MaxRetainedSessions =
			SpeechToTextSettings.ClampRetainedSessions((int)args.NewValue);
	}

	private void BtnResetMaxRetainedSessions_Click(object sender, RoutedEventArgs e) =>
		NbMaxRetainedSessions.Value = SettingsDefaults.Speech.MaxRetainedSessions;

	private void ChkStripSilence_Changed(object sender, RoutedEventArgs e)
	{
		if (_suppressEvents) return;
		(_settings.SpeechToTextSettings ??= new SpeechToTextSettings()).EnableSilenceStripping = ChkStripSilence.IsChecked == true;
	}

	private void NbMinSilence_ValueChanged(NumberBox sender, NumberBoxValueChangedEventArgs args)
	{
		if (_suppressEvents) return;
		if (double.IsNaN(args.NewValue)) return;
		(_settings.SpeechToTextSettings ??= new SpeechToTextSettings()).MinSilenceSeconds = args.NewValue;
	}

	private void NbSilenceThreshold_ValueChanged(NumberBox sender, NumberBoxValueChangedEventArgs args)
	{
		if (_suppressEvents) return;
		if (double.IsNaN(args.NewValue)) return;
		(_settings.SpeechToTextSettings ??= new SpeechToTextSettings()).SilenceThresholdDbFs = args.NewValue;
	}

	private void NbSilenceGuard_ValueChanged(NumberBox sender, NumberBoxValueChangedEventArgs args)
	{
		if (_suppressEvents) return;
		if (double.IsNaN(args.NewValue)) return;
		(_settings.SpeechToTextSettings ??= new SpeechToTextSettings()).SilenceGuardMilliseconds = args.NewValue;
	}

	private void BtnResetMinSilence_Click(object sender, RoutedEventArgs e) =>
		NbMinSilence.Value = SettingsDefaults.Speech.MinSilenceSeconds;

	private void BtnResetSilenceThreshold_Click(object sender, RoutedEventArgs e) =>
		NbSilenceThreshold.Value = SettingsDefaults.Speech.SilenceThresholdDbFs;

	private void BtnResetSilenceGuard_Click(object sender, RoutedEventArgs e) =>
		NbSilenceGuard.Value = SettingsDefaults.Speech.SilenceGuardMilliseconds;

	private void NbMaxUploadSizeMb_ValueChanged(NumberBox sender, NumberBoxValueChangedEventArgs args)
	{
		if (_suppressEvents) return;
		if (double.IsNaN(args.NewValue)) return;
		// 0 (or less) stores 0 = "never split"; otherwise convert MB -> bytes and clamp,
		// so the stored value is always one the chunker can act on.
		long bytes = args.NewValue <= 0 ? 0 : (long)Math.Round(args.NewValue * BytesPerMb);
		long clamped = SpeechToTextSettings.ClampTranscriptionUploadBytes(bytes);
		(_settings.SpeechToTextSettings ??= new SpeechToTextSettings()).MaxTranscriptionUploadBytes = clamped;

		// The box allows 0 as "never split", so it also allows a fraction of a megabyte,
		// which the clamp raises to 1 MB. Put the value that was actually stored back on
		// screen rather than leaving the box — and the screen reader — announcing a
		// number nothing will act on.
		ShowUploadSize(sender, clamped);
	}

	private void ShowUploadSize(NumberBox box, long bytes)
	{
		double display = bytes > 0 ? Math.Round(bytes / BytesPerMb, 1) : 0;
		if (Math.Abs(display - box.Value) < 0.0001)
			return;

		_suppressEvents = true;
		try { box.Value = display; }
		finally { _suppressEvents = false; }
	}

	private void BtnResetMaxUploadSize_Click(object sender, RoutedEventArgs e) =>
		NbMaxUploadSizeMb.Value = SettingsDefaults.Speech.MaxTranscriptionUploadBytes / BytesPerMb;

	// Puts the stored temp directory back on screen after the dialog has repaired an
	// unusable one, so the user is looking at the path that will actually be saved
	// (issue #230). _suppressEvents keeps the write-back from firing on the way in.
	public void RefreshTempDirectory()
	{
		_suppressEvents = true;
		try
		{
			TxtTempDir.Text = _settings.SpeechToTextSettings?.TempDirectory ?? string.Empty;
		}
		finally
		{
			_suppressEvents = false;
		}
	}

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
