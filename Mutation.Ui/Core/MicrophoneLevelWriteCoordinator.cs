using System;
using System.Threading;
using System.Threading.Tasks;

namespace Mutation.Ui.Core;

/// <summary>
/// Serializes microphone capture-level writes onto a single background worker so the
/// COM write, its read-back verification, and any device re-enumeration in the
/// underlying write never run on the UI thread (which would freeze the UI and, with
/// it, the screen reader). The write itself is injected as a delegate, keeping this
/// coordinator unit-testable without audio hardware or a UI.
///
/// Unlike <see cref="MicrophoneMuteToggleCoordinator"/>, which drops an overlapping
/// toggle, this coalesces to the latest requested level. A slider drag or a held
/// arrow key raises a burst of level requests, and only the most recent one needs to
/// reach the device — writing every intermediate value would apply a backlog of
/// stale levels and could settle on the wrong one. So a request that has not started
/// yet when a newer one arrives is superseded and dropped; the device ends up at the
/// value the user actually chose.
///
/// Because a single instance is shared by every level writer (the slider, the pin
/// toggle, and the record-start re-assert), it is also the one serialization point
/// for the level endpoint: two threads never write the same COM object at once.
/// <see cref="ReadCurrentLevelAsync"/> joins the same serialization — a display read
/// is a COM read of the very endpoint the writes target, so it runs on a background
/// thread and never overlaps a write.
/// </summary>
public sealed class MicrophoneLevelWriteCoordinator
{
	private readonly Func<int, CaptureLevelResult> _write;
	private readonly Func<int?> _readLevel;
	private readonly object _gate = new();

	// Held across the actual device access, by both the drain worker's write and a
	// display read, so the two never touch the endpoint concurrently. A semaphore
	// rather than a lock so a read can wait for it asynchronously instead of parking a
	// thread-pool thread for the length of a stalled write. Never waited on from the
	// UI thread — blocking there is exactly the freeze this coordinator exists to
	// prevent.
	private readonly SemaphoreSlim _endpointGate = new(1, 1);

	// The most recent level requested but not yet being written, and the awaiter tied
	// to it. Both are cleared the moment the worker picks the request up. Guarded by
	// _gate.
	private int? _pendingLevel;
	private TaskCompletionSource<CaptureLevelResult?>? _pendingCompletion;

	// True while the drain worker is alive (a write is in flight or a request is
	// queued). Guarded by _gate.
	private bool _workerRunning;

	public MicrophoneLevelWriteCoordinator(Func<int, CaptureLevelResult> write, Func<int?> readLevel)
	{
		_write = write ?? throw new ArgumentNullException(nameof(write));
		_readLevel = readLevel ?? throw new ArgumentNullException(nameof(readLevel));
	}

	/// <summary>
	/// True while a level write started by this coordinator is in flight or a newer
	/// request is queued behind it.
	/// </summary>
	public bool IsWriting
	{
		get { lock (_gate) return _workerRunning; }
	}

	/// <summary>
	/// Requests that <paramref name="level"/> be written to the device, coalescing to
	/// the latest request. The write runs on a background thread. The returned task
	/// completes with the write's <see cref="CaptureLevelResult"/> when this request
	/// is the one applied, or with <c>null</c> when a newer request superseded it
	/// before it started. The continuation resumes on the caller's synchronization
	/// context (the UI thread when awaited from a UI event), so the caller can update
	/// the UI directly with the outcome.
	/// </summary>
	public Task<CaptureLevelResult?> RequestLatestAsync(int level)
	{
		TaskCompletionSource<CaptureLevelResult?>? superseded;
		var mine = new TaskCompletionSource<CaptureLevelResult?>(
			TaskCreationOptions.RunContinuationsAsynchronously);
		bool startWorker = false;

		lock (_gate)
		{
			// Any request still queued (not yet started) is now stale — this newer one
			// replaces it.
			superseded = _pendingCompletion;
			_pendingLevel = level;
			_pendingCompletion = mine;

			if (!_workerRunning)
			{
				_workerRunning = true;
				startWorker = true;
			}
		}

		// A dropped request completes with null. Done outside the lock so a continuation
		// cannot run while it is held.
		superseded?.TrySetResult(null);

		if (startWorker)
			_ = Task.Run(Drain);

		return mine.Task;
	}

	/// <summary>
	/// Reads the device's current capture level (0–100, or <c>null</c> when it cannot
	/// be read) on a background thread, serialized against the level writes. Callers on
	/// the UI thread — the window-activation display re-sync — use this instead of
	/// reading the endpoint directly: the read is a COM call that can stall on a slow
	/// or failing device, which would freeze the window and, with it, the screen
	/// reader. The continuation resumes on the caller's synchronization context, so the
	/// UI can be updated directly with the result.
	/// </summary>
	public async Task<int?> ReadCurrentLevelAsync()
	{
		// Waited on asynchronously: a queued read holds no thread while a slow write is
		// in flight.
		await _endpointGate.WaitAsync().ConfigureAwait(false);
		try
		{
			return await Task.Run(_readLevel).ConfigureAwait(false);
		}
		catch
		{
			// The injected read already degrades a stale COM proxy to null on its own,
			// so nothing routine reaches here; this is the backstop that keeps an
			// unexpected fault from faulting the awaiter. Null means "level unknown",
			// and the caller leaves its display unchanged rather than showing a wrong
			// value.
			return null;
		}
		finally
		{
			_endpointGate.Release();
		}
	}

	// Applies queued level requests one at a time until none remain. Only the request
	// currently held in _pendingLevel is applied each pass, so a burst that arrives
	// during a write collapses to its most recent value.
	private void Drain()
	{
		while (true)
		{
			int level;
			TaskCompletionSource<CaptureLevelResult?> completion;

			lock (_gate)
			{
				if (_pendingLevel is not int pending)
				{
					_workerRunning = false;
					return;
				}

				level = pending;
				completion = _pendingCompletion!;
				_pendingLevel = null;
				_pendingCompletion = null;
			}

			CaptureLevelResult result;
			try
			{
				// Serialized with ReadCurrentLevelAsync: the write and the display read
				// target the same COM endpoint. Waited on synchronously — this is the
				// coordinator's own worker, whose whole job is to block on the device.
				_endpointGate.Wait();
				try
				{
					result = _write(level);
				}
				finally
				{
					_endpointGate.Release();
				}
			}
			catch
			{
				// The injected write already treats device faults as a failed write and
				// returns Failed; this guard only keeps an unexpected throw from killing
				// the worker or leaving the awaiter hanging.
				result = new CaptureLevelResult(CaptureLevelOutcome.Failed);
			}

			completion.TrySetResult(result);
		}
	}
}
