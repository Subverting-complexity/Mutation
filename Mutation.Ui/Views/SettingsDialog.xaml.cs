using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Linq;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Windows.System;
using CognitiveSupport;
using Mutation.Ui.Services;
using Mutation.Ui.Views.SettingsUi;
using Mutation.Ui.Views.SettingsUi.Pages;

namespace Mutation.Ui.Views;

public sealed partial class SettingsDialog : ContentDialog
{
	private readonly Settings _live;
	private readonly Settings _workingCopy;
	private readonly ISettingsManager? _settingsManager;
	private readonly string? _settingsFilePath;
	private readonly Action? _onLiveApply;
	private readonly List<SettingsCategoryItem> _allCategories = new();

	private SettingsCategoryItem? _selectedCategory;
	private FrameworkElement? _activePage;

	public ObservableCollection<SettingsCategoryItem> FilteredCategories { get; } = new();

	public SettingsDialog(Settings settings)
		: this(settings, null, null, null)
	{
	}

	public SettingsDialog(
		Settings settings,
		ISettingsManager? settingsManager,
		string? settingsFilePath,
		Action? onLiveApply,
		string? initialCategoryKey = null)
	{
		_live = settings ?? throw new ArgumentNullException(nameof(settings));
		_workingCopy = SettingsWorkingCopy.Clone(_live);
		_settingsManager = settingsManager;
		_settingsFilePath = settingsFilePath;
		_onLiveApply = onLiveApply;

		PopulateCategories();
		InitializeComponent();
		SetDialogSize();

		if (FilteredCategories.Count > 0)
		{
			CategoryList.SelectedIndex = IndexOfCategory(initialCategoryKey);
		}
		else
		{
			ShowEmptyState();
		}

		PrimaryButtonClick += SettingsDialog_PrimaryButtonClick;
		KeyDown += SettingsDialog_KeyDown;
	}

	private void SettingsDialog_KeyDown(object sender, KeyRoutedEventArgs e)
	{
		if (e.Handled || e.Key != VirtualKey.Escape)
			return;

		if (SearchBox.FocusState != FocusState.Unfocused && !string.IsNullOrEmpty(SearchBox.Text))
		{
			SearchBox.Text = string.Empty;
			e.Handled = true;
			return;
		}

		Hide();
		e.Handled = true;
	}

	private void PopulateCategories()
	{
		_allCategories.Clear();
		_allCategories.Add(new SettingsCategoryItem("audio", "Audio", "Microphone capture, mute hotkey, beeps.", "",
			new[] { "audio", "microphone", "capture", "mute", "beep", "visualization" }));
		_allCategories.Add(new SettingsCategoryItem("ocr", "Screen capture & OCR", "Screenshot automation, OCR, Azure vision.", "",
			new[] { "ocr", "azure", "screenshot", "vision", "endpoint", "free tier" }));
		_allCategories.Add(new SettingsCategoryItem("speech", "Speech to Text", "Transcription providers and recording behavior.", "",
			new[] { "speech", "stt", "whisper", "deepgram", "transcription", "temp directory", "timeout" }));
		_allCategories.Add(new SettingsCategoryItem("apikeys", "API keys", "Central provider keys: OpenAI, Anthropic, Deepgram.", "",
			new[] { "api key", "api keys", "openai", "anthropic", "claude", "deepgram", "key", "credentials" }));
		_allCategories.Add(new SettingsCategoryItem("llm", "AI assistance", "LLM providers, models, prompts.", "",
			new[] { "llm", "openai", "anthropic", "claude", "prompts", "models" }));
		_allCategories.Add(new SettingsCategoryItem("tts", "Text to Speech", "Voice playback and narration.", "",
			new[] { "tts", "text to speech", "voice", "rate", "volume", "preprocessing" }));
		_allCategories.Add(new SettingsCategoryItem("transcript", "Transcript formatting", "Find-and-replace rules applied by the Format button.", "",
			new[] { "transcript", "format", "find", "replace", "regex", "rule", "rules", "smart" }));
		_allCategories.Add(new SettingsCategoryItem("ui", "Interface", "Window layout and dictation insert behavior.", "",
			new[] { "ui", "interface", "max line", "dictation", "paste", "type" }));
		_allCategories.Add(new SettingsCategoryItem("hotkeys", "Hotkeys", "All keyboard shortcuts in one place.", "",
			new[] { "hotkey", "shortcut", "keys", "binding", "router" }));

		ApplySearchFilter(string.Empty);
	}

