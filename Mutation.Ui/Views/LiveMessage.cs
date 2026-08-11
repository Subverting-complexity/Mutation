using System;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Controls;

namespace Mutation.Ui.Views;

/// <summary>
/// Puts a message into a <see cref="TextBlock"/> and makes sure it is heard.
/// <para>
/// Three things are needed and the message is silent without any one of them: the block
/// carries <c>AutomationProperties.LiveSetting</c>, its <c>Text</c> changes rather than only
/// its visibility, and the event is raised by hand. WinUI raises nothing of its own when a
/// TextBlock's text changes — the live setting says the text is worth announcing, it does not
/// announce it (issue #243). Two settings warnings had the first without the other two and so
/// appeared in silence.
/// </para>
/// </summary>
internal static class LiveMessage
{
	/// <summary>
	/// Shows <paramref name="message"/>, or hides the block when it is blank. Announced only
	/// when the text actually changes, so re-validating on every keystroke does not repeat the
	/// same warning, and only when there is something to say — an emptied live region has
	/// nothing to read out.
	/// </summary>
	/// <param name="shouldAnnounce">
	/// Asked at the moment of raising rather than now, and null means always. It is how a page
	/// shows a message without reading it out: the Hotkeys page builds rows from settings that
	/// already carry "Enter a hotkey.", and announcing each of them would greet the user with
	/// one interruption per stored row about settings nobody had touched (issue #350).
	/// <para>
	/// Asked late for a reason. The bindings in a list row all apply in one pass, and whichever
	/// order they happen to run in, the raise is enqueued behind the whole pass — so a question
	/// asked then gets the row's settled answer rather than a half-applied one.
	/// </para>
	/// <para>
	/// Note what a silent show still does: it writes the text. So the same message becoming
	/// allowed to speak later says nothing, because by then it is no longer a change. That is
	/// the wanted behaviour — a stored error the user has since started editing around should
	/// not suddenly be shouted at them — and it means nothing has to remember what it swallowed.
	/// </para>
	/// </summary>
	public static void Show(TextBlock target, string? message, Func<bool>? shouldAnnounce = null)
	{
		string text = message ?? string.Empty;
		if (target.Text == text)
			return;

		bool hasMessage = text.Length > 0;
		target.Visibility = hasMessage ? Visibility.Visible : Visibility.Collapsed;
		target.Text = text;

		if (!hasMessage)
			return;

		// Raised after this layout pass rather than inside it. These blocks are collapsed until
		// the moment they have something to say, and a collapsed element is not in the
		// automation tree yet — raising against it before layout has run would announce into
		// nothing.
		target.DispatcherQueue?.TryEnqueue(
			DispatcherQueuePriority.Low, () => Announce(target, shouldAnnounce));
	}

	private static void Announce(TextBlock target, Func<bool>? shouldAnnounce)
	{
		if (target.Visibility != Visibility.Visible || target.Text.Length == 0)
			return;

		if (shouldAnnounce is not null && !shouldAnnounce())
			return;

		AutomationPeer? peer = FrameworkElementAutomationPeer.FromElement(target)
			?? FrameworkElementAutomationPeer.CreatePeerForElement(target);
		peer?.RaiseAutomationEvent(AutomationEvents.LiveRegionChanged);
	}
}
