using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using CognitiveSupport;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Mutation.Ui.Core;
using Mutation.Ui.Services;
using Mutation.Ui.Views;
using Mutation.Ui.Views.SettingsUi.Controls;

namespace Mutation.Ui.Views.SettingsUi.Pages;

public sealed partial class HotkeysSettingsPage : UserControl
{
	private const string DuplicateBadgeText = "Duplicate hotkey";

	private readonly Settings _settings;
	private readonly List<HotkeyRow> _rows = new();
	private readonly HotkeyRouterController _routerController;

	public ObservableCollection<Mutation.Ui.HotkeyRouterEntry> HotkeyRouterEntries { get; } = new();

	public HotkeysSettingsPage(Settings settings)
	{
		_settings = settings;
		InitializeComponent();
		BuildRows();
		RecomputeDuplicates();

		_routerController = new HotkeyRouterController(
			HotkeyRouterEntries,
			_settings,
			settingsManager: null,
			DispatcherQueue.GetForCurrentThread(),
			HotkeyRouterList,
			autoPersist: false);
		_routerController.Initialize();
	}

	/// <param name="Registers">
	/// Whether this shortcut is claimed from Windows. The "Send key after…" entries are typed
	/// out to whichever app is in front rather than registered, so two of them holding the same
	/// key is an ordinary way to set the app up and not a conflict — it used to be reported as
	/// one (issue #306). They are still checked against the shortcuts Mutation does claim,
	/// because Windows routes a synthesized keystroke back to whoever registered it.
	/// </param>
	private sealed record HotkeySpec(
		string Label,
		Func<Settings, string?> Getter,
		Action<Settings, string?> Setter,
		bool AllowEmpty,
		string? Default = null,
		bool Registers = true);

	private sealed class HotkeyRow
	{
		public required HotkeySpec Spec { get; init; }
		public required HotkeyEditor Editor { get; init; }
		public required Border Container { get; init; }
		public required TextBlock DuplicateBadge { get; init; }
	}

	private static readonly HotkeySpec[] Specs = new[]
	{
		new HotkeySpec("Toggle microphone mute",
			s => s.AudioSettings?.MicrophoneToggleMuteHotKey,
			(s, v) => (s.AudioSettings ??= new AudioSettings()).MicrophoneToggleMuteHotKey = v,
			false, SettingsDefaults.Audio.MicrophoneToggleMuteHotKey),

		new HotkeySpec("Take screenshot",
			s => s.AzureComputerVisionSettings?.ScreenshotHotKey,
			(s, v) => (s.AzureComputerVisionSettings ??= new AzureComputerVisionSettings()).ScreenshotHotKey = v,
			false, SettingsDefaults.Ocr.ScreenshotHotKey),
		new HotkeySpec("Screenshot + OCR",
			s => s.AzureComputerVisionSettings?.ScreenshotOcrHotKey,
			(s, v) => (s.AzureComputerVisionSettings ??= new AzureComputerVisionSettings()).ScreenshotOcrHotKey = v,
			false, SettingsDefaults.Ocr.ScreenshotOcrHotKey),
		new HotkeySpec("Screenshot + OCR (left-to-right)",
			s => s.AzureComputerVisionSettings?.ScreenshotLeftToRightTopToBottomOcrHotKey,
			(s, v) => (s.AzureComputerVisionSettings ??= new AzureComputerVisionSettings()).ScreenshotLeftToRightTopToBottomOcrHotKey = v,
			false, SettingsDefaults.Ocr.ScreenshotLeftToRightTopToBottomOcrHotKey),
		new HotkeySpec("OCR clipboard",
			s => s.AzureComputerVisionSettings?.OcrHotKey,
			(s, v) => (s.AzureComputerVisionSettings ??= new AzureComputerVisionSettings()).OcrHotKey = v,
			false, SettingsDefaults.Ocr.OcrHotKey),
		new HotkeySpec("OCR clipboard (left-to-right)",
			s => s.AzureComputerVisionSettings?.OcrLeftToRightTopToBottomHotKey,
			(s, v) => (s.AzureComputerVisionSettings ??= new AzureComputerVisionSettings()).OcrLeftToRightTopToBottomHotKey = v,
			false, SettingsDefaults.Ocr.OcrLeftToRightTopToBottomHotKey),
		new HotkeySpec("Send key after OCR (optional)",
			s => s.AzureComputerVisionSettings?.SendHotkeyAfterOcrOperation,
			(s, v) => (s.AzureComputerVisionSettings ??= new AzureComputerVisionSettings()).SendHotkeyAfterOcrOperation = v,
			true, null, Registers: false),

		new HotkeySpec("Speech to text",
			s => s.SpeechToTextSettings?.SpeechToTextHotKey,
			(s, v) => (s.SpeechToTextSettings ??= new SpeechToTextSettings()).SpeechToTextHotKey = v,
			false, SettingsDefaults.Speech.SpeechToTextHotKey),
		new HotkeySpec("Speech to text + process with LLM",
			s => s.SpeechToTextSettings?.SpeechToTextWithLlmProcessingHotKey,
			(s, v) => (s.SpeechToTextSettings ??= new SpeechToTextSettings()).SpeechToTextWithLlmProcessingHotKey = v,
			false, SettingsDefaults.Speech.SpeechToTextWithLlmProcessingHotKey),
		new HotkeySpec("Send key after transcription (optional)",
			s => s.SpeechToTextSettings?.SendHotkeyAfterTranscriptionOperation,
			(s, v) => (s.SpeechToTextSettings ??= new SpeechToTextSettings()).SendHotkeyAfterTranscriptionOperation = v,
			true, null, Registers: false),

		new HotkeySpec("Speak clipboard",
			s => s.TextToSpeechSettings?.SpeakClipboard,
			(s, v) => (s.TextToSpeechSettings ??= new TextToSpeechSettings()).SpeakClipboard = v,
			false, SettingsDefaults.Tts.SpeakClipboard),
		new HotkeySpec("Speak selection",
			s => s.TextToSpeechSettings?.SpeakSelectionHotKey,
			(s, v) => (s.TextToSpeechSettings ??= new TextToSpeechSettings()).SpeakSelectionHotKey = v,
			false, SettingsDefaults.Tts.SpeakSelectionHotKey),
		new HotkeySpec("Restart speech from beginning",
			s => s.TextToSpeechSettings?.RestartFromBeginningHotKey,
			(s, v) => (s.TextToSpeechSettings ??= new TextToSpeechSettings()).RestartFromBeginningHotKey = v,
			false, SettingsDefaults.Tts.RestartFromBeginningHotKey),
		new HotkeySpec("Skip sentence backward",
			s => s.TextToSpeechSettings?.SkipSentenceBackwardHotKey,
			(s, v) => (s.TextToSpeechSettings ??= new TextToSpeechSettings()).SkipSentenceBackwardHotKey = v,
			false, SettingsDefaults.Tts.SkipSentenceBackwardHotKey),
		new HotkeySpec("Skip sentence forward",
			s => s.TextToSpeechSettings?.SkipSentenceForwardHotKey,
			(s, v) => (s.TextToSpeechSettings ??= new TextToSpeechSettings()).SkipSentenceForwardHotKey = v,
			false, SettingsDefaults.Tts.SkipSentenceForwardHotKey),
		new HotkeySpec("Speak reading position",
			s => s.TextToSpeechSettings?.SpeakPositionHotKey,
			(s, v) => (s.TextToSpeechSettings ??= new TextToSpeechSettings()).SpeakPositionHotKey = v,
			false, SettingsDefaults.Tts.SpeakPositionHotKey),
		new HotkeySpec("Pause or resume reading",
			s => s.TextToSpeechSettings?.PauseResumeHotKey,
			(s, v) => (s.TextToSpeechSettings ??= new TextToSpeechSettings()).PauseResumeHotKey = v,
			false, SettingsDefaults.Tts.PauseResumeHotKey),
	};

