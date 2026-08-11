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
		"Message", typeof(string), typeof(LiveText), new PropertyMetadata(null, OnLiveValueChanged));

	/// <summary>
	/// Bind this true to put the message on screen without reading it out. The Hotkeys page
	/// uses it for rows built straight from settings, which can already be carrying an error
	/// before the user has seen the page at all — one assertive interruption per stored bad row
	/// on the way in (issue #350). The written half is unaffected either way, because a sighted
	/// reader was never the problem.
	/// </summary>
	public static readonly DependencyProperty MutedProperty = DependencyProperty.RegisterAttached(
		"Muted", typeof(bool), typeof(LiveText), new PropertyMetadata(false, OnLiveValueChanged));

	public static string? GetMessage(DependencyObject element) =>
		(string?)element.GetValue(MessageProperty);

	public static void SetMessage(DependencyObject element, string? value) =>
		element.SetValue(MessageProperty, value);

	public static bool GetMuted(DependencyObject element) =>
		(bool)element.GetValue(MutedProperty);

	public static void SetMuted(DependencyObject element, bool value) =>
		element.SetValue(MutedProperty, value);

	/// <summary>
	/// One handler for both properties, so the order the row's bindings happen to apply in does
	/// not decide whether the message is heard. Whichever of the two lands second re-runs this
	/// with both settled values, and <see cref="LiveMessage"/> asks about muting later still —
	/// at the moment it would raise, once the whole binding pass is behind it.
	/// </summary>
	private static void OnLiveValueChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
	{
		if (d is TextBlock block)
			LiveMessage.Show(block, GetMessage(block), () => !GetMuted(block));
	}
}
