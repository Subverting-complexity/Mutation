using System;
using System.Collections.Generic;

namespace Mutation.Ui.Services;

/// <summary>
/// Keeps track of work handed to the UI thread that has not come back yet, so that when the UI
/// thread stops running for good, everything still waiting can be told rather than left waiting
/// forever.
/// <para>
/// This exists because accepting a piece of work is not the same as running it. A dispatcher
/// queue takes a callback and answers "yes, queued"; if the queue shuts down before that
/// callback's turn comes, the callback is dropped and nobody is told. Whatever was awaiting it
/// never resumes (issue #361). This is the bookkeeping that lets the shutdown say so.
/// </para>
/// <para>
/// Split from the dispatcher itself because it is the only part that can be tested. A test
/// assembly has no UI thread and cannot create a real dispatcher queue, let alone shut one down
/// mid-flight, so the wiring in <see cref="DispatcherQueueUiThread"/> is checked by reading it.
/// The rules that matter — one answer per call, no leak on the normal path, and no call left
/// waiting after a shutdown, whichever order the two arrive in — live here and are tested.
/// </para>
/// </summary>
internal sealed class PendingUiCalls
{
	private readonly object _gate = new();
	private readonly Dictionary<long, Action<Exception>> _pending = new();
	private long _nextToken;
	private Exception? _abandonReason;

	/// <summary>How many calls are outstanding. For tests and diagnostics.</summary>
	public int Count
	{
		get { lock (_gate) return _pending.Count; }
	}

	/// <summary>
	/// Registers a call that is about to be handed to the UI thread, returning the token used to
	/// release it again. <paramref name="abandon"/> is how this call is failed if the UI thread
	/// stops before it runs; it is invoked at most once, and never while the lock is held.
	/// <para>
	/// Registering after the UI thread has already stopped abandons the call immediately and
	/// returns <see cref="NotTracked"/>. Without that, a call registered in the gap between the
	/// shutdown sweep and the queue starting to refuse work would be the one thing the sweep
	/// could not reach.
	/// </para>
	/// </summary>
	public long Track(Action<Exception> abandon)
	{
		if (abandon is null)
			throw new ArgumentNullException(nameof(abandon));

		Exception? alreadyOver;

		lock (_gate)
		{
			alreadyOver = _abandonReason;
			if (alreadyOver is null)
			{
				long token = ++_nextToken;
				_pending.Add(token, abandon);
				return token;
			}
		}

		abandon(alreadyOver);
		return NotTracked;
	}

	/// <summary>
	/// The token for a call that was never tracked, because the UI thread had already stopped.
	/// Releasing it does nothing, so callers do not have to check.
	/// </summary>
	public const long NotTracked = 0;

	/// <summary>
	/// Forgets a call that answered on its own. Safe to call for a token that was already
	/// abandoned or never tracked.
	/// </summary>
	public void Release(long token)
	{
		if (token == NotTracked)
			return;

		lock (_gate)
			_pending.Remove(token);
	}

	/// <summary>
	/// Fails every outstanding call with <paramref name="reason"/>, and every call registered
	/// afterwards as it arrives. There is no way back from this: once the UI thread has stopped,
	/// it does not start again.
	/// </summary>
	public void AbandonAll(Exception reason)
	{
		if (reason is null)
			throw new ArgumentNullException(nameof(reason));

		Action<Exception>[] abandoning;

		lock (_gate)
		{
			_abandonReason ??= reason;
			abandoning = new Action<Exception>[_pending.Count];
			_pending.Values.CopyTo(abandoning, 0);
			_pending.Clear();
		}

		// Outside the lock: each of these completes a task, which can run a continuation inline,
		// and a continuation that came back here for anything would deadlock against a lock held
		// across the callback.
		foreach (var abandon in abandoning)
			abandon(reason);
	}
}
