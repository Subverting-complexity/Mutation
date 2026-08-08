using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Mutation.Ui.Views;

/// <summary>
/// <see cref="LiveMessage"/> for a <see cref="TextBlock"/> that only a binding can reach.
/// <para>
/// A row inside a <c>DataTemplate</c> has no name to call it by from code-behind, so the
/// hotkey router's rows could not use the one-line <c>LiveMessage.Show</c> the settings
/// editors use. Binding <c>Text</c> directly is not enough on its own: WinUI raises nothing
/// when a TextBlock's text changes, so a message bound into a live region is displayed and
/// never spoken (issue #243). Binding this instead routes the same value through the same
/// place, which sets the text, the visibility, and raises the event by hand.
/// </para>
/// </summary>
public static class LiveRegion
{
	/// <summary>
	/// The message to show and announce, or blank to take the last one down. Set it with a
	/// binding — <c>views:LiveRegion.Text="{x:Bind SomeMessage, Mode=OneWay}"</c> — on a
	/// TextBlock that also carries <c>AutomationProperties.LiveSetting</c>. Without the live
	/// setting the block is announced by nothing; without this the text never changes.
	/// </summary>
	public static readonly DependencyProperty TextProperty = DependencyProperty.RegisterAttached(
		"Text", typeof(string), typeof(LiveRegion), new PropertyMetadata(null, OnTextChanged));

	public static string? GetText(DependencyObject element) => (string?)element.GetValue(TextProperty);

	public static void SetText(DependencyObject element, string? value) => element.SetValue(TextProperty, value);

	private static void OnTextChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
	{
		if (d is TextBlock block)
			LiveMessage.Show(block, e.NewValue as string);
	}
}
