using System.Threading.Tasks;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Mutation.Ui.Core;

namespace Mutation.Ui;

// Where keyboard focus starts when Mutation opens (issue #448).
public sealed partial class MainWindow
{
	private bool _startupFocusApplied;

	/// <summary>
	/// Puts keyboard focus on the Play button, or on Record when there is nothing to play.
	/// App.OnLaunched calls this once, after its last startup dialog has closed.
	///
	/// It runs through the dialog queue rather than straight away, so a dialog that is
	/// still on screen or waiting its turn (one raised outside App.OnLaunched's own awaited
	/// sequence, say) keeps focus and this applies only once that dialog has closed.
	/// Focusing a control behind an open dialog would pull focus out of the dialog the
	/// user is reading.
	///
	/// It does this exactly once. Nothing here listens to window activation, so when the
	/// user switches away and back, WinUI gives focus back to the control they last used.
	/// </summary>
	internal Task ApplyStartupFocusAsync()
	{
		if (_startupFocusApplied)
			return Task.CompletedTask;

		_startupFocusApplied = true;
		return _dialogQueue.EnqueueAsync(async () =>
		{
			// A ContentDialog that has just closed gives focus back to the control that had
			// it before it opened, and it can do that after its ShowAsync has completed.
			// Without this one dispatcher turn, that hand-back could land after ours and
			// put focus back on whatever WinUI chose first.
			await YieldToDispatcherAsync();
			FocusStartupControl();
			return ContentDialogResult.None;
		});
	}

	private void FocusStartupControl()
	{
		StartupFocusTarget target = StartupFocusPlanner.Choose(
			BtnPlayLatestRecording.IsEnabled,
			BtnSpeechToText.IsEnabled);

		Control? control = target switch
		{
			StartupFocusTarget.Play => BtnPlayLatestRecording,
			StartupFocusTarget.Record => BtnSpeechToText,
			_ => null,
		};

		// FocusState.Keyboard rather than Programmatic: no key has been pressed yet at
		// startup, and with Programmatic WinUI then hides the focus rectangle. A user
		// working with a magnifier needs to see where focus is, not only hear it.
		// Focus returning false (the window content never loaded) leaves focus where
		// WinUI put it, which is what happened before this change.
		control?.Focus(FocusState.Keyboard);
	}
}