	// Resolve the list index of the category to open initially. Falls back to the
	// first category (index 0) when no key is supplied or it does not match, so the
	// dialog always opens on a real page. Used to deep-link callers (e.g. the
	// missing speech-to-text key warning) straight to the API keys tab.
	private int IndexOfCategory(string? categoryKey)
	{
		if (!string.IsNullOrWhiteSpace(categoryKey))
		{
			for (int i = 0; i < FilteredCategories.Count; i++)
			{
				if (string.Equals(FilteredCategories[i].Key, categoryKey, StringComparison.OrdinalIgnoreCase))
					return i;
			}
		}

		return 0;
	}

	private void ApplySearchFilter(string query)
	{
		FilteredCategories.Clear();
		string trimmed = (query ?? string.Empty).Trim();
		IEnumerable<SettingsCategoryItem> source = _allCategories;
		if (trimmed.Length > 0)
		{
			source = _allCategories.Where(c =>
				c.MatchesSearch(trimmed));
		}
		foreach (var c in source)
			FilteredCategories.Add(c);
	}

	private void CategoryList_SelectionChanged(object sender, SelectionChangedEventArgs e)
	{
		_selectedCategory = CategoryList.SelectedItem as SettingsCategoryItem;
		LoadSelectedPage();
	}

	private void LoadSelectedPage()
	{
		if (_selectedCategory is null)
		{
			ShowEmptyState();
			return;
		}

		ActiveCategoryHeader.Text = _selectedCategory.DisplayName;
		AutomationProperties.SetName(PageHost, _selectedCategory.DisplayName);

		_activePage = _selectedCategory.Key switch
		{
			"audio" => new AudioSettingsPage(_workingCopy),
			"ocr" => new OcrSettingsPage(_workingCopy),
			"speech" => new SpeechSettingsPage(_workingCopy),
			"apikeys" => new ApiKeysSettingsPage(_workingCopy),
			"llm" => new LlmSettingsPage(_workingCopy),
			"tts" => new TtsSettingsPage(_workingCopy),
			"transcript" => new TranscriptFormattingSettingsPage(_workingCopy),
			"ui" => new InterfaceSettingsPage(_workingCopy),
			"hotkeys" => new HotkeysSettingsPage(_workingCopy),
			_ => null,
		};

		PageHost.Content = _activePage;

		// Re-apply any active search query against the freshly loaded page so a user
		// who typed before switching tabs immediately sees matches on the new tab.
		if (_activePage is UserControl uc && uc.Content is Panel rootPanel
			&& !string.IsNullOrWhiteSpace(SearchBox?.Text))
		{
			// Defer until after the visual tree is populated — VisualTreeHelper requires a live tree.
			DispatcherQueue.TryEnqueue(() =>
			{
				int sectionMatches = SettingsSearchHelper.ApplyFilter(rootPanel, SearchBox.Text);
				UpdateSearchStatus(sectionMatches);
			});
		}
	}

	private void ShowEmptyState()
	{
		ActiveCategoryHeader.Text = "Settings";
		PageHost.Content = new TextBlock
		{
			Text = "Select a category from the list.",
			Style = (Style)Application.Current.Resources["BodyTextBlockStyle"],
			Foreground = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["TextFillColorSecondaryBrush"],
		};
	}

	private void SearchBox_TextChanged(object sender, TextChangedEventArgs e)
	{
		ApplySearchFilter(SearchBox.Text);
		if (FilteredCategories.Count > 0 && CategoryList.SelectedIndex < 0)
			CategoryList.SelectedIndex = 0;
		UpdateSearchStatus(FilterActivePage());
	}

