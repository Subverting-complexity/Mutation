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
	public static void Show(TextBlock target, string? message)
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
		target.DispatcherQueue?.TryEnqueue(DispatcherQueuePriority.Low, () => Announce(target));
	}

	private static void Announce(TextBlock target)
	{
		if (target.Visibility != Visibility.Visible || target.Text.Length == 0)
			return;

		AutomationPeer? peer = FrameworkElementAutomationPeer.FromElement(target)
			?? FrameworkElementAutomationPeer.CreatePeerForElement(target);
		peer?.RaiseAutomationEvent(AutomationEvents.LiveRegionChanged);
	}
}
