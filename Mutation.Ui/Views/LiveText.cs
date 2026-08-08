using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Mutation.Ui.Views;

/// <summary>
/// <see cref="LiveMessage"/> for a <see cref="TextBlock"/> that lives inside a data template.
/// <para>
/// Every other live region in this app is a named part of a control, so its code-behind can
/// call <see cref="LiveMessage.Show"/> on it directly. A row in a <c>ListView</c> has no code-
/// behind and no name to reach it by — binding <c>Text</c> is the only way in, and a bound
/// <c>Text</c> is exactly the case WinUI announces nothing for (issue #332). Binding this
/// instead routes the same value through the same one place that shows a message and then
/// raises the event that makes it heard.
/// </para>
/// <para>
/// Public because XAML markup has to reach it, unlike the internal helper behind it.
/// </para>
/// </summary>
public static class LiveText
{
	public static readonly DependencyProperty MessageProperty = DependencyProperty.RegisterAttached(
		"Message", typeof(string), typeof(LiveText), new PropertyMetadata(null, OnMessageChanged));

	public static string? GetMessage(DependencyObject element) =>
		(string?)element.GetValue(MessageProperty);

	public static void SetMessage(DependencyObject element, string? value) =>
		element.SetValue(MessageProperty, value);

	private static void OnMessageChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
	{
		if (d is TextBlock block)
			LiveMessage.Show(block, e.NewValue as string);
	}
}
