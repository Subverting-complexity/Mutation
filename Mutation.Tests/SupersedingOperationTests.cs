using System.Threading;
using CognitiveSupport;
using Xunit;

namespace Mutation.Tests;

// Covers the handover that makes one read replace another in TextToSpeechService.
// The bug this guards against (issue #236) was cancelling the outgoing token *after*
// the replacement had been published and the caller's lock released: a worker for the
// superseded read could get in during that window, see a live token, and read stale
// text in full before the text the user actually asked for.
//
// Speaking itself needs a synthesizer and an audio device, so it is not exercised here.
// The handover does not, and it is where the ordering lives.
public class SupersedingOperationTests
{
	[Fact]
	public void Begin_HandsOutALiveToken()
	{
		using var operation = new SupersedingOperation();

		CancellationToken token = operation.Begin();

		Assert.False(token.IsCancellationRequested);
	}

	[Fact]
	public void Begin_CancelsThePreviousOperationBeforeItReturns()
	{
		using var operation = new SupersedingOperation();
		CancellationToken first = operation.Begin();

		CancellationToken second = operation.Begin();

		// The point of the ordering: by the time the caller is holding the new token —
		// still inside its own lock — the old one is already dead, so a worker for the
		// old operation cannot find a live token whichever order the two threads run in.
		Assert.True(first.IsCancellationRequested);
		Assert.False(second.IsCancellationRequested);
	}

	[Fact]
	public void Begin_CancelsEachOperationInTurn()
	{
		using var operation = new SupersedingOperation();

		CancellationToken first = operation.Begin();
		CancellationToken second = operation.Begin();
		CancellationToken third = operation.Begin();

		Assert.True(first.IsCancellationRequested);
		Assert.True(second.IsCancellationRequested);
		Assert.False(third.IsCancellationRequested);
	}

	[Fact]
	public void Cancel_EndsTheCurrentOperationWithoutStartingAnother()
	{
		using var operation = new SupersedingOperation();
		CancellationToken token = operation.Begin();

		operation.Cancel();

		Assert.True(token.IsCancellationRequested);
	}

	[Fact]
	public void Cancel_WithNothingRunning_IsANoOp()
	{
		using var operation = new SupersedingOperation();

		operation.Cancel();
		operation.Cancel();

		// Still usable afterwards: a Stop with nothing playing must not wedge the service.
		Assert.False(operation.Begin().IsCancellationRequested);
	}

	[Fact]
	public void Dispose_CancelsWhateverIsRunning()
	{
		var operation = new SupersedingOperation();
		CancellationToken token = operation.Begin();

		operation.Dispose();

		Assert.True(token.IsCancellationRequested);
	}

	[Fact]
	public void Begin_AfterDispose_HandsBackAnAlreadyCancelledToken()
	{
		var operation = new SupersedingOperation();
		operation.Dispose();

		CancellationToken token = operation.Begin();

		// A caller racing shutdown gets a token that stops its worker at the first check,
		// rather than one that nothing will ever cancel.
		Assert.True(token.IsCancellationRequested);
	}

	[Fact]
	public void Dispose_IsIdempotent()
	{
		var operation = new SupersedingOperation();
		operation.Begin();

		operation.Dispose();
		operation.Dispose();
	}
}
