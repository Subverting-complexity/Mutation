using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.UI.Xaml;
using Mutation.Ui.Views;

namespace Mutation.Ui;

public sealed partial class MainWindow
{
	// First-run onboarding: show a friendly welcome, then open Settings on the API keys
	// page so the user can add their API keys. Replaces the old behaviour of opening
	// Mutation.json in Notepad. Called from App.OnLaunched when no LLM key is configured.
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
		// The welcome says the Settings window opens "so you can add your keys", so it
		// opens where the keys are rather than on whichever page happens to be first.
		await ShowSettingsDialogAsync("apikeys");
	}

	// Friendly, screen-reader-accessible welcome shown on first run.
	private Task ShowFirstRunWelcomeAsync()
	{
		const string title = "Welcome to Mutation";
		const string message =
			"Mutation needs at least one API key before you can use it.\n\n" +
			"• OpenAI API key — required for dictation (speech-to-text) and LLM processing.\n" +
			"• Anthropic API key — optional, for Claude LLM models.\n" +
			"• Azure Computer Vision key & endpoint — optional, for OCR.\n\n" +
			"All hotkeys are preset and fully editable. The Settings window will now open so you can add your keys. " +
			"You can reopen it anytime with Ctrl+Comma.";

		return ShowNoticeAsync(title, message, "Continue", NoticeSeverity.Information);
	}

	// Opens the Settings dialog. Reused by the Settings menu item, the Ctrl+,
	// shortcut, and first-run onboarding in App.OnLaunched. An optional category key
	// (e.g. "apikeys") opens the dialog directly on that tab.
	internal async Task ShowSettingsDialogAsync(string? initialCategoryKey = null)
	{
		if (Content is not FrameworkElement rootElement)
		{
			return;
		}

		var settingsDialog = new SettingsDialog(
			_settings,
			_settingsManager,
			_settingsManager.SettingsFilePath,
			ApplyLiveSettings,
			initialCategoryKey,
			// So a Hotkey Router row on the Hotkeys page can say whether the app is actually
			// listening for it. Null while there is no hotkey manager yet — before startup has
			// finished wiring one up — and the rows then say nothing rather than guess
			// (issue #343).
			liveRouterRoutes: () => _hotkeyManager?.LiveRouterRoutes())
		{
			XamlRoot = rootElement.XamlRoot,
			RequestedTheme = rootElement.ActualTheme
		};

		await ShowDialogAsync(settingsDialog);
	}

	// Shown at startup when one or more configured speech-to-text services have no
	// API key. Mirrors the OpenAI/Anthropic onboarding flow: a friendly,
	// screen-reader-accessible warning, then the Settings dialog opened on the API
	// keys tab so the user can add the missing key — instead of the old behaviour of
	// crashing with a "see the log" error dialog that closed the app on OK.
	internal async Task ShowMissingSpeechServiceKeysWarningAsync(IReadOnlyList<string> serviceNames)
	{
		const string title = "Speech-to-Text API Key Missing";
		string list = string.Join("\n", serviceNames.Select(n => $"• {n}"));
		string message =
			"One or more speech-to-text services are missing their API key and were disabled:\n\n" +
			list +
			"\n\nThe Settings window will now open on the API keys tab so you can add the missing key. " +
			"Restart Mutation after saving for the service to become available.";

		await ShowNoticeAsync(title, message, "Continue", NoticeSeverity.Warning);

		// Yield a dispatcher turn so the warning dialog fully closes before the
		// Settings dialog opens (WinUI allows only one ContentDialog at a time).
		await YieldToDispatcherAsync();
		await ShowSettingsDialogAsync("apikeys");
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
