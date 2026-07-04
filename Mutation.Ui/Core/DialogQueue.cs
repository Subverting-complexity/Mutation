using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Mutation.Ui.Core;

/// <summary>
/// Serializes modal dialogs onto a single "one at a time" channel so a
/// dialog requested while another is already open is queued and shown when
/// the current one closes, instead of being silently dropped.
///
/// The queue is affine to the thread that drives it (the UI dispatcher in
/// this app): every member must be called from that one thread. It holds no
/// locks because that single-threaded contract removes the races a lock
/// would guard; the awaited show operations still run asynchronously and
/// yield the thread while a dialog is on screen.
/// </summary>
/// <typeparam name="TResult">The result each show operation produces (e.g.
/// the button the user chose).</typeparam>
public sealed class DialogQueue<TResult>
{
	private readonly Queue<PendingDialog> _pending = new();
	private bool _isPumping;

	/// <summary>True while a dialog is showing or waiting to show.</summary>
	public bool IsBusy => _isPumping;

	/// <summary>How many dialogs are waiting behind the one on screen.</summary>
	public int PendingCount => _pending.Count;

	/// <summary>
	/// Queue a dialog-show operation. The returned task completes with the
	/// operation's result once the dialog has actually been shown and closed,
	/// so callers still <c>await</c> the real outcome whether it ran
	/// immediately or waited its turn. A show operation that throws faults
	/// only its own task; the queue keeps draining the rest.
	/// </summary>
	public Task<TResult> EnqueueAsync(Func<Task<TResult>> showAsync)
	{
		ArgumentNullException.ThrowIfNull(showAsync);

		// RunContinuationsAsynchronously so an awaiting UI handler resumes on
		// the dispatcher rather than nested inside the pump loop.
		var completion = new TaskCompletionSource<TResult>(
			TaskCreationOptions.RunContinuationsAsynchronously);
		_pending.Enqueue(new PendingDialog(showAsync, completion));

		if (!_isPumping)
		{
			_isPumping = true;
			_ = PumpAsync();
		}

		return completion.Task;
	}

	private async Task PumpAsync()
	{
		while (_pending.Count > 0)
		{
			PendingDialog dialog = _pending.Dequeue();
			try
			{
				dialog.Completion.TrySetResult(await dialog.ShowAsync());
			}
			catch (Exception ex)
			{
				dialog.Completion.TrySetException(ex);
			}
		}

		_isPumping = false;
	}

	private readonly record struct PendingDialog(
		Func<Task<TResult>> ShowAsync,
		TaskCompletionSource<TResult> Completion);
}
