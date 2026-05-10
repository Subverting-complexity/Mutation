using System;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Mutation.Ui.Views.SettingsUi.Controls;

// Small factory for the "↶ reset to default" button used next to settings fields.
// Kept as a code-only helper to avoid a XAML round-trip per page.
internal static class SettingsResetButton
{
	private const string RefreshGlyph = ""; // Segoe Fluent Icons "Refresh"

	public static Button Create(string tooltip, Action onReset)
	{
		var btn = new Button
		{
			Content = new FontIcon
			{
				Glyph = RefreshGlyph,
				FontFamily = new Microsoft.UI.Xaml.Media.FontFamily("Segoe Fluent Icons"),
				FontSize = 12,
			},
			Padding = new Thickness(6, 4, 6, 4),
			MinWidth = 32,
		};
		ToolTipService.SetToolTip(btn, tooltip);
		Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(btn, "Reset to default");
		btn.Click += (_, _) =>
		{
			try { onReset(); }
			catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"Reset failed: {ex.Message}"); }
		};
		return btn;
	}
}
