using System;
using System.Threading.Tasks;

namespace Mutation.Ui.Core;

/// <summary>
/// Waits for a window's content to become usable as a dialog host.
///
/// <see cref="Microsoft.UI.Xaml.Window.Activate"/> returns before the content has been
/// loaded, so for the first few dispatcher turns after it the root element still has no
/// <c>XamlRoot</c>. A <c>ContentDialog</c> opened in that window either throws or has to
/// fall back to a bare Win32 message box, which carries no automation name and no help
/// text for a screen reader. Anything that shows a dialog at startup therefore waits
/// here first.
///
/// The wait polls instead of hooking <c>Loaded</c>: by the time a caller asks, that event
/// may already have been raised and gone, and a missed event would stall startup for the
/// whole timeout for no reason. Awaiting the delay on the UI thread is also what yields
/// the dispatcher turns the content needs in order to load.
/// </summary>
public static class ContentReadyGate
{
	/// <summary>
	/// Completes once <paramref name="isReady"/> returns true, or once
	/// <paramref name="elapsed"/> reports that <paramref name="timeout"/> has passed —
	/// whichever comes first. Returns whether the content became ready. It never throws
	/// and never waits indefinitely: a window that somehow never loads must still let
	/// startup continue, degraded rather than hung.
	/// </summary>
	/// <param name="isReady">Whether the content can host a dialog yet.</param>
	/// <param name="delay">How to wait one poll interval. Awaiting it on the UI thread is
	/// what returns control to the message pump so the content can load.</param>
	/// <param name="elapsed">Real time since the wait started. The timeout is measured
	/// against this rather than against a count of poll intervals, because the case this
	/// bound exists for — a UI thread too busy to load its content — is exactly the case
	/// where each poll takes far longer than the interval asked for.</param>
	public static async Task<bool> WaitAsync(
		Func<bool> isReady,
		Func<TimeSpan, Task> delay,
		Func<TimeSpan> elapsed,
		TimeSpan pollInterval,
		TimeSpan timeout)
	{
		if (isReady is null) throw new ArgumentNullException(nameof(isReady));
		if (delay is null) throw new ArgumentNullException(nameof(delay));
		if (elapsed is null) throw new ArgumentNullException(nameof(elapsed));
		if (pollInterval <= TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(pollInterval));

		while (true)
		{
			if (isReady())
				return true;

			if (elapsed() >= timeout)
				return false;

			await delay(pollInterval).ConfigureAwait(true);
		}
	}
}
