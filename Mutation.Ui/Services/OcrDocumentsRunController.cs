using System;
using System.Threading;

namespace Mutation.Ui.Services;

/// <summary>
/// Owns the cancellation lifetime of one batch OCR run.
///
/// A run's token is linked to application shutdown, so closing the window stops an
/// in-flight batch instead of leaving it burning through the user's Azure quota, and the
/// run can also be cancelled on its own from the Cancel button without taking anything
/// else down with it (issue #227).
/// </summary>
public sealed class OcrDocumentsRunController : IDisposable
{
	private readonly CancellationToken _shutdownToken;
	private CancellationTokenSource? _runCts;
	private bool _disposed;

	public OcrDocumentsRunController(CancellationToken shutdownToken)
	{
		_shutdownToken = shutdownToken;
	}

	/// <summary>True while a run is in flight and can still be cancelled.</summary>
	public bool IsRunning => _runCts is not null;

	/// <summary>
	/// Starts a run and returns the token every OCR call in it must observe.
	/// </summary>
	/// <exception cref="InvalidOperationException">A run is already in flight.</exception>
	public CancellationToken Begin()
	{
		ObjectDisposedException.ThrowIf(_disposed, this);

		if (_runCts is not null)
			throw new InvalidOperationException("An OCR documents run is already in progress.");

		_runCts = CancellationTokenSource.CreateLinkedTokenSource(_shutdownToken);
		return _runCts.Token;
	}

	/// <summary>
	/// Cancels the run in flight. Returns false when there is nothing to cancel, so a
	/// caller does not announce a cancellation that never happened.
	/// </summary>
	public bool Cancel()
	{
		CancellationTokenSource? cts = _runCts;
		if (cts is null)
			return false;

		try
		{
			cts.Cancel();
			return true;
		}
		catch (ObjectDisposedException)
		{
			// The run finished between the check and the cancel; nothing left to stop.
			return false;
		}
	}

	/// <summary>
	/// Ends the run and releases its token source. Safe to call when no run is in flight,
	/// so it can live in a finally block alongside the rest of the run's cleanup.
	/// </summary>
	public void End()
	{
		CancellationTokenSource? cts = Interlocked.Exchange(ref _runCts, null);
		cts?.Dispose();
	}

	public void Dispose()
	{
		if (_disposed)
			return;

		_disposed = true;
		End();
	}
}
