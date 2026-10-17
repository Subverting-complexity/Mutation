using System;
using System.Collections.Generic;
using System.Linq;
using CognitiveSupport;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Windows.System;

namespace Mutation.Ui.Views.SettingsUi.Controls;

/// <summary>
/// The content of the small window the reasoning levels button opens: one check box for
/// each level in the model's service list, then OK and Cancel. It only reports what the
/// user chose; the caller saves it and closes the window.
/// </summary>
/// <remarks>
/// It is shown in a flyout, not a <see cref="ContentDialog"/>, because the Settings
/// window is itself a ContentDialog and WinUI allows only one open at a time; a second
/// one throws.
/// </remarks>
public sealed partial class ReasoningLevelsPicker : UserControl
{
	private readonly List<(CheckBox Box, ReasoningLevel Level)> _boxes = new();

	public ReasoningLevelsPicker(string heading, IReadOnlyList<ReasoningLevelChoice> choices)
	{
		ArgumentNullException.ThrowIfNull(choices);
		InitializeComponent();

		HeadingText.Text = heading;
		AutomationProperties.SetName(this, heading);

		foreach (var choice in choices)
		{
			var box = new CheckBox
			{
				Content = choice.DisplayName,
				IsChecked = choice.IsTicked,
			};
			AutomationProperties.SetName(box, choice.DisplayName);
			LevelsPanel.Children.Add(box);
			_boxes.Add((box, choice.Level));
		}
	}

	/// <summary>Raised on OK, with the ticked levels in list order.</summary>
	public event EventHandler<IReadOnlyList<ReasoningLevel>>? Accepted;

	/// <summary>Raised on Cancel. Nothing is changed.</summary>
	public event EventHandler? Cancelled;

	/// <summary>Moves focus to the first check box, or to OK when there is none.</summary>
	public void FocusFirstLevel()
	{
		Control target = _boxes.Count > 0 ? _boxes[0].Box : BtnOk;
		target.Focus(FocusState.Programmatic);
	}

	private IReadOnlyList<ReasoningLevel> TickedLevels() =>
		_boxes.Where(b => b.Box.IsChecked == true).Select(b => b.Level).ToList();

	private void BtnOk_Click(object sender, RoutedEventArgs e) =>
		Accepted?.Invoke(this, TickedLevels());

	private void BtnCancel_Click(object sender, RoutedEventArgs e) =>
		Cancelled?.Invoke(this, EventArgs.Empty);

	// Enter on a check box means OK, as in any dialog. Buttons handle Enter themselves
	// and mark it handled, so it does not reach here from Cancel. Marking it handled
	// here also keeps it from reaching the Settings window, whose own Enter means Save.
	private void OnKeyDown(object sender, KeyRoutedEventArgs e)
	{
		if (e.Key != VirtualKey.Enter || e.OriginalSource is Button)
			return;

		e.Handled = true;
		Accepted?.Invoke(this, TickedLevels());
	}
}
