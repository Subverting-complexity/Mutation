using System.Threading;
using Mutation.Ui.Core;

namespace Mutation.Tests;

// Covers the cancellation handle added for issue #256. Its whole job is to survive being
// cancelled from the UI thread while the call it belongs to is still between retries, so
// that is what these assert.
public class LlmOperationStateTests
{
	[Fact]
	public void NothingRunning_UntilBegin()
	{
		using var state = new LlmOperationState();

		Assert.False(state.Running);
	}

	[Fact]
	public void Begin_MarksItRunning_AndEnd_ReleasesIt()
	{
		using var state = new LlmOperationState();

		state.Begin();
		Assert.True(state.Running);

		state.End();
		Assert.False(state.Running);
	}

	[Fact]
	public void Cancel_SignalsTheTokenTheCallIsHolding()
	{
		using var state = new LlmOperationState();
		CancellationToken token = state.Begin();

		state.Cancel();

		Assert.True(token.IsCancellationRequested);
	}

	[Fact]
	public void Cancel_LeavesTheSourceUsable_SoARetryCanStillLinkToIt()
	{
		// The reason Cancel does not dispose. A retry links a fresh token to this source on
		// every attempt, and a disposed source would surface ObjectDisposedException in
		// place of the clean cancellation the caller is waiting to catch.
		using var state = new LlmOperationState();
		CancellationToken token = state.Begin();

		state.Cancel();

		using var linked = CancellationTokenSource.CreateLinkedTokenSource(token);
		Assert.True(linked.Token.IsCancellationRequested);
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
	public void End_IsIdempotent()
	{
		using var state = new LlmOperationState();
		state.Begin();

		state.End();
		state.End();

		Assert.False(state.Running);
	}

	[Fact]
	public void Begin_LinksToTheExternalToken_SoAClosingWindowStopsTheCall()
	{
		using var shutdown = new CancellationTokenSource();
		using var state = new LlmOperationState();
		CancellationToken token = state.Begin(shutdown.Token);

		shutdown.Cancel();

		Assert.True(token.IsCancellationRequested);
	}

	[Fact]
	public void Begin_AfterAStaleBegin_CancelsTheOldTokenRatherThanAbandoningIt()
	{
		using var state = new LlmOperationState();
		CancellationToken stale = state.Begin();

		CancellationToken fresh = state.Begin();

		Assert.True(stale.IsCancellationRequested);
		Assert.False(fresh.IsCancellationRequested);
	}

	[Fact]
	public void Dispose_CancelsAnythingStillRunning()
	{
		var state = new LlmOperationState();
		CancellationToken token = state.Begin();

		state.Dispose();

		Assert.True(token.IsCancellationRequested);
		Assert.False(state.Running);
	}
}
