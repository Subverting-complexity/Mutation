using System.Threading;
using System.Threading.Tasks;
using Mutation.Ui.Core;

namespace Mutation.Tests;

// Covers the cancellation handle added for issue #256. Its whole job is to survive being
// cancelled from the UI thread while the call it belongs to is still between retries, and
// to make sure one run can never release or cancel another's claim, so that is what these
// assert.
public class LlmOperationStateTests
{
	[Fact]
	public void NothingRunning_UntilBegin()
	{
		using var state = new LlmOperationState();

		Assert.False(state.Running);
		Assert.False(state.CancelRequested);
	}

	[Fact]
	public void Begin_MarksItRunning_AndDisposingTheRunReleasesIt()
	{
		using var state = new LlmOperationState();

		using (state.Begin())
		{
			Assert.True(state.Running);
		}

		Assert.False(state.Running);
	}

	[Fact]
	public void Cancel_SignalsTheTokenTheCallIsHolding()
	{
		using var state = new LlmOperationState();
		using var run = state.Begin();

		state.Cancel();

		Assert.True(run.Token.IsCancellationRequested);
	}

	[Fact]
	public void Cancel_LeavesTheSourceUsable_SoARetryCanStillLinkToIt()
	{
		// The reason Cancel does not dispose. A retry links a fresh token to this source on
		// every attempt, and a disposed source would surface ObjectDisposedException in
		// place of the clean cancellation the caller is waiting to catch.
		using var state = new LlmOperationState();
		using var run = state.Begin();

		state.Cancel();

		using var linked = CancellationTokenSource.CreateLinkedTokenSource(run.Token);
		Assert.True(linked.Token.IsCancellationRequested);
	}

	[Fact]
	public void CancelRequested_IsTrueOnlyWhileTheCancelledCallIsStillWindingDown()
	{
		// What the UI reads to answer a repeat press with "already stopping" instead of
		// re-announcing the request — hearing the same sentence twice is issue #299.
		using var state = new LlmOperationState();
		var run = state.Begin();
		Assert.False(state.CancelRequested);

		state.Cancel();
		Assert.True(state.CancelRequested);

		run.Dispose();
		Assert.False(state.CancelRequested);
	}

	[Fact]
	public void Cancel_WithNothingRunning_DoesNothing()
	{
		// So the UI never has to check first.
		using var state = new LlmOperationState();

		state.Cancel();

		Assert.False(state.Running);
	}

	[Fact]
	public void DisposingARunTwice_IsHarmless()
	{
		using var state = new LlmOperationState();
		var run = state.Begin();

		run.Dispose();
		run.Dispose();

		Assert.False(state.Running);
	}

	[Fact]
	public void Begin_LinksToTheExternalToken_SoAClosingWindowStopsTheCall()
	{
		using var shutdown = new CancellationTokenSource();
		using var state = new LlmOperationState();
		using var run = state.Begin(shutdown.Token);

		shutdown.Cancel();

		Assert.True(run.Token.IsCancellationRequested);
	}

	[Fact]
	public void Begin_AfterAStaleBegin_CancelsTheOldTokenRatherThanAbandoningIt()
	{
		using var state = new LlmOperationState();
		var stale = state.Begin();

		var fresh = state.Begin();

		Assert.True(stale.Token.IsCancellationRequested);
		Assert.False(fresh.Token.IsCancellationRequested);
	}

	// ----- Ownership. The reason Begin hands back a handle instead of pairing with End().

	[Fact]
	public void ASupersededRunReleasingItsClaim_DoesNotCancelTheRunThatTookOver()
	{
		// The bug this shape exists to prevent. Two runs overlap; the second supersedes the
		// first; the first then unwinds and releases. Releasing "whatever is current" took
		// the second run's source with it, so a single double-start killed both calls and
		// left Running reading false with a request still in flight.
		using var state = new LlmOperationState();
		var first = state.Begin();
		var second = state.Begin();

		first.Dispose();

		Assert.False(second.Token.IsCancellationRequested);
		Assert.True(state.Running);
	}

	[Fact]
	public void ASupersededRunReleasingItsClaim_LeavesTheSlotHeldByTheCurrentRun()
	{
		using var state = new LlmOperationState();
		var first = state.Begin();
		var second = state.Begin();
		first.Dispose();

		// Still the second run's to release, and releasing it does free the slot.
		second.Dispose();

		Assert.False(state.Running);
	}

	[Fact]
	public void ReleasingARun_DoesNotCancelItsOwnToken_SoATimeoutIsNotMistakenForACancel()
	{
		// The callers ask the token "did somebody cancel this, or did the provider simply
		// never answer?" to decide whether to report a cancel or a failure. Cancelling on
		// release would answer "cancelled" for every ordinary completion.
		using var state = new LlmOperationState();
		var run = state.Begin();

		run.Dispose();

		Assert.False(run.Token.IsCancellationRequested);
	}

	[Fact]
	public void Dispose_CancelsAnythingStillRunning()
	{
		var state = new LlmOperationState();
		var run = state.Begin();

		state.Dispose();

		Assert.True(run.Token.IsCancellationRequested);
		Assert.False(state.Running);
	}

	[Fact]
	public async Task Cancel_FromAnotherThread_WhileTheRunIsLive_IsSafe()
	{
		// The class doc's whole premise: Cancel arrives from the UI thread while the call is
		// still going on a worker. Neither side may see a torn or disposed source.
		using var state = new LlmOperationState();
		using var run = state.Begin();

		await Task.WhenAll(
			Task.Run(() => { for (int i = 0; i < 200; i++) state.Cancel(); }),
			Task.Run(() =>
			{
				for (int i = 0; i < 200; i++)
				{
					// What a retry does on every attempt.
					using var linked = CancellationTokenSource.CreateLinkedTokenSource(run.Token);
					_ = linked.Token.IsCancellationRequested;
					_ = state.Running;
					_ = state.CancelRequested;
				}
			}));

		Assert.True(run.Token.IsCancellationRequested);
	}
}
