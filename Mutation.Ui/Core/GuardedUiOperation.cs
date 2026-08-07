using System;
using System.Threading.Tasks;

namespace Mutation.Ui.Core;

/// <summary>
/// Runs UI work that has taken something away from the user — a text box put into
/// read-only, suppressed auto-actions — and must give it back however the work ends.
/// </summary>
/// <remarks>
/// The callers are <c>async void</c> handlers, so an escaping exception is swallowed by
/// the global handler and the window is simply left crippled with no explanation: the
/// transcript box stops accepting typing for the rest of the session (issue #234).
/// Restoration therefore runs in a <c>finally</c>, and the failure is reported where
/// the user will actually meet it rather than only in the log.
/// </remarks>
public static class GuardedUiOperation
{
	/// <param name="work">The operation. Its failure is caught, not propagated.</param>
	/// <param name="onFailure">
	/// Tells the user the operation failed — status text and a beep. Called on the same
	/// thread as <paramref name="work"/> finished on, which for UI callers is the UI
	/// thread.
	/// </param>
	/// <param name="restore">
	/// Gives the window back. Runs exactly once, whether or not anything threw. Put the
	/// state the user needs back first: a failure part-way through is reported through
	/// <paramref name="onReportFailed"/> and swallowed, so the statements before it have
	/// still taken effect.
	/// </param>
	/// <param name="onReportFailed">
	/// Receives a failure raised by <paramref name="onFailure"/> or by
	/// <paramref name="restore"/> itself — the beep player or an automation peer failing
	/// while reporting or clearing up. It is logged rather than rethrown. Nothing at all
	/// escapes this method: the callers are <c>async void</c>, so an escaping exception
	/// would go straight back to the global handler this exists to keep them out of.
	/// </param>
	public static async Task RunAsync(
		Func<Task> work,
		Action<Exception> onFailure,
		Action restore,
		Action<Exception>? onReportFailed = null)
	{
		ArgumentNullException.ThrowIfNull(work);
		ArgumentNullException.ThrowIfNull(onFailure);
		ArgumentNullException.ThrowIfNull(restore);

		try
		{
			await work();
		}
		catch (Exception ex)
		{
			try
			{
				onFailure(ex);
			}
			catch (Exception reportFailure)
			{
				onReportFailed?.Invoke(reportFailure);
			}
		}
		finally
		{
			try
			{
				restore();
			}
			catch (Exception restoreFailure)
			{
				onReportFailed?.Invoke(restoreFailure);
			}
		}
	}
}
