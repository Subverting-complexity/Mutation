using System;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Mutation.Ui.Core;
using Mutation.Ui.Services;
using Windows.System;

namespace Mutation.Ui.Views.SettingsUi.Controls;

public sealed partial class HotkeyEditor : UserControl
{
	public static readonly DependencyProperty HotkeyProperty = DependencyProperty.Register(
		nameof(Hotkey), typeof(string), typeof(HotkeyEditor),
		new PropertyMetadata(string.Empty, OnHotkeyChanged));

	public static readonly DependencyProperty HeaderProperty = DependencyProperty.Register(
		nameof(Header), typeof(string), typeof(HotkeyEditor),
		new PropertyMetadata(string.Empty, OnHeaderChanged));

	public static readonly DependencyProperty AllowEmptyProperty = DependencyProperty.Register(
		nameof(AllowEmpty), typeof(bool), typeof(HotkeyEditor),
		new PropertyMetadata(false));

	public static readonly DependencyProperty AllowSendKeysSyntaxProperty = DependencyProperty.Register(
		nameof(AllowSendKeysSyntax), typeof(bool), typeof(HotkeyEditor),
		new PropertyMetadata(false));

	public static readonly DependencyProperty HelpTextProperty = DependencyProperty.Register(
		nameof(HelpText), typeof(string), typeof(HotkeyEditor),
		new PropertyMetadata(string.Empty, OnHelpTextChanged));

	private bool _isRecording;
	private bool _suppressTextChanged;

	public HotkeyEditor()
	{
		InitializeComponent();
		UpdateHeaderVisibility();
		UpdateHelpVisibility();
	}

	/// <summary>
	/// Whether the page holding this editor has been used yet. Until it has, this row's
	/// validation message is written on screen but not read out — a settings page full of
	/// required-but-empty rows would otherwise greet the user with one "Enter a hotkey."
	/// interruption per row, about settings nobody had touched (issue #350).
	/// <para>
	/// Left null by any page that does not care, and then the message announces as it always
	/// did. Editors on pages whose rows practically always have values were quiet in practice
	/// anyway; the Hotkeys page is the one where it showed.
	/// </para>
	/// </summary>
	internal FirstTouchGate? AnnouncementGate { get; set; }

	public string Hotkey
	{
		get => (string)GetValue(HotkeyProperty);
		set => SetValue(HotkeyProperty, value ?? string.Empty);
	}

	public string Header
	{
		get => (string)GetValue(HeaderProperty);
		set => SetValue(HeaderProperty, value ?? string.Empty);
	}

	public bool AllowEmpty
	{
		get => (bool)GetValue(AllowEmptyProperty);
		set => SetValue(AllowEmptyProperty, value);
	}

	public bool AllowSendKeysSyntax
	{
		get => (bool)GetValue(AllowSendKeysSyntaxProperty);
		set => SetValue(AllowSendKeysSyntaxProperty, value);
	}

	public string HelpText
	{
		get => (string)GetValue(HelpTextProperty);
		set => SetValue(HelpTextProperty, value ?? string.Empty);
	}

	public Visibility HeaderVisibility =>
		string.IsNullOrEmpty(Header) ? Visibility.Collapsed : Visibility.Visible;

	public event EventHandler<string>? HotkeyCommitted;

	private static void OnHotkeyChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
	{
		if (d is not HotkeyEditor editor)
			return;
		string newValue = (e.NewValue as string) ?? string.Empty;
		if (string.Equals(editor.HotkeyTextBox.Text, newValue, StringComparison.Ordinal))
		{
			editor.Validate(newValue);
			return;
		}

		editor._suppressTextChanged = true;
		try { editor.HotkeyTextBox.Text = newValue; }
		finally { editor._suppressTextChanged = false; }
		editor.Validate(newValue);
	}

	private static void OnHeaderChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
	{
		if (d is HotkeyEditor editor)
			editor.UpdateHeaderVisibility();
	}

	private static void OnHelpTextChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
	{
		if (d is HotkeyEditor editor)
			editor.UpdateHelpVisibility();
	}

	private void UpdateHeaderVisibility()
	{
		HeaderRow.Visibility = string.IsNullOrEmpty(Header)
			? Visibility.Collapsed
			: Visibility.Visible;
	}

	private void UpdateHelpVisibility()
	{
		HelpHint.Visibility = string.IsNullOrEmpty(HelpText)
			? Visibility.Collapsed
			: Visibility.Visible;
	}

