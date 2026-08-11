using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;
using Mutation.Ui.Services;

namespace Mutation.Tests;

/// <summary>
/// A stand-in for the WinUI dispatcher: one dedicated thread that work is queued onto, so a
/// test can name the thread a call was supposed to be made on and then check that it was.
/// <para>
/// Unlike the real UI thread this one has no synchronization context, so an operation that
/// genuinely suspends would resume on the thread pool rather than back here. Hand it operations
/// that complete synchronously — that is what keeps a thread assertion honest.
/// </para>
/// </summary>
internal sealed class SingleThreadUiDispatcher : IUiThreadDispatcher, IDisposable
{
	private readonly BlockingCollection<Action> _work = new();
	private readonly Thread _thread;
	private int _dispatchCount;

	public SingleThreadUiDispatcher()
	{
		_thread = new Thread(() =>
		{
			foreach (var item in _work.GetConsumingEnumerable())
				item();
		})
		{ IsBackground = true, Name = nameof(SingleThreadUiDispatcher) };

		_thread.Start();
	}

	/// <summary>The thread every dispatched call is made on.</summary>
	public int ThreadId => _thread.ManagedThreadId;

	/// <summary>
	/// How many calls were handed across from another thread. Calls already on this thread are
	/// run in place and are not counted, the same way the real dispatcher skips the queue when
	/// it is already where it needs to be.
	/// </summary>
	public int DispatchCount => Volatile.Read(ref _dispatchCount);

	public Task<T> RunAsync<T>(Func<Task<T>> operation)
	{
		if (operation is null)
			throw new ArgumentNullException(nameof(operation));

		if (Environment.CurrentManagedThreadId == _thread.ManagedThreadId)
			return operation();

		Interlocked.Increment(ref _dispatchCount);

		var completion = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);

		_work.Add(() =>
		{
			try
			{
				operation().ContinueWith(
					finished =>
					{
						if (finished.IsFaulted)
							completion.SetException(finished.Exception!.InnerExceptions);
						else if (finished.IsCanceled)
							completion.SetCanceled();
						else
							completion.SetResult(finished.Result);
					},
					TaskContinuationOptions.ExecuteSynchronously);
			}
			catch (Exception ex)
			{
				completion.SetException(ex);
			}
		});

		return completion.Task;
	}

	/// <summary>
	/// Stops the worker thread and waits for it, so a test that asserts on what ran has nothing
	/// still running behind it. Work handed over after this throws out of <c>RunAsync</c> rather
	/// than faulting a task nobody is waiting on any more.
	/// </summary>
	public void Dispose()
	{
		_work.CompleteAdding();
		_thread.Join(TimeSpan.FromSeconds(5));
		_work.Dispose();
	}
}
