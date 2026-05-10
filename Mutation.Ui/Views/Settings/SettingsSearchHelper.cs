using System.Collections.Generic;
using System.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace Mutation.Ui.Views.SettingsUi;

// Walks the top-level children of a settings page's root Panel, gathers the
// visible text underneath each child, and dims children whose text doesn't
// contain the query. Empty query restores full opacity.
internal static class SettingsSearchHelper
{
	private const double DimmedOpacity = 0.35;

	public static void ApplyFilter(Panel root, string query)
	{
		string q = (query ?? string.Empty).Trim().ToLowerInvariant();
		bool noQuery = q.Length == 0;

		foreach (UIElement child in root.Children)
		{
			if (child is not FrameworkElement fe)
				continue;

			if (noQuery)
			{
				fe.Opacity = 1.0;
				continue;
			}

			string text = CollectText(fe).ToLowerInvariant();
			fe.Opacity = text.Contains(q) ? 1.0 : DimmedOpacity;
		}
	}

	private static string CollectText(DependencyObject root)
	{
		var sb = new StringBuilder();
		var stack = new Stack<DependencyObject>();
		stack.Push(root);
		while (stack.Count > 0)
		{
			var node = stack.Pop();

			switch (node)
			{
				case TextBlock tb:
					sb.Append(tb.Text).Append(' ');
					break;
				case TextBox tx:
					sb.Append(tx.Header?.ToString()).Append(' ').Append(tx.PlaceholderText).Append(' ');
					break;
				case NumberBox nb:
					sb.Append(nb.Header?.ToString()).Append(' ').Append(nb.PlaceholderText).Append(' ');
					break;
				case ToggleSwitch ts:
					sb.Append(ts.Header?.ToString()).Append(' ');
					break;
				case ComboBox cb:
					sb.Append(cb.Header?.ToString()).Append(' ').Append(cb.PlaceholderText).Append(' ');
					break;
				case Button btn when btn.Content is string s:
					sb.Append(s).Append(' ');
					break;
				case Mutation.Ui.Views.SettingsUi.Controls.HotkeyEditor he:
					sb.Append(he.Header).Append(' ');
					break;
				case Mutation.Ui.Views.SettingsUi.Controls.SecretBox sec:
					sb.Append(sec.Header).Append(' ').Append(sec.PlaceholderText).Append(' ');
					break;
			}

			int count = VisualTreeHelper.GetChildrenCount(node);
			for (int i = 0; i < count; i++)
				stack.Push(VisualTreeHelper.GetChild(node, i));
		}
		return sb.ToString();
	}
}
