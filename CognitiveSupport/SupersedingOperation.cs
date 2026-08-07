namespace CognitiveSupport;

// The single "current operation" token for a service where anything new supersedes
// whatever was already running — a read replaces a read, a skip replaces a read, a stop
// ends both.
//
// The ordering inside Begin is the whole point: the outgoing token is cancelled *before*
// the replacement is published. Cancelling afterwards — worse, after the caller has let
// go of its lock — leaves a window in which a worker for the superseded operation can
// still see a live token, take the lock and go ahead, so the stale work runs and the new
// work merely queues behind it (issue #236). Callers therefore hold their own lock across
// Begin, and nothing between the two halves can observe a half-finished handover.
internal sealed class SupersedingOperation : IDisposable
{
	private readonly object _lock = new();
	private CancellationTokenSource? _current;
	private bool _disposed;

	// Cancel whatever is running and hand back the token for its replacement. After
	// Dispose this returns an already-cancelled token, so a caller racing disposal
	// starts no work rather than starting work nothing will ever stop.
	public CancellationToken Begin()
	{
		lock (_lock)
		{
			CancelCurrentLocked();
			if (_disposed) return new CancellationToken(canceled: true);
			_current = new CancellationTokenSource();
			return _current.Token;
		}
	}

	// Cancel whatever is running without starting anything in its place.
	public void Cancel()
	{
		lock (_lock) CancelCurrentLocked();
	}

	public void Dispose()
	{
		lock (_lock)
		{
			_disposed = true;
			CancelCurrentLocked();
		}
	}

	private void CancelCurrentLocked()
	{
		CancellationTokenSource? cts = _current;
		_current = null;
		if (cts is null) return;

		// Cancel first, then release the source. A worker still holding the token only
		// polls IsCancellationRequested, which keeps answering true after disposal.
		try { cts.Cancel(); } catch (ObjectDisposedException) { }
		cts.Dispose();
	}
}
