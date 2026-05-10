using System;
using System.Drawing;
using CognitiveSupport;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Mutation.Ui.Views.SettingsUi.Pages;

public sealed partial class InterfaceSettingsPage : UserControl
{
	private readonly Settings _settings;
	private bool _suppressEvents;

	public InterfaceSettingsPage(Settings settings)
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
			var ui = _settings.MainWindowUiSettings ?? new MainWindowUiSettings();
			NbMaxLines.Value = ui.MaxTextBoxLineCount > 0 ? ui.MaxTextBoxLineCount : SettingsDefaults.MainWindowUi.MaxTextBoxLineCount;
			CmbDictationInsert.SelectedItem = ui.DictationInsertPreference ?? SettingsDefaults.MainWindowUi.DictationInsertPreference;
			UpdateWindowSummary();
		}
		finally { _suppressEvents = false; }
	}

	private void UpdateWindowSummary()
	{
		var ui = _settings.MainWindowUiSettings;
		if (ui is null)
		{
			TxtWindowSummary.Text = "(not set)";
			return;
		}
		TxtWindowSummary.Text = $"Position: {ui.WindowLocation.X}, {ui.WindowLocation.Y}    Size: {ui.WindowSize.Width} x {ui.WindowSize.Height}";
	}

	private void NbMaxLines_ValueChanged(NumberBox sender, NumberBoxValueChangedEventArgs args)
	{
		if (_suppressEvents) return;
		if (double.IsNaN(args.NewValue)) return;
		_settings.MainWindowUiSettings ??= new MainWindowUiSettings();
		_settings.MainWindowUiSettings.MaxTextBoxLineCount = (int)args.NewValue;
	}

	private void CmbDictationInsert_SelectionChanged(object sender, SelectionChangedEventArgs e)
	{
		if (_suppressEvents) return;
		string? selected = CmbDictationInsert.SelectedItem as string;
		_settings.MainWindowUiSettings ??= new MainWindowUiSettings();
		_settings.MainWindowUiSettings.DictationInsertPreference = string.IsNullOrWhiteSpace(selected)
			? SettingsDefaults.MainWindowUi.DictationInsertPreference
			: selected;
	}

	private void BtnResetMaxLines_Click(object sender, RoutedEventArgs e)
	{
		NbMaxLines.Value = SettingsDefaults.MainWindowUi.MaxTextBoxLineCount;
	}

	private void BtnResetDictation_Click(object sender, RoutedEventArgs e)
	{
		CmbDictationInsert.SelectedItem = SettingsDefaults.MainWindowUi.DictationInsertPreference;
	}

	private void BtnResetWindow_Click(object sender, RoutedEventArgs e)
	{
		_settings.MainWindowUiSettings ??= new MainWindowUiSettings();
		_settings.MainWindowUiSettings.WindowLocation = Point.Empty;
		_settings.MainWindowUiSettings.WindowSize = Size.Empty;
		UpdateWindowSummary();
	}
}
