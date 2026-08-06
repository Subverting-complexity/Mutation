using System;
using System.Threading;

namespace Mutation.Ui.Services;

/// <summary>
/// One batch OCR run's cancellation lifetime, handed out by
/// <see cref="OcrDocumentsRunController.Begin"/>. Ending a run takes its handle, so one
/// invocation of the OCR button can never tear down a run another one started.
/// </summary>
public sealed class OcrDocumentsRun
{
	private readonly CancellationTokenSource _source;
	private readonly CancellationToken _token;

	internal OcrDocumentsRun(CancellationToken shutdownToken)
	{
		_source = CancellationTokenSource.CreateLinkedTokenSource(shutdownToken);

		// Copied out now, because CancellationTokenSource.Token throws once the source is
		// disposed. The handle outlives the run — the cancellation message reads it after
		// the finally has already released it — and asking it a question must never throw.
		_token = _source.Token;
	}

	/// <summary>The token every OCR call in this run must observe.</summary>
	public CancellationToken Token => _token;

	internal bool Cancel()
	{
		if (_token.IsCancellationRequested)
			return false;

		try
		{
			_source.Cancel();
			return true;
		}
		catch (ObjectDisposedException)
		{
			// The run ended between the check and the cancel; nothing left to stop.
			return false;
		}
	}

	internal void Release() => _source.Dispose();
}

/// <summary>
/// Owns the cancellation lifetime of the batch OCR run.
///
/// A run's token is linked to application shutdown, so closing the window stops an
/// in-flight batch instead of leaving it burning through the user's Azure quota, and the
/// run can also be cancelled on its own from the Cancel button without taking anything
/// else down with it (issue #227).
///
/// Disposing a linked token source permanently severs it from its parent — after that a
/// shutdown cancel never reaches the run. That makes "who is allowed to end this run" a
/// correctness question, not bookkeeping, which is why <see cref="End"/> takes the handle
/// <see cref="Begin"/> returned and ignores anything else.
///
/// UI-thread affinity is the contract, not an accident: <see cref="IsRunning"/> and
/// <see cref="Begin"/> are meant to be called back to back as one atomic claim, and that
/// only holds while every caller is on the same thread. Do not drive this from a
/// background task without adding synchronisation.
/// </summary>
public sealed class OcrDocumentsRunController : IDisposable
{
	private readonly CancellationToken _shutdownToken;
	private OcrDocumentsRun? _run;
	private bool _disposed;

	public OcrDocumentsRunController(CancellationToken shutdownToken)
	{
		_shutdownToken = shutdownToken;
	}

	/// <summary>True while a run is in flight and can still be cancelled.</summary>
	public bool IsRunning => _run is not null;

	/// <summary>
	/// Starts a run. Callers check <see cref="IsRunning"/> first; this throws rather than
	/// silently replacing a run, because replacing one would strand it uncancellable.
	/// </summary>
	/// <exception cref="InvalidOperationException">A run is already in flight.</exception>
	public OcrDocumentsRun Begin()
	{
		ObjectDisposedException.ThrowIf(_disposed, this);

		if (_run is not null)
			throw new InvalidOperationException("An OCR documents run is already in progress.");

		_run = new OcrDocumentsRun(_shutdownToken);
		return _run;
	}

	/// <summary>
	/// Cancels the run in flight. Returns false when there is nothing left to cancel —
	/// no run, or one already cancelled — so a caller neither announces a stop that never
	/// happened nor announces the same one twice.
	/// </summary>
	public bool Cancel() => _run?.Cancel() ?? false;

	/// <summary>
	/// Ends <paramref name="run"/> and releases it. Does nothing if that run is not the
	/// one in flight, so a handler unwinding after a failed <see cref="Begin"/> cannot
	/// release the run it lost the race to.
	/// </summary>
	public void End(OcrDocumentsRun? run)
	{
		if (run is null || !ReferenceEquals(_run, run))
			return;

		_run = null;
		run.Release();
	}

	/// <summary>
	/// Cancels the run in flight before releasing it. Releasing first would sever the run
	/// from shutdown and leave it processing documents nothing can stop.
	/// </summary>
	public void Dispose()
	{
		if (_disposed)
			return;

		_disposed = true;

		OcrDocumentsRun? run = _run;
		if (run is null)
			return;

		run.Cancel();
		_run = null;
		run.Release();
	}
}
