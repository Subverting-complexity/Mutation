using System;
using System.Diagnostics;
using System.Threading.Tasks;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Mutation.Ui.Core;

namespace Mutation.Ui;

/// <summary>
/// How serious a notice is. Kept separate from the WinForms
/// <see cref="System.Windows.Forms.MessageBoxIcon"/> so that type stays behind the one
/// fallback that needs it instead of appearing at every call site.
/// </summary>
public enum NoticeSeverity
{
	Information,
	Warning,
}

// Raising a message the user has to acknowledge, and the wait that makes an accessible
// one possible at startup.
public sealed partial class MainWindow
{
	// How long startup will wait for the window's content to become a usable dialog
	// host before showing its notices the degraded way. Generous compared with the
	// couple of dispatcher turns it normally takes, so a slow machine still gets the
	// accessible dialog, but bounded so a window that never loads cannot wedge startup.
	private static readonly TimeSpan ContentReadyTimeout = TimeSpan.FromSeconds(5);
	private static readonly TimeSpan ContentReadyPollInterval = TimeSpan.FromMilliseconds(50);

	// Awaited by App.OnLaunched after Activate() and before any startup dialog.
	// Activate() returns before the content is loaded, so for the first dispatcher
	// turns after it there is no XamlRoot — and a ContentDialog without one cannot
	// open. Every startup notice used to be shown in that window, which is why the
	// first-run welcome arrived as a bare Win32 message box with no automation name
	// and the hotkey-failure list was dropped entirely.
	//
	// Returns whether the content became ready; false means the caller's notices will
	// take their fallback surface.
	internal Task<bool> WaitForContentReadyAsync()
	{
		// A real clock, not a count of polls: the reason content would fail to load is a
		// UI thread too busy to pump, which is exactly when each "50 ms" poll costs far
		// more than 50 ms. Counting intervals would stretch a 5 second bound to whatever
		// 100 dispatcher round-trips happen to take.
		var clock = Stopwatch.StartNew();
		return ContentReadyGate.WaitAsync(
			HasLiveXamlRoot,
			Task.Delay,
			() => clock.Elapsed,
			ContentReadyPollInterval,
			ContentReadyTimeout);
	}

	// ContentDialog.ShowAsync needs a loaded visual tree, not merely an assigned
	// XamlRoot, so both are required before the wait is satisfied.
	private bool HasLiveXamlRoot() => Content is FrameworkElement { IsLoaded: true, XamlRoot: not null };

	/// <summary>
	/// Shows a message the user has to acknowledge, as an in-app dialog whenever the
	/// window can host one and as a system message box when it cannot — including when
	/// the dialog was attempted and failed.
	///
	/// The fallback exists so a notice is never simply dropped: a message that does not
	/// appear leaves the user with an unexplained beep and no way to find out what
	/// happened. Both surfaces carry the same text, and the dialog carries it as an
	/// automation name and help text so a screen reader reads it on open.
	/// </summary>
	internal async Task ShowNoticeAsync(
		string title,
		string message,
		string closeButtonText,
		NoticeSeverity severity)
	{
		if (Content is FrameworkElement rootElement && rootElement.XamlRoot is not null)
		{
			var dialog = new ContentDialog
			{
				Title = title,
				Content = new TextBlock { Text = message, TextWrapping = TextWrapping.Wrap },
				CloseButtonText = closeButtonText,
				XamlRoot = rootElement.XamlRoot,
				RequestedTheme = rootElement.ActualTheme
			};
			AutomationProperties.SetName(dialog, title);
			AutomationProperties.SetHelpText(dialog, message);

			// Only fall through when the dialog never made it on screen. ShowDialogAsync
			// turns a failed show into a status-bar line, which is not an acknowledgement
			// and is exactly the "it beeped and said nothing" symptom this helper exists
			// to prevent.
			if (await TryShowDialogAsync(dialog))
				return;
		}

		ShowNoticeMessageBox(title, message, severity);
	}

	// Owned by the main window so it cannot open behind it: an unowned modal box that
	// is hidden but still taking input is worse than no message at all, and worst of all
	// for a screen-reader user who has no visual cue where the focus went.
	private void ShowNoticeMessageBox(string title, string message, NoticeSeverity severity)
	{
		var icon = severity == NoticeSeverity.Warning
			? System.Windows.Forms.MessageBoxIcon.Warning
			: System.Windows.Forms.MessageBoxIcon.Information;

		try
		{
			var owner = new Win32WindowHandle(WinRT.Interop.WindowNative.GetWindowHandle(this));
			System.Windows.Forms.MessageBox.Show(
				owner, message, title, System.Windows.Forms.MessageBoxButtons.OK, icon);
		}
		catch (Exception ex)
		{
			// The handle is gone (the window is closing). An unowned box still delivers
			// the message, which is the point.
			Debug.WriteLine($"Owned notice message box failed: {ex.Message}");
			System.Windows.Forms.MessageBox.Show(
				message, title, System.Windows.Forms.MessageBoxButtons.OK, icon);
		}
	}

	private sealed class Win32WindowHandle : System.Windows.Forms.IWin32Window
	{
		public Win32WindowHandle(IntPtr handle) => Handle = handle;

		public IntPtr Handle { get; }
	}
}
