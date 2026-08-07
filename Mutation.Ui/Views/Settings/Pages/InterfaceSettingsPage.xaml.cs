using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using CognitiveSupport;
using Mutation.Ui;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Mutation.Ui.Views.SettingsUi.Pages;

public sealed partial class InterfaceSettingsPage : UserControl
{
	private readonly Settings _settings;
	private bool _suppressEvents;

	// The same described options the main window's dropdown binds, for the same two reasons:
	// a screen reader was reading out identifiers, and this list's middle entry said "Type",
	// which is not a DictationInsertOption at all — choosing it wrote a value the main window
	// could not parse, so the preference silently reverted to Paste (issue #243).
	private readonly IReadOnlyList<DictationInsertOptionItem> _insertOptions = DictationInsertOptionItem.All();

	public InterfaceSettingsPage(Settings settings)
	{
		_settings = settings;
		InitializeComponent();
		CmbDictationInsert.ItemsSource = _insertOptions;
		CmbDictationInsert.DisplayMemberPath = nameof(DictationInsertOptionItem.Description);
		LoadValues();
	}

	private void LoadValues()
	{
		_suppressEvents = true;
		try
		{
			var ui = _settings.MainWindowUiSettings ?? new MainWindowUiSettings();
			NbMaxLines.Value = ui.MaxTextBoxLineCount > 0 ? ui.MaxTextBoxLineCount : SettingsDefaults.MainWindowUi.MaxTextBoxLineCount;
			SelectStoredOption(ui.DictationInsertPreference);
		}
		finally { _suppressEvents = false; }
	}

	/// <summary>
	/// Selects the entry a stored preference names, and writes the value back. A file written
	/// by an older build can hold something no option matches — "Type" was offered here for
	/// years — so an unrecognised value falls back to the default rather than leaving the
	/// dropdown blank, and the file is corrected rather than left holding a value nothing can
	/// read.
	/// <para>
	/// The write is explicit rather than left to <c>SelectionChanged</c>: selecting the item
	/// that is already selected raises nothing, which is exactly the case a Reset lands in
	/// after a fallback, and it would have made the button do nothing at all.
	/// </para>
	/// </summary>
	private void SelectStoredOption(string? storedValue)
	{
		DictationInsertOptionItem? match = Enum.TryParse(storedValue, ignoreCase: true, out DictationInsertOption stored)
			? _insertOptions.FirstOrDefault(item => item.Option == stored)
			: null;

		match ??= _insertOptions.FirstOrDefault(
			item => item.Option.ToString() == SettingsDefaults.MainWindowUi.DictationInsertPreference);

		CmbDictationInsert.SelectedItem = match;
		StorePreference(match);
	}

	private void StorePreference(DictationInsertOptionItem? selected)
	{
		_settings.MainWindowUiSettings ??= new MainWindowUiSettings();
		// The identifier, not the description: that is what is persisted and what the main
		// window parses back.
		_settings.MainWindowUiSettings.DictationInsertPreference = selected is null
			? SettingsDefaults.MainWindowUi.DictationInsertPreference
			: selected.Option.ToString();
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
		StorePreference(CmbDictationInsert.SelectedItem as DictationInsertOptionItem);
	}

	private void BtnResetMaxLines_Click(object sender, RoutedEventArgs e)
	{
		NbMaxLines.Value = SettingsDefaults.MainWindowUi.MaxTextBoxLineCount;
	}

	private void BtnResetDictation_Click(object sender, RoutedEventArgs e)
	{
		SelectStoredOption(SettingsDefaults.MainWindowUi.DictationInsertPreference);
	}

	private void BtnResetWindow_Click(object sender, RoutedEventArgs e)
	{
		_settings.MainWindowUiSettings ??= new MainWindowUiSettings();
		_settings.MainWindowUiSettings.WindowLocation = Point.Empty;
		_settings.MainWindowUiSettings.WindowSize = Size.Empty;
	}
}
