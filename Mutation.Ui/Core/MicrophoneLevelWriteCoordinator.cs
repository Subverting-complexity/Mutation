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
/// </summary>
public sealed class MicrophoneLevelWriteCoordinator
{
	private readonly Func<int, CaptureLevelResult> _write;
	private readonly object _gate = new();

	// The most recent level requested but not yet being written, and the awaiter tied
	// to it. Both are cleared the moment the worker picks the request up. Guarded by
	// _gate.
	private int? _pendingLevel;
	private TaskCompletionSource<CaptureLevelResult?>? _pendingCompletion;

	// True while the drain worker is alive (a write is in flight or a request is
	// queued). Guarded by _gate.
	private bool _workerRunning;

	public MicrophoneLevelWriteCoordinator(Func<int, CaptureLevelResult> write)
	{
		_write = write ?? throw new ArgumentNullException(nameof(write));
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
				result = _write(level);
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
