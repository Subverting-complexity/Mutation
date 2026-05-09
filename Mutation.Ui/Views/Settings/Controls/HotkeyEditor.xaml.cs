using System;
using System.Collections.Generic;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
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

	private bool _isRecording;
	private bool _suppressTextChanged;

	public HotkeyEditor()
	{
		InitializeComponent();
		UpdateHeaderVisibility();
	}

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

	private void UpdateHeaderVisibility()
	{
		HeaderText.Visibility = string.IsNullOrEmpty(Header)
			? Visibility.Collapsed
			: Visibility.Visible;
	}

	private void HotkeyTextBox_TextChanged(object sender, TextChangedEventArgs e)
	{
		if (_suppressTextChanged) return;
		Validate(HotkeyTextBox.Text);
	}

	private void HotkeyTextBox_LostFocus(object sender, RoutedEventArgs e)
	{
		StopRecording();
		Commit();
	}

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
		if (_isRecording)
			StopRecording();
		else
			StartRecording();
	}

	private void ClearButton_Click(object sender, RoutedEventArgs e)
	{
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

	private void Validate(string text)
	{
		string trimmed = text?.Trim() ?? string.Empty;
		if (string.IsNullOrEmpty(trimmed))
		{
			if (AllowEmpty)
			{
				ValidationText.Visibility = Visibility.Collapsed;
				ValidationText.Text = string.Empty;
			}
			else
			{
				ValidationText.Visibility = Visibility.Visible;
				ValidationText.Text = "Enter a hotkey.";
			}
			return;
		}

		try
		{
			_ = Mutation.Ui.Services.Hotkey.Parse(trimmed);
			ValidationText.Visibility = Visibility.Collapsed;
			ValidationText.Text = string.Empty;
		}
		catch (Exception ex)
		{
			ValidationText.Visibility = Visibility.Visible;
			ValidationText.Text = ex.Message;
		}
	}

	private void Commit()
	{
		string normalized = Normalize(HotkeyTextBox.Text);
		if (!string.Equals(normalized, HotkeyTextBox.Text, StringComparison.Ordinal))
		{
			_suppressTextChanged = true;
			try { HotkeyTextBox.Text = normalized; }
			finally { _suppressTextChanged = false; }
		}

		if (!string.Equals(Hotkey, normalized, StringComparison.Ordinal))
			Hotkey = normalized;

		HotkeyCommitted?.Invoke(this, normalized);
	}

	private static string Normalize(string? value)
	{
		if (string.IsNullOrWhiteSpace(value))
			return string.Empty;

		var parts = value.Split(Mutation.Ui.Services.Hotkey.TokenSeparators,
			StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
		var normalized = new List<string>(parts.Length);
		foreach (var p in parts)
			normalized.Add(p.ToUpperInvariant());
		return string.Join('+', normalized);
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

	private static string FormatHotkey(VirtualKeyModifiers mods, VirtualKey key)
	{
		var parts = new List<string>();
		if ((mods & VirtualKeyModifiers.Control) != 0) parts.Add("CTRL");
		if ((mods & VirtualKeyModifiers.Shift) != 0) parts.Add("SHIFT");
		if ((mods & VirtualKeyModifiers.Menu) != 0) parts.Add("ALT");
		if ((mods & VirtualKeyModifiers.Windows) != 0) parts.Add("WIN");

		string keyName = key.ToString();
		if (keyName.StartsWith("Number", StringComparison.Ordinal) && keyName.Length == 7)
			keyName = keyName.Substring(6);
		parts.Add(keyName.ToUpperInvariant());
		return string.Join('+', parts);
	}
}