	private void BuildRows()
	{
		HotkeyList.Children.Clear();
		_rows.Clear();

		foreach (var spec in Specs)
		{
			var editor = new HotkeyEditor
			{
				Header = spec.Label,
				AllowEmpty = spec.AllowEmpty,
				Hotkey = spec.Getter(_settings) ?? string.Empty,
			};
			var dupBadge = new TextBlock
			{
				Text = string.Empty,
				Foreground = new SolidColorBrush(Microsoft.UI.Colors.OrangeRed),
				Visibility = Visibility.Collapsed,
				Style = (Style)Application.Current.Resources["CaptionTextBlockStyle"],
			};
			// Announced the moment it appears — LiveMessage does the raising, this only marks
			// the text as worth announcing. A warning only a sighted user notices is no
			// warning here.
			AutomationProperties.SetLiveSetting(dupBadge, AutomationLiveSetting.Assertive);

			Grid editorRow = new() { ColumnSpacing = 8 };
			editorRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
			editorRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

			Grid.SetColumn(editor, 0);
			editorRow.Children.Add(editor);

			if (spec.Default is not null)
			{
				var resetBtn = SettingsResetButton.Create($"Reset to default ({spec.Default})", () =>
				{
					editor.Hotkey = spec.Default!;
					spec.Setter(_settings, spec.Default);
					RecomputeDuplicates();
				});
				resetBtn.VerticalAlignment = VerticalAlignment.Bottom;
				resetBtn.Margin = new Thickness(0, 0, 0, 4);
				Grid.SetColumn(resetBtn, 1);
				editorRow.Children.Add(resetBtn);
			}

			var stack = new StackPanel { Spacing = 2 };
			stack.Children.Add(editorRow);
			stack.Children.Add(dupBadge);
			var border = new Border
			{
				Padding = new Thickness(8),
				CornerRadius = new CornerRadius(8),
				Child = stack,
			};

			var row = new HotkeyRow { Spec = spec, Editor = editor, Container = border, DuplicateBadge = dupBadge };
			editor.HotkeyCommitted += (_, value) =>
			{
				spec.Setter(_settings, string.IsNullOrWhiteSpace(value) ? null : value);
				RecomputeDuplicates();
			};

			HotkeyList.Children.Add(border);
			_rows.Add(row);
		}
	}

	private void RecomputeDuplicates()
	{
		var configured = new List<HotkeyConflictFinder.ConfiguredHotkey>(_rows.Count);
		foreach (var row in _rows)
			configured.Add(new(row.Spec.Getter(_settings), row.Spec.Registers));

		var duplicates = HotkeyConflictFinder.DuplicateIndexes(configured);

		for (int i = 0; i < _rows.Count; i++)
			ShowDuplicateBadge(_rows[i].DuplicateBadge, duplicates.Contains(i));
	}

	private static void ShowDuplicateBadge(TextBlock badge, bool isDuplicate) =>
		LiveMessage.Show(badge, isDuplicate ? DuplicateBadgeText : null);

	private void BtnAddHotkeyRoute_Click(object sender, RoutedEventArgs e) =>
		_routerController.AddNewMapping();

	private void HotkeyRouterDelete_Click(object sender, RoutedEventArgs e) =>
		_routerController.DeleteMapping(sender);

	private void HotkeyRouterFrom_LostFocus(object sender, RoutedEventArgs e) =>
		_routerController.CommitFromLostFocus(sender);

	private void HotkeyRouterTo_LostFocus(object sender, RoutedEventArgs e) =>
		_routerController.CommitToLostFocus(sender);
}
