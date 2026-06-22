using System;
using System.Collections.Generic;
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
			RefreshActiveServiceCombo();

			NbFileTimeout.Value = stt.FileTranscriptionTimeoutSeconds > 0
				? stt.FileTranscriptionTimeoutSeconds
				: SettingsDefaults.Speech.FileTranscriptionTimeoutSeconds;
			TxtTempDir.Text = stt.TempDirectory ?? string.Empty;
			HkSendAfterTranscription.Hotkey = stt.SendHotkeyAfterTranscriptionOperation ?? string.Empty;

			ChkStripSilence.IsChecked = stt.EnableSilenceStripping;
			NbMinSilence.Value = stt.MinSilenceSeconds;
			NbSilenceThreshold.Value = stt.SilenceThresholdDbFs;
			NbSilenceGuard.Value = stt.SilenceGuardMilliseconds;
		}
		finally { _suppressEvents = false; }
	}

	private void CmbActiveService_SelectionChanged(object sender, SelectionChangedEventArgs e)
	{
		if (_suppressEvents) return;
		string? name = CmbActiveService.SelectedItem as string;
		(_settings.SpeechToTextSettings ??= new SpeechToTextSettings()).ActiveSpeechToTextService = name;
		_activeEntry = name is null
			? null
			: ServiceEntries.FirstOrDefault(x => string.Equals(x.Name, name, StringComparison.Ordinal));
	}

	// Rebuilds the active-service dropdown from the current entries, skipping blank
	// or duplicate names (only uniquely named services can be the active one), and
	// restores the selection from the stored active-service name.
	private void RefreshActiveServiceCombo()
	{
		bool previous = _suppressEvents;
		_suppressEvents = true;
		try
		{
			string? desired = _settings.SpeechToTextSettings?.ActiveSpeechToTextService;
			CmbActiveService.Items.Clear();
			var seen = new HashSet<string>(StringComparer.Ordinal);
			foreach (var entry in ServiceEntries)
			{
				string name = entry.Name;
				if (string.IsNullOrWhiteSpace(name)) continue;
				if (!seen.Add(name)) continue;
				CmbActiveService.Items.Add(name);
			}
			CmbActiveService.SelectedItem =
				!string.IsNullOrWhiteSpace(desired) && CmbActiveService.Items.Contains(desired)
					? desired
					: null;
		}
		finally { _suppressEvents = previous; }
	}

	private void ServiceEntry_PropertyChanged(object? sender, PropertyChangedEventArgs e)
	{
		if (e.PropertyName != nameof(SpeechServiceEntry.Name)) return;
		if (sender is not SpeechServiceEntry entry) return;

		// Keep the active-service reference pointed at the (possibly renamed) service.
		if (ReferenceEquals(entry, _activeEntry))
		{
			(_settings.SpeechToTextSettings ??= new SpeechToTextSettings()).ActiveSpeechToTextService =
				string.IsNullOrWhiteSpace(entry.Name) ? null : entry.Name;
		}
		RefreshActiveServiceCombo();
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
		RefreshActiveServiceCombo();
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
		RefreshActiveServiceCombo();
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
