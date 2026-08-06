using System;
using System.Threading;
using System.Threading.Tasks;

namespace Mutation.Ui.Core;

/// <summary>
/// Coalesces a burst of rapid triggers into a single deferred action. Each
/// <see cref="Trigger"/> cancels any run that has not yet fired and schedules a
/// new one after the configured delay, so the action runs only once the triggers
/// stop for at least that delay. Used to keep a continuous UI stream — a slider
/// drag or a held arrow key, which raise <c>ValueChanged</c> dozens of times per
/// second — from running an expensive action (a full settings serialize plus an
/// atomic file replace) on every tick.
/// </summary>
/// <remarks>
/// The action runs on whatever synchronization context <see cref="Trigger"/> was
/// called from (the UI thread, in this app), so the caller's action can safely
/// touch UI-affine state exactly as an inline save would. The delay mechanism is
/// injectable so the coalescing and cancellation behaviour can be unit-tested
/// without real time passing.
/// </remarks>
public sealed class Debouncer : IDisposable
{
	private readonly TimeSpan _delay;
	private readonly Action _action;
	private readonly Action<Exception>? _onError;
	private readonly Func<TimeSpan, CancellationToken, Task> _delayAsync;
	private readonly object _gate = new();
	private CancellationTokenSource? _cts;
	private bool _disposed;

	/// <summary>
	/// Creates a debouncer that runs <paramref name="action"/> once the triggers
	/// have been quiet for <paramref name="delay"/>.
	/// </summary>
	/// <param name="delay">Quiet period that must elapse after the last trigger
	/// before the action runs. Must not be negative.</param>
	/// <param name="action">The work to run once per settled burst.</param>
	/// <param name="delayAsync">The delay mechanism; defaults to
	/// <see cref="Task.Delay(TimeSpan, CancellationToken)"/>. Injectable so tests
	/// can drive the timing deterministically.</param>
	/// <param name="onError">Called when the action throws. Runs on the same
	/// context as the action. Without it a failure is invisible: the run is
	/// fire-and-forget, so the exception only reaches
	/// <see cref="TaskScheduler.UnobservedTaskException"/> and the user is never
	/// told the work did not happen (issue #233).</param>
	public Debouncer(
		TimeSpan delay,
		Action action,
		Func<TimeSpan, CancellationToken, Task>? delayAsync = null,
		Action<Exception>? onError = null)
	{
		if (delay < TimeSpan.Zero)
			throw new ArgumentOutOfRangeException(nameof(delay), "Delay must not be negative.");

		_delay = delay;
		_action = action ?? throw new ArgumentNullException(nameof(action));
		_delayAsync = delayAsync ?? Task.Delay;
		_onError = onError;
	}

	/// <summary>
	/// Schedules the action to run after the delay, cancelling any earlier run
	/// that has not yet fired. Rapid successive calls therefore collapse into a
	/// single run once the calls stop. A no-op after <see cref="Dispose"/>.
	/// </summary>
	public void Trigger()
	{
		CancellationToken token;
		lock (_gate)
		{
			if (_disposed)
				return;

			_cts?.Cancel();
			_cts?.Dispose();
			_cts = new CancellationTokenSource();
			token = _cts.Token;
		}

		_ = RunAsync(token);
	}

	private async Task RunAsync(CancellationToken token)
	{
		try
		{
			await _delayAsync(_delay, token).ConfigureAwait(true);
			if (!token.IsCancellationRequested)
				_action();
		}
		catch (OperationCanceledException)
		{
			// A newer trigger (or Dispose) superseded this run — expected, drop it.
		}
		catch (Exception ex)
		{
			// Nobody awaits this task, so an unhandled exception here would vanish.
			// Hand it to the caller instead; for the settings save that is the only
			// chance the user has of hearing that the write failed.
			ReportError(ex);
		}
	}

	private void ReportError(Exception exception)
	{
		if (_onError is null)
			return;

		try
		{
			_onError(exception);
		}
		catch (Exception callbackFailure)
		{
			// The reporter itself failing must not take the process with it — this
			// still runs on an unobserved task.
			System.Diagnostics.Debug.WriteLine(
				$"Debouncer error callback failed: {callbackFailure.Message}");
		}
	}

	/// <summary>
	/// Cancels any pending run and stops accepting further triggers. Does not
	/// flush a pending action; a caller that must persist the latest value on
	/// shutdown does so on its own path (e.g. the window's close handler saves
	/// the already-mutated settings object).
	/// </summary>
	public void Dispose()
	{
		lock (_gate)
		{
			if (_disposed)
				return;

			_disposed = true;
			_cts?.Cancel();
			_cts?.Dispose();
			_cts = null;
		}
	}
}
