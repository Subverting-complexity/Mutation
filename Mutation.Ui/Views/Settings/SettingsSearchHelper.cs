using System.Collections.Generic;
using System.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace Mutation.Ui.Views.SettingsUi;

// Walks the top-level children of a settings page's root Panel, gathers the
// visible text underneath each child, and collapses children whose text doesn't
// contain the query. Collapsing (rather than dimming) removes non-matching
// sections from the tab order and the screen-reader tree, so search results are
// perceivable without sight. Empty query restores all sections.
// Returns the number of sections that match (all sections for an empty query).
internal static class SettingsSearchHelper
{
	public static int ApplyFilter(Panel root, string query)
	{
		string q = (query ?? string.Empty).Trim().ToLowerInvariant();
		bool noQuery = q.Length == 0;
		int matches = 0;

		foreach (UIElement child in root.Children)
		{
			if (child is not FrameworkElement fe)
				continue;

			if (noQuery)
			{
				fe.Visibility = Visibility.Visible;
				matches++;
				continue;
			}

			bool isMatch = CollectText(fe).ToLowerInvariant().Contains(q);
			fe.Visibility = isMatch ? Visibility.Visible : Visibility.Collapsed;
			if (isMatch)
				matches++;
		}

		return matches;
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