	private void HotkeyTextBox_TextChanged(object sender, TextChangedEventArgs e)
	{
		if (_suppressTextChanged) return;

		// Every programmatic write to this box suppresses this handler, so reaching here means
		// the user typed. That is what opens the page's mouth — merely tabbing through a row
		// does not (issue #350).
		AnnouncementGate?.Touch();
		Validate(HotkeyTextBox.Text);
	}

	private void HotkeyTextBox_LostFocus(object sender, RoutedEventArgs e)
	{
		StopRecording();
		Commit();
	}

	/// <summary>
	/// Takes down the last commit's notice on the way back in. It described something that
	/// happened when the user left, and left standing it becomes a line of ordinary-looking
	/// text under the row — read out as current content by anyone going down the page
	/// afterwards, and one more thing between them and the next control. Clearing it also
	/// means the same rewrite happening twice is announced twice, rather than the second one
	/// being swallowed as an unchanged message.
	/// </summary>
	private void HotkeyTextBox_GotFocus(object sender, RoutedEventArgs e) =>
		LiveMessage.Show(CommitAnnouncement, null);

	private void HotkeyTextBox_KeyDown(object sender, KeyRoutedEventArgs e)
	{
		if (!_isRecording)
			return;

		if (e.Key == VirtualKey.Escape)
		{
			e.Handled = true;
			StopRecording();
			return;
		}

		if (IsModifierKey(e.Key))
		{
			e.Handled = true;
			return;
		}

		var modifiers = GetCurrentModifiers();
		string formatted = FormatHotkey(modifiers, e.Key);
		if (!string.IsNullOrEmpty(formatted))
		{
			_suppressTextChanged = true;
			try { HotkeyTextBox.Text = formatted; }
			finally { _suppressTextChanged = false; }
			Validate(formatted);
			Commit();
			StopRecording();
		}
		e.Handled = true;
	}

	private void RecordButton_Click(object sender, RoutedEventArgs e)
	{
		AnnouncementGate?.Touch();

		if (_isRecording)
			StopRecording();
		else
			StartRecording();
	}

	private void ClearButton_Click(object sender, RoutedEventArgs e)
	{
		AnnouncementGate?.Touch();

		_suppressTextChanged = true;
		try { HotkeyTextBox.Text = string.Empty; }
		finally { _suppressTextChanged = false; }
		Validate(string.Empty);
		Commit();
	}

	private void StartRecording()
	{
		_isRecording = true;
		HotkeyTextBox.Focus(FocusState.Programmatic);
		HotkeyTextBox.SelectAll();
		RecordLabel.Text = "Press keys...";
	}

	private void StopRecording()
	{
		if (!_isRecording) return;
		_isRecording = false;
		RecordLabel.Text = "Record";
	}

	/// <summary>
	/// Shows what is wrong with the text in the box, or clears the message when it is fine.
	/// <para>
	/// Routed through <see cref="LiveMessage"/> so the message is actually announced. It
	/// carried the live setting and nothing else, which the user guide has been describing as
	/// "screen readers announce it straight away" — true only with the event raised (issue
	/// #243).
	/// </para>
	/// </summary>
	/// <remarks>
	/// Shown either way, announced only once the page has been used. Setting a row's
	/// <c>Hotkey</c> while the page is being built runs straight through here, so without the
	/// gate a page of required-but-empty rows reads them all out on the way in (issue #350).
	/// </remarks>
	private void Validate(string text) =>
		LiveMessage.Show(ValidationText, ValidationMessageFor(text), AnnouncementAllowed);

	/// <summary>
	/// Asked at the moment the message would be raised rather than when it is set, so a page
	/// used during the same dispatcher pass is heard rather than swallowed.
	/// </summary>
	private bool AnnouncementAllowed() => AnnouncementGate is null || AnnouncementGate.HasBeenTouched;

	/// <summary>What is wrong with <paramref name="text"/>, or null when nothing is.</summary>
	private string? ValidationMessageFor(string? text)
	{
		string trimmed = text?.Trim() ?? string.Empty;
		if (string.IsNullOrEmpty(trimmed))
			return AllowEmpty ? null : "Enter a hotkey.";

		return HotkeyValidator.Validate(trimmed, AllowSendKeysSyntax);
	}

