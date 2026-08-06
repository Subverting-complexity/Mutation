using System;
using System.Threading;
using Mutation.Ui.Services;

namespace Mutation.Tests;

/// <summary>
/// The batch OCR run used to be started with <c>CancellationToken.None</c>: forty PDFs
/// picked by mistake ran to the end against the user's Azure quota, and closing the
/// window did not stop them either (issue #227). These pin the ways out — and the ways
/// the run must not lose them.
/// </summary>
public class OcrDocumentsRunControllerTests
{
	[Fact]
	public void Begin_ReturnsAnUncancelledToken()
	{
		using var shutdown = new CancellationTokenSource();
		using var controller = new OcrDocumentsRunController(shutdown.Token);

		OcrDocumentsRun run = controller.Begin();

		Assert.False(run.Token.IsCancellationRequested);
		Assert.True(controller.IsRunning);
	}

	[Fact]
	public void ShutdownCancelsTheRun()
	{
		using var shutdown = new CancellationTokenSource();
		using var controller = new OcrDocumentsRunController(shutdown.Token);
		OcrDocumentsRun run = controller.Begin();

		shutdown.Cancel();

		Assert.True(run.Token.IsCancellationRequested);
	}

	[Fact]
	public void CancelStopsTheRunWithoutTouchingShutdown()
	{
		using var shutdown = new CancellationTokenSource();
		using var controller = new OcrDocumentsRunController(shutdown.Token);
		OcrDocumentsRun run = controller.Begin();

		Assert.True(controller.Cancel());

		Assert.True(run.Token.IsCancellationRequested);
		Assert.False(shutdown.IsCancellationRequested);
	}

	// The caller announces "cancelled" off this return value, so an idle controller has to
	// say no — otherwise a stray press claims it stopped a batch that was never running.
	[Fact]
	public void CancelReportsNothingToCancel_WhenNoRunIsInFlight()
	{
		using var shutdown = new CancellationTokenSource();
		using var controller = new OcrDocumentsRunController(shutdown.Token);

		Assert.False(controller.Cancel());
		Assert.False(controller.IsRunning);
	}

	// The Cancel button stays enabled and focused after a press, so a second press must be
	// silent rather than announcing the same stop again.
	[Fact]
	public void CancelReportsNothingToCancel_OnASecondPress()
	{
		using var shutdown = new CancellationTokenSource();
		using var controller = new OcrDocumentsRunController(shutdown.Token);
		controller.Begin();

		Assert.True(controller.Cancel());
		Assert.False(controller.Cancel());
	}

	[Fact]
	public void CancelReportsNothingToCancel_AfterTheRunEnded()
	{
		using var shutdown = new CancellationTokenSource();
		using var controller = new OcrDocumentsRunController(shutdown.Token);
		OcrDocumentsRun run = controller.Begin();
		controller.End(run);

		Assert.False(controller.Cancel());
		Assert.False(controller.IsRunning);
	}

	[Fact]
	public void EndIsSafeWhenNoRunIsInFlight()
	{
		using var shutdown = new CancellationTokenSource();
		using var controller = new OcrDocumentsRunController(shutdown.Token);

		controller.End(null);

		Assert.False(controller.IsRunning);
	}

	// The scenario this design exists for: a second click that fails to Begin unwinds
	// through its own finally. If End took no handle it would release the *first* run's
	// token source — and disposing a linked source severs it from its parent for good, so
	// the batch still running would stop answering to shutdown or to the Cancel button.
	[Fact]
	public void EndingAForeignRunLeavesTheRunningOneCancellable()
	{
		using var shutdown = new CancellationTokenSource();
		using var controller = new OcrDocumentsRunController(shutdown.Token);
		OcrDocumentsRun live = controller.Begin();

		// The handle a losing caller would be holding: never the current run.
		OcrDocumentsRun foreign = new OcrDocumentsRunController(shutdown.Token).Begin();
		controller.End(foreign);

		Assert.True(controller.IsRunning);
		Assert.True(controller.Cancel());
		Assert.True(live.Token.IsCancellationRequested);
	}

	[Fact]
	public void EndingAForeignRunLeavesTheRunningOneAnsweringToShutdown()
	{
		using var shutdown = new CancellationTokenSource();
		using var controller = new OcrDocumentsRunController(shutdown.Token);
		OcrDocumentsRun live = controller.Begin();
		OcrDocumentsRun foreign = new OcrDocumentsRunController(shutdown.Token).Begin();

		controller.End(foreign);
		shutdown.Cancel();

		Assert.True(live.Token.IsCancellationRequested);
	}

	// A second batch must not inherit the first one's cancellation, or the run would end
	// the moment it started and the user would be told it was cancelled again.
	[Fact]
	public void ASecondRunStartsClean_AfterTheFirstWasCancelled()
	{
		using var shutdown = new CancellationTokenSource();
		using var controller = new OcrDocumentsRunController(shutdown.Token);
		OcrDocumentsRun first = controller.Begin();
		controller.Cancel();
		controller.End(first);

		OcrDocumentsRun second = controller.Begin();

		Assert.True(first.Token.IsCancellationRequested);
		Assert.False(second.Token.IsCancellationRequested);
	}

	[Fact]
	public void BeginThrows_WhenARunIsAlreadyInFlight()
	{
		using var shutdown = new CancellationTokenSource();
		using var controller = new OcrDocumentsRunController(shutdown.Token);
		controller.Begin();

		Assert.Throws<InvalidOperationException>(() => controller.Begin());
	}

	// Releasing without cancelling would leave a batch running against the user's Azure
	// quota with nothing left able to stop it, because a released linked source no longer
	// hears its parent. Dispose must cancel first.
	[Fact]
	public void DisposeCancelsTheRunItReleases()
	{
		using var shutdown = new CancellationTokenSource();
		var controller = new OcrDocumentsRunController(shutdown.Token);
		OcrDocumentsRun run = controller.Begin();

		controller.Dispose();

		Assert.True(run.Token.IsCancellationRequested);
		Assert.False(controller.IsRunning);
		Assert.Throws<ObjectDisposedException>(() => controller.Begin());
	}

	[Fact]
	public void DisposeIsIdempotent()
	{
		using var shutdown = new CancellationTokenSource();
		var controller = new OcrDocumentsRunController(shutdown.Token);
		controller.Begin();

		controller.Dispose();
		controller.Dispose();

		Assert.False(controller.IsRunning);
	}

	[Fact]
	public void DisposeIsSafeWhenNoRunIsInFlight()
	{
		using var shutdown = new CancellationTokenSource();
		var controller = new OcrDocumentsRunController(shutdown.Token);

		controller.Dispose();

		Assert.False(controller.IsRunning);
	}

	// Shutdown already fired before the user got to the picker: the run must come back
	// cancelled rather than starting a batch nothing will ever stop.
	[Fact]
	public void ARunBegunAfterShutdownIsAlreadyCancelled()
	{
		using var shutdown = new CancellationTokenSource();
		shutdown.Cancel();
		using var controller = new OcrDocumentsRunController(shutdown.Token);

		OcrDocumentsRun run = controller.Begin();

		Assert.True(run.Token.IsCancellationRequested);
	}
}
