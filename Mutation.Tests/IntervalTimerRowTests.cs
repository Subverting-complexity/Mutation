using System.Collections.Generic;
using CognitiveSupport;
using Mutation.Ui;
using Xunit;

namespace Mutation.Tests;

// The main window's timer row must never change what a screen reader reads while the user is
// on the toggle (issue #456).
public class IntervalTimerRowTests
{
	private static IntervalTimerRow NewRow(List<bool>? toggles = null) =>
		new(new IntervalTimerSettings { Name = "Stretch", Enabled = true }, (_, isOn) => toggles?.Add(isOn));

	[Fact]
	public void HelpText_FollowsTheCountdown_WhileNotFocused()
	{
		var row = NewRow();

		row.Update("next in 5 minutes", null);
		row.Update("next in 4 minutes", null);

		Assert.Equal("next in 4 minutes", row.FocusHelpText);
	}

	[Fact]
	public void HelpText_IsFrozenWhileFocused_AndCatchesUpOnLeaving()
	{
		var row = NewRow();
		row.Update("next in 5 minutes", null);
		var raised = new List<string?>();
		row.PropertyChanged += (_, e) => raised.Add(e.PropertyName);

		row.NoteFocused();
		row.Update("next in 4 minutes", null);

		Assert.Equal("next in 5 minutes", row.FocusHelpText);
		Assert.Equal("next in 4 minutes", row.CountdownText);
		Assert.DoesNotContain(nameof(IntervalTimerRow.FocusHelpText), raised);

		row.NoteUnfocused();

		Assert.Equal("next in 4 minutes", row.FocusHelpText);
	}

	[Fact]
	public void HelpText_IncludesTheWarning()
	{
		var row = NewRow();

		row.Update("next in 5 minutes", IntervalTimerText.SoundFailedWarning);

		Assert.Equal($"next in 5 minutes. {IntervalTimerText.SoundFailedWarning}", row.FocusHelpText);
		Assert.Equal(Microsoft.UI.Xaml.Visibility.Visible, row.WarningVisibility);
	}

	[Fact]
	public void Toggling_ReportsTheNewState_OnlyWhenItChanges()
	{
		var toggles = new List<bool>();
		var row = NewRow(toggles);

		row.IsOn = true;  // already on
		row.IsOn = false;

		Assert.Equal(new[] { false }, toggles);
	}

	[Fact]
	public void AccessibleName_AddsTimer()
	{
		Assert.Equal("Stretch timer", NewRow().AccessibleName);
	}
}
