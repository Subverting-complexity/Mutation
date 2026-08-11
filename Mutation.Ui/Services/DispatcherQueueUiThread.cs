using System;
using System.Threading.Tasks;
using Microsoft.UI.Dispatching;

namespace Mutation.Ui.Services;

/// <summary>
/// Runs work on the UI thread through the WinUI dispatcher queue.
/// </summary>
internal sealed class DispatcherQueueUiThread : IUiThreadDispatcher
{
	/// <summary>
	/// Said to anything still waiting on the UI thread when there is no longer a UI thread to
	/// wait for. A failure, deliberately: the retry above these calls knows what to do with a
	/// failure and has nothing to do with a wait that never ends.
	/// </summary>
	private const string ShuttingDownMessage =
		"The UI thread is shutting down and is no longer accepting work.";

	private readonly DispatcherQueue _queue;
	private readonly PendingUiCalls _pending = new();

	public DispatcherQueueUiThread(DispatcherQueue queue)
	{
		_queue = queue ?? throw new ArgumentNullException(nameof(queue));

		// ShutdownCompleted, not ShutdownStarting. The queue goes on draining what it already
		// holds after shutdown starts, so failing everything at that point would abandon calls
		// that were about to succeed. By the time this fires, anything still outstanding never
		// ran and never will.
		_queue.ShutdownCompleted += OnShutdownCompleted;
	}

	public Task<T> RunAsync<T>(Func<Task<T>> operation)
	{
		if (operation is null)
			throw new ArgumentNullException(nameof(operation));

		if (_queue.HasThreadAccess)
			return operation();

		// RunContinuationsAsynchronously keeps whatever awaits this task off the UI thread.
		// Without it the awaiter's continuation runs inline on the UI thread inside the
		// dispatched callback, which would put the retry loop's own bookkeeping — its counter,
		// its catch, its delay — on the UI thread between attempts. Only the clipboard call
		// itself needs to be there.
		var completion = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);

		// Registered before the callback is handed over, so a shutdown that lands between the
		// two finds this call rather than missing it. The reverse order leaves exactly the gap
		// this is here to close.
		long token = _pending.Track(reason => completion.TrySetException(reason));

		// Deliberately an async void callback: DispatcherQueueHandler returns void. It is safe
		// because every exception the operation can raise is caught here and handed to the
		// caller through the task instead of escaping.
		//
		// TrySetResult rather than SetResult, and the same for the two exception paths: a
		// shutdown sweep can reach this call at any moment, so more than one of them may try to
		// answer and the first answer must simply win. Only one of them can be the winner, and
		// it is always the one that ran first.
		bool queued = _queue.TryEnqueue(async () =>
		{
			try
			{
				completion.TrySetResult(await operation());
			}
			catch (Exception ex)
			{
				completion.TrySetException(ex);
			}
			finally
			{
				_pending.Release(token);
			}
		});

		// A queue that refuses the callback outright is the honest case, and it always was
		// handled. The one that needed the bookkeeping above is the queue that accepts a
		// callback and then shuts down before running it (issue #361): accepted is not run.
		if (!queued)
		{
			_pending.Release(token);
			completion.TrySetException(new InvalidOperationException(ShuttingDownMessage));
		}

		return completion.Task;
	}

	private void OnShutdownCompleted(DispatcherQueue sender, object args) =>
		_pending.AbandonAll(new InvalidOperationException(ShuttingDownMessage));
}
