using System;
using System.Threading.Tasks;
using Mutation.Ui.Services;
using Xunit;

namespace Mutation.Tests;

/// <summary>
/// Accepting a piece of work is not the same as running it. A dispatcher queue answers "yes,
/// queued" and then, if it shuts down before that callback's turn comes, drops it without telling
/// anybody — so whatever was awaiting it never resumes (issue #361). These pin the bookkeeping
/// that turns that silence into a failure the caller can act on.
/// <para>
/// This is the testable half, and it is worth saying exactly how much of the story it is. These
/// pin the rules of the bookkeeping itself: one answer per call, nothing held onto on the normal
/// path, and no <em>tracked</em> call left waiting after a shutdown, whichever order the two
/// arrive in. They say nothing about the dispatcher as a whole. A call that runs in place because
/// the caller was already on the UI thread is never tracked, and can still be lost if a
/// continuation of its own is dropped at shutdown — a path that predates this and that nothing
/// here reaches.
/// </para>
/// <para>
/// <c>DispatcherQueueUiThread</c> itself needs a real WinUI dispatcher queue on a real UI thread,
/// shut down while a call is still in it, and this test assembly can create none of those. What
/// is checked there is the wiring — register before enqueuing, release on the way out, sweep on
/// <c>ShutdownCompleted</c> — and it is checked by reading it, not by running it.
/// </para>
/// </summary>
public class PendingUiCallsTests
{
	[Fact]
	public void ACallThatAnswersOnItsOwnIsForgotten()
	{
		var pending = new PendingUiCalls();
		Exception? abandonedWith = null;

		long token = pending.Track(reason => abandonedWith = reason);
		Assert.Equal(1, pending.Count);

		pending.Release(token);

		Assert.Equal(0, pending.Count);
		Assert.Null(abandonedWith);
	}

	/// <summary>
	/// The leak the tracking exists to prevent is not the dropped callback — it is this list
	/// growing for the whole life of the app. Every clipboard call goes through it.
	/// </summary>
	[Fact]
	public void NothingIsHeldOntoAfterCallsAnswerOutOfOrder()
	{
		var pending = new PendingUiCalls();

		long first = pending.Track(_ => { });
		long second = pending.Track(_ => { });
		long third = pending.Track(_ => { });

		pending.Release(second);
		pending.Release(third);
		pending.Release(first);

		Assert.Equal(0, pending.Count);
	}

	[Fact]
	public void ACallStillWaitingWhenTheUiThreadStopsIsFailed()
	{
		var pending = new PendingUiCalls();
		Exception? abandonedWith = null;
		var reason = new InvalidOperationException("the UI thread is gone");

		pending.Track(r => abandonedWith = r);
		pending.AbandonAll(reason);

		Assert.Same(reason, abandonedWith);
		Assert.Equal(0, pending.Count);
	}

	/// <summary>
	/// A call registered after the sweep is failed as it arrives rather than tracked. Without
	/// this, a call that slipped into the gap between the sweep and the queue starting to refuse
	/// work would be the one thing nothing could reach — which is the same hang, moved.
	/// </summary>
	[Fact]
	public void ACallRegisteredAfterTheUiThreadStoppedIsFailedImmediately()
	{
		var pending = new PendingUiCalls();
		Exception? abandonedWith = null;
		var reason = new InvalidOperationException("the UI thread is gone");

		pending.AbandonAll(reason);
		long token = pending.Track(r => abandonedWith = r);

		Assert.Same(reason, abandonedWith);
		Assert.Equal(PendingUiCalls.NotTracked, token);
		Assert.Equal(0, pending.Count);
	}

	/// <summary>
	/// Releasing after a sweep has already failed the call is harmless. It is the ordinary race:
	/// the sweep fails the call, and the dropped callback's own cleanup — or the queue refusing
	/// it outright — arrives afterwards.
	/// </summary>
	[Fact]
	public void ReleasingACallThatWasAlreadyFailedDoesNothing()
	{
		var pending = new PendingUiCalls();
		int abandonCount = 0;

		long token = pending.Track(_ => abandonCount++);
		pending.AbandonAll(new InvalidOperationException("the UI thread is gone"));
		pending.Release(token);

		Assert.Equal(1, abandonCount);
		Assert.Equal(0, pending.Count);
	}

	[Fact]
	public void ReleasingACallThatWasNeverTrackedDoesNothing()
	{
		var pending = new PendingUiCalls();

		pending.Release(PendingUiCalls.NotTracked);

		Assert.Equal(0, pending.Count);
	}

	/// <summary>
	/// The whole point, stated in the terms the caller sees it in: a task that would otherwise
	/// wait forever completes as a failure instead. The retry above these calls knows what to do
	/// with a failure and has nothing to do with a wait that never ends.
	/// </summary>
	[Fact]
	public async Task AWaitingCallerIsFaultedRatherThanLeftWaiting()
	{
		var pending = new PendingUiCalls();
		var completion = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);

		pending.Track(reason => completion.TrySetException(reason));
		pending.AbandonAll(new InvalidOperationException("the UI thread is gone"));

		var thrown = await Assert.ThrowsAsync<InvalidOperationException>(() => completion.Task);
		Assert.Equal("the UI thread is gone", thrown.Message);
	}

	/// <summary>
	/// Only the first answer counts. A sweep and a callback that ran anyway can both try, and the
	/// second must not turn a completed call into a crash.
	/// </summary>
	[Fact]
	public async Task TheFirstAnswerWins_WhenACallAndASweepRace()
	{
		var pending = new PendingUiCalls();
		var completion = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);

		long token = pending.Track(reason => completion.TrySetException(reason));
		completion.TrySetResult(42);
		pending.AbandonAll(new InvalidOperationException("the UI thread is gone"));
		pending.Release(token);

		Assert.Equal(42, await completion.Task);
	}
}
