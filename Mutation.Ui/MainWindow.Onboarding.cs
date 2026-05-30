using System.Threading.Tasks;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Mutation.Ui.Views;

namespace Mutation.Ui;

public sealed partial class MainWindow
{
	// First-run onboarding: show a friendly welcome, then open Settings so the
	// user can add their API keys. Replaces the old behaviour of opening
	// Mutation.json in Notepad. Called from App.OnLaunched when no LLM key is
	// configured.
	//
	// The two dialogs must NOT be opened back-to-back in the same continuation:
	// WinUI throws "Only a single ContentDialog can be open at any time" because
	// the welcome dialog is still closing when the Settings dialog opens, which
	// previously left a phantom modal overlay and an invisible window. Yielding a
	// dispatcher turn between them lets the first dialog fully close first.
	internal async Task ShowFirstRunOnboardingAsync()
	{
		await ShowFirstRunWelcomeAsync();
		await YieldToDispatcherAsync();
		await ShowSettingsDialogAsync();
	}

	// Friendly, screen-reader-accessible welcome shown on first run.
	private async Task ShowFirstRunWelcomeAsync()
	{
		const string title = "Welcome to Mutation";
		const string message =
			"Mutation needs at least one API key before you can use it.\n\n" +
			"• OpenAI API key — required for dictation (speech-to-text) and LLM processing.\n" +
			"• Anthropic API key — optional, for Claude LLM models.\n" +
			"• Azure Computer Vision key & endpoint — optional, for OCR.\n\n" +
			"All hotkeys are preset and fully editable. The Settings window will now open so you can add your keys. " +
			"You can reopen it anytime with Ctrl+Comma.";

		if (Content is not FrameworkElement rootElement || rootElement.XamlRoot is null)
		{
			System.Windows.Forms.MessageBox.Show(
				message,
				title,
				System.Windows.Forms.MessageBoxButtons.OK,
				System.Windows.Forms.MessageBoxIcon.Information);
			return;
		}

		var dialog = new ContentDialog
		{
			Title = title,
			Content = new TextBlock { Text = message, TextWrapping = Microsoft.UI.Xaml.TextWrapping.Wrap },
			CloseButtonText = "Continue",
			XamlRoot = rootElement.XamlRoot,
			RequestedTheme = rootElement.ActualTheme
		};
		AutomationProperties.SetName(dialog, title);
		AutomationProperties.SetHelpText(dialog, message);

		await ShowDialogAsync(dialog);
	}

	// Opens the Settings dialog. Reused by the Settings menu item, the Ctrl+,
	// shortcut, and first-run onboarding in App.OnLaunched.
	internal async Task ShowSettingsDialogAsync()
	{
		if (Content is not FrameworkElement rootElement)
		{
			return;
		}

		var settingsDialog = new SettingsDialog(
			_settings,
			_settingsManager,
			_settingsManager.SettingsFilePath,
			ApplyLiveSettings)
		{
			XamlRoot = rootElement.XamlRoot,
			RequestedTheme = rootElement.ActualTheme
		};

		await ShowDialogAsync(settingsDialog);
	}

	// Completes on a fresh dispatcher turn, breaking out of the current
	// (input-synchronous) continuation so a just-closed ContentDialog can finish
	// tearing down before the next one opens.
	private Task YieldToDispatcherAsync()
	{
		var tcs = new TaskCompletionSource();
		if (!DispatcherQueue.TryEnqueue(() => tcs.SetResult()))
			tcs.SetResult();
		return tcs.Task;
	}
}