	// Applies the current query to the active page, collapsing non-matching
	// sections. Returns the section match count, or null when no page is loaded.
	private int? FilterActivePage()
	{
		if (PageHost.Content is FrameworkElement page && page is UserControl uc && uc.Content is Panel rootPanel)
			return SettingsSearchHelper.ApplyFilter(rootPanel, SearchBox.Text ?? string.Empty);
		return null;
	}

	// Reflects the current match state in the status line under the search box.
	// The TextBlock is a polite live region, so updating its text announces the
	// result to a screen reader without moving focus.
	private void UpdateSearchStatus(int? sectionMatches)
	{
		string status = SettingsSearchStatus.Compose(
			SearchBox.Text, FilteredCategories.Count, _allCategories.Count, sectionMatches);
		SearchStatusText.Text = status;
		SearchStatusText.Visibility = status.Length == 0 ? Visibility.Collapsed : Visibility.Visible;
	}

	private void AdvancedToggle_Toggled(object sender, RoutedEventArgs e)
	{
		OpenJsonButton.Visibility = AdvancedToggle.IsOn ? Visibility.Visible : Visibility.Collapsed;
	}

	private void OpenJsonButton_Click(object sender, RoutedEventArgs e)
	{
		if (string.IsNullOrWhiteSpace(_settingsFilePath))
			return;
		try
		{
			var psi = new ProcessStartInfo(_settingsFilePath) { UseShellExecute = true };
			Process.Start(psi);
		}
		catch (Exception ex)
		{
			System.Diagnostics.Debug.WriteLine($"Failed to open settings JSON: {ex.Message}");
		}
	}

	private void SettingsDialog_PrimaryButtonClick(ContentDialog sender, ContentDialogButtonClickEventArgs args)
	{
		try
		{
			SettingsWorkingCopy.CommitInto(_live, _workingCopy);
			_settingsManager?.SaveSettingsToFile(_live);
			_onLiveApply?.Invoke();
		}
		catch (Exception ex)
		{
			args.Cancel = true;
			System.Diagnostics.Debug.WriteLine($"SettingsDialog save failed: {ex}");
		}
	}

	private XamlRoot? _sizingRoot;

	private void SetDialogSize()
	{
		HorizontalAlignment = HorizontalAlignment.Stretch;
		VerticalAlignment = VerticalAlignment.Stretch;

		Loaded += SettingsDialog_Loaded;
		Closed += SettingsDialog_Closed;
	}

	private void SettingsDialog_Loaded(object sender, RoutedEventArgs e)
	{
		if (XamlRoot is null)
			return;

		ApplyRootSize();

		_sizingRoot = XamlRoot;
		_sizingRoot.Changed += XamlRoot_Changed;
	}

	private void SettingsDialog_Closed(ContentDialog sender, ContentDialogClosedEventArgs args)
	{
		if (_sizingRoot is not null)
		{
			_sizingRoot.Changed -= XamlRoot_Changed;
			_sizingRoot = null;
		}
	}

	private void XamlRoot_Changed(XamlRoot sender, XamlRootChangedEventArgs args)
	{
		ApplyRootSize();
	}

	// Vertical space the ContentDialog template reserves for its own chrome
	// (title bar + Save/Cancel command area + dialog padding). The content Grid must
	// be sized to the window height MINUS this, otherwise the dialog grows taller than
	// the window and the bottom of the scrollable content is pushed off-screen / behind
	// the command bar (which hid the last controls on the tallest page).
	private const double DialogChromeHeight = 160d;

	// Horizontal space the ContentDialog template reserves (left/right padding + insets).
	// Mirrors DialogChromeHeight: without it RootGrid.Width = full window width pushes the
	// content's right edge off-screen, clipping the scrollbar gutter and the right side of
	// full-width controls (API key toggle, endpoint box, etc.).
	private const double DialogChromeWidth = 56d;

	private void ApplyRootSize()
	{
		if (XamlRoot is null || RootGrid is null)
			return;

		var bounds = XamlRoot.Size;
		RootGrid.Width = Math.Max(0d, bounds.Width - DialogChromeWidth);
		RootGrid.Height = Math.Max(0d, bounds.Height - DialogChromeHeight);
	}
}
