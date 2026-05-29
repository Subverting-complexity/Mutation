using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Mutation.Ui.Views.SettingsUi.Controls;

public sealed partial class InfoHint : UserControl
{
	public static readonly DependencyProperty TextProperty = DependencyProperty.Register(
		nameof(Text), typeof(string), typeof(InfoHint),
		new PropertyMetadata(string.Empty));

	public InfoHint()
	{
		InitializeComponent();
	}

	public string Text
	{
		get => (string)GetValue(TextProperty);
		set => SetValue(TextProperty, value ?? string.Empty);
	}

	private void OnGotFocus(object sender, RoutedEventArgs e)
	{
		if (!string.IsNullOrEmpty(Text))
			HintToolTip.IsOpen = true;
	}

	private void OnLostFocus(object sender, RoutedEventArgs e)
	{
		HintToolTip.IsOpen = false;
	}
}