	private void Commit()
	{
		string typed = HotkeyTextBox.Text ?? string.Empty;

		// SendKeys syntax is not a chord, so it is kept exactly as typed. Anything else goes
		// through the one canonical spelling the app uses everywhere (issue #323).
		string committed = AllowSendKeysSyntax
			? typed.Trim()
			: Mutation.Ui.Services.Hotkey.Canonicalize(typed);

		bool rewritten = !string.Equals(committed, typed, StringComparison.Ordinal);
		if (rewritten)
		{
			// A rewrite changes what this row holds, which can make it collide with another row,
			// and it happens without the user typing a character — tabbing out of a box holding a
			// hand-edited spelling is enough. Without this the resulting "Duplicate hotkey" badge
			// was written and never spoken (issue #350).
			AnnouncementGate?.Touch();

			_suppressTextChanged = true;
			try { HotkeyTextBox.Text = committed; }
			finally { _suppressTextChanged = false; }
		}

		// Suppressing TextChanged also suppressed everything downstream of it, so the box
		// could rewrite its own contents in complete silence and leave the validation message
		// describing text that is no longer there (issue #327).
		string? validation = ValidationMessageFor(committed);

		// Only a rewrite can have left that message stale, and only a rewrite refreshes it.
		// Doing it on every commit would newly complain at a required row that is empty and
		// untouched — of which there are plenty — every single time the user tabbed past it.
		// Through the gate like every other validation message, so one page has one set of
		// manners. In practice the gate is already open by now — a rewrite opened it above — but
		// leaving this path ungated meant a whitespace-only box, tabbed into and straight out of,
		// announced "Enter a hotkey." on a page nobody had touched.
		if (rewritten)
			LiveMessage.Show(ValidationText, validation, AnnouncementAllowed);

		if (!string.Equals(Hotkey, committed, StringComparison.Ordinal))
			Hotkey = committed;

		HotkeyCommitted?.Invoke(this, committed);

		// Last, and deliberately so. The Hotkeys page recomputes duplicates off this event
		// and puts "Duplicate hotkey" into an assertive live region of its own. Announced
		// before that, a polite notice is cut off mid-sentence by the assertive one that
		// follows it onto the same dispatcher pass — and the clash is precisely the case
		// where both have something to say. Announced after, the urgent news goes first and
		// this waits its turn.
		LiveMessage.Show(
			CommitAnnouncement,
			HotkeyCommitAnnouncement.For(Header, typed, committed, validation));
	}

	private static VirtualKeyModifiers GetCurrentModifiers()
	{
		var mods = VirtualKeyModifiers.None;
		if (IsKeyDown(VirtualKey.Control)) mods |= VirtualKeyModifiers.Control;
		if (IsKeyDown(VirtualKey.Menu)) mods |= VirtualKeyModifiers.Menu;
		if (IsKeyDown(VirtualKey.Shift)) mods |= VirtualKeyModifiers.Shift;
		if (IsKeyDown(VirtualKey.LeftWindows) || IsKeyDown(VirtualKey.RightWindows))
			mods |= VirtualKeyModifiers.Windows;
		return mods;
	}

	private static bool IsKeyDown(VirtualKey key)
	{
		var state = Microsoft.UI.Input.InputKeyboardSource.GetKeyStateForCurrentThread(key);
		return (state & Windows.UI.Core.CoreVirtualKeyStates.Down) == Windows.UI.Core.CoreVirtualKeyStates.Down;
	}

	private static bool IsModifierKey(VirtualKey key) =>
		key is VirtualKey.Control or VirtualKey.LeftControl or VirtualKey.RightControl
			or VirtualKey.Shift or VirtualKey.LeftShift or VirtualKey.RightShift
			or VirtualKey.Menu or VirtualKey.LeftMenu or VirtualKey.RightMenu
			or VirtualKey.LeftWindows or VirtualKey.RightWindows;

	/// <summary>
	/// The recorded chord as text, or nothing when there is no key to record. Built through
	/// <see cref="Mutation.Ui.Services.Hotkey"/> so what the box shows is the same canonical
	/// spelling registration and duplicate detection use, rather than a third copy of the same
	/// formatting rules (issue #306). The caller ignores an empty result, which leaves the box
	/// as it was rather than committing a placeholder that is not a shortcut.
	/// </summary>
	private static string FormatHotkey(VirtualKeyModifiers mods, VirtualKey key)
	{
		if (key == VirtualKey.None)
			return string.Empty;

		return new Mutation.Ui.Services.Hotkey
		{
			Control = (mods & VirtualKeyModifiers.Control) != 0,
			Shift = (mods & VirtualKeyModifiers.Shift) != 0,
			Alt = (mods & VirtualKeyModifiers.Menu) != 0,
			Win = (mods & VirtualKeyModifiers.Windows) != 0,
			Key = key,
		}.ToString();
	}
}
