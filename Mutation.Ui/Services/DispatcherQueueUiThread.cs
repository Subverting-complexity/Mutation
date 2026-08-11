using System;
using System.Threading.Tasks;
using Microsoft.UI.Dispatching;

namespace Mutation.Ui.Services;

/// <summary>
/// Runs work on the UI thread through the WinUI dispatcher queue.
/// </summary>
internal sealed class DispatcherQueueUiThread : IUiThreadDispatcher
{
	private readonly DispatcherQueue _queue;

	public DispatcherQueueUiThread(DispatcherQueue queue)
	{
		_queue = queue ?? throw new ArgumentNullException(nameof(queue));
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

		// Deliberately an async void callback: DispatcherQueueHandler returns void. It is safe
		// because every exception the operation can raise is caught here and handed to the
		// caller through the task instead of escaping. The completion calls themselves are not
		// inside that guarantee, but they cannot throw: this source is completed either by the
		// callback or by the not-queued branch below, never by both.
		bool queued = _queue.TryEnqueue(async () =>
		{
			try
			{
				completion.SetResult(await operation());
			}
			catch (Exception ex)
			{
				completion.SetException(ex);
			}
		});

		if (!queued)
			completion.SetException(new InvalidOperationException(
				"The UI thread is shutting down and is no longer accepting work."));

		return completion.Task;
	}
}
