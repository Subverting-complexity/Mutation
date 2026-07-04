using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Mutation.Ui.Core;
using Xunit;

namespace Mutation.Tests;

public class DialogQueueTests
{
	[Fact]
	public async Task SingleDialog_ShowsImmediately_AndReturnsItsResult()
	{
		var queue = new DialogQueue<int>();

		int result = await queue.EnqueueAsync(() => Task.FromResult(42));

		Assert.Equal(42, result);
		Assert.False(queue.IsBusy);
		Assert.Equal(0, queue.PendingCount);
	}

	[Fact]
	public async Task SecondDialog_IsQueuedAndShownWhenFirstCloses_InFifoOrder()
	{
		var queue = new DialogQueue<int>();
		// Default TaskCompletionSource (inline continuations) so the queue's
		// pump advances synchronously on this thread the moment a gate is
		// completed — keeps the test deterministic and single-threaded, which
		// mirrors the queue's UI-dispatcher affinity.
		var gateA = new TaskCompletionSource<int>();
		var gateB = new TaskCompletionSource<int>();
		var shownOrder = new List<string>();

		Task<int> first = queue.EnqueueAsync(() => { shownOrder.Add("A"); return gateA.Task; });
		Task<int> second = queue.EnqueueAsync(() => { shownOrder.Add("B"); return gateB.Task; });

		// A is on screen; B has not started and is waiting behind it.
		Assert.Equal(new[] { "A" }, shownOrder);
		Assert.True(queue.IsBusy);
		Assert.Equal(1, queue.PendingCount);

		// Closing A lets the queue show B next.
		gateA.SetResult(1);
		Assert.Equal(new[] { "A", "B" }, shownOrder);
		Assert.Equal(0, queue.PendingCount);
		Assert.True(queue.IsBusy);

		// Closing B drains the queue.
		gateB.SetResult(2);
		Assert.False(queue.IsBusy);

		Assert.Equal(1, await first);
		Assert.Equal(2, await second);
	}

	[Fact]
	public async Task FaultingShow_FaultsOnlyItsOwnTask_AndTheQueueKeepsDraining()
	{
		var queue = new DialogQueue<int>();
		var boom = new InvalidOperationException("boom");

		Task<int> failing = queue.EnqueueAsync(() => Task.FromException<int>(boom));
		Task<int> following = queue.EnqueueAsync(() => Task.FromResult(7));

		var thrown = await Assert.ThrowsAsync<InvalidOperationException>(() => failing);
		Assert.Same(boom, thrown);
		Assert.Equal(7, await following);
		Assert.False(queue.IsBusy);
		Assert.Equal(0, queue.PendingCount);
	}

	[Fact]
	public void EnqueueAsync_NullShowOperation_Throws()
	{
		var queue = new DialogQueue<int>();

		// The null guard throws synchronously before any task is created, so
		// Assert.Throws is correct despite EnqueueAsync's Task return type; the
		// analyzer cannot see that the throw is synchronous, so suppress it.
#pragma warning disable xUnit2014
		Assert.Throws<ArgumentNullException>(() =>
		{
			queue.EnqueueAsync(null!);
		});
#pragma warning restore xUnit2014
	}
}
