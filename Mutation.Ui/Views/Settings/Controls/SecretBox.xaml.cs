using System;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Mutation.Ui.Views.SettingsUi.Controls;

public sealed partial class SecretBox : UserControl
{
	public static readonly DependencyProperty SecretProperty = DependencyProperty.Register(
		nameof(Secret), typeof(string), typeof(SecretBox),
		new PropertyMetadata(string.Empty, OnSecretChanged));

	public static readonly DependencyProperty HeaderProperty = DependencyProperty.Register(
		nameof(Header), typeof(string), typeof(SecretBox),
		new PropertyMetadata(string.Empty, OnHeaderChanged));

	public static readonly DependencyProperty PlaceholderTextProperty = DependencyProperty.Register(
		nameof(PlaceholderText), typeof(string), typeof(SecretBox),
		new PropertyMetadata(string.Empty));

	public static readonly DependencyProperty HelpTextProperty = DependencyProperty.Register(
		nameof(HelpText), typeof(string), typeof(SecretBox),
		new PropertyMetadata(string.Empty, OnHelpTextChanged));

	private bool _suppressChanges;

	public SecretBox()
	{
		InitializeComponent();
		UpdateHeaderVisibility();
		UpdateHelpVisibility();
	}

	public string Secret
	{
		get => (string)GetValue(SecretProperty);
		set => SetValue(SecretProperty, value ?? string.Empty);
	}

	public string Header
	{
		get => (string)GetValue(HeaderProperty);
		set => SetValue(HeaderProperty, value ?? string.Empty);
	}

	public string PlaceholderText
	{
		get => (string)GetValue(PlaceholderTextProperty);
		set => SetValue(PlaceholderTextProperty, value ?? string.Empty);
	}

	public string HelpText
	{
		get => (string)GetValue(HelpTextProperty);
		set => SetValue(HelpTextProperty, value ?? string.Empty);
	}

	public Visibility HeaderVisibility =>
		string.IsNullOrEmpty(Header) ? Visibility.Collapsed : Visibility.Visible;

	private static void OnSecretChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
	{
		if (d is not SecretBox box) return;
		string newValue = (e.NewValue as string) ?? string.Empty;
		box._suppressChanges = true;
		try
		{
			if (!string.Equals(box.HiddenBox.Password, newValue, StringComparison.Ordinal))
				box.HiddenBox.Password = newValue;
			if (!string.Equals(box.VisibleBox.Text, newValue, StringComparison.Ordinal))
				box.VisibleBox.Text = newValue;
		}
		finally { box._suppressChanges = false; }
	}

	private static void OnHeaderChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
	{
		if (d is SecretBox box) box.UpdateHeaderVisibility();
	}

	private static void OnHelpTextChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
	{
		if (d is SecretBox box) box.UpdateHelpVisibility();
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

	private void HiddenBox_PasswordChanged(object sender, RoutedEventArgs e)
	{
		if (_suppressChanges) return;
		Secret = HiddenBox.Password;
	}

	private void VisibleBox_TextChanged(object sender, TextChangedEventArgs e)
	{
		if (_suppressChanges) return;
		Secret = VisibleBox.Text;
	}

	private void RevealToggle_Toggled(object sender, RoutedEventArgs e)
	{
		bool reveal = RevealToggle.IsChecked == true;
		_suppressChanges = true;
		try
		{
			if (reveal)
			{
				VisibleBox.Text = HiddenBox.Password;
				VisibleBox.Visibility = Visibility.Visible;
				HiddenBox.Visibility = Visibility.Collapsed;
			}
			else
			{
				HiddenBox.Password = VisibleBox.Text;
				HiddenBox.Visibility = Visibility.Visible;
				VisibleBox.Visibility = Visibility.Collapsed;
			}
		}
		finally { _suppressChanges = false; }
	}
}
