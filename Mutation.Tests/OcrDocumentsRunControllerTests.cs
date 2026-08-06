using System;
using System.Threading;
using Mutation.Ui.Services;

namespace Mutation.Tests;

/// <summary>
/// The batch OCR run used to be started with <c>CancellationToken.None</c>: forty PDFs
/// picked by mistake ran to the end against the user's Azure quota, and closing the
/// window did not stop them either (issue #227). These pin the two ways out.
/// </summary>
public class OcrDocumentsRunControllerTests
{
	[Fact]
	public void Begin_ReturnsAnUncancelledToken()
	{
		using var shutdown = new CancellationTokenSource();
		using var controller = new OcrDocumentsRunController(shutdown.Token);

		CancellationToken token = controller.Begin();

		Assert.False(token.IsCancellationRequested);
		Assert.True(controller.IsRunning);
	}

	[Fact]
	public void ShutdownCancelsTheRun()
	{
		using var shutdown = new CancellationTokenSource();
		using var controller = new OcrDocumentsRunController(shutdown.Token);
		CancellationToken token = controller.Begin();

		shutdown.Cancel();

		Assert.True(token.IsCancellationRequested);
	}

	[Fact]
	public void CancelStopsTheRunWithoutTouchingShutdown()
	{
		using var shutdown = new CancellationTokenSource();
		using var controller = new OcrDocumentsRunController(shutdown.Token);
		CancellationToken token = controller.Begin();

		Assert.True(controller.Cancel());

		Assert.True(token.IsCancellationRequested);
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

	[Fact]
	public void CancelReportsNothingToCancel_AfterTheRunEnded()
	{
		using var shutdown = new CancellationTokenSource();
		using var controller = new OcrDocumentsRunController(shutdown.Token);
		controller.Begin();
		controller.End();

		Assert.False(controller.Cancel());
		Assert.False(controller.IsRunning);
	}

	[Fact]
	public void EndIsSafeWhenNoRunIsInFlight()
	{
		using var shutdown = new CancellationTokenSource();
		using var controller = new OcrDocumentsRunController(shutdown.Token);

		controller.End();
		controller.End();

		Assert.False(controller.IsRunning);
	}

	// A second batch must not inherit the first one's cancellation, or the run would end
	// the moment it started and the user would be told it was cancelled again.
	[Fact]
	public void ASecondRunStartsClean_AfterTheFirstWasCancelled()
	{
		using var shutdown = new CancellationTokenSource();
		using var controller = new OcrDocumentsRunController(shutdown.Token);
		CancellationToken first = controller.Begin();
		controller.Cancel();
		controller.End();

		CancellationToken second = controller.Begin();

		Assert.True(first.IsCancellationRequested);
		Assert.False(second.IsCancellationRequested);
	}

	[Fact]
	public void BeginThrows_WhenARunIsAlreadyInFlight()
	{
		using var shutdown = new CancellationTokenSource();
		using var controller = new OcrDocumentsRunController(shutdown.Token);
		controller.Begin();

		Assert.Throws<InvalidOperationException>(() => controller.Begin());
	}

	[Fact]
	public void DisposeCancelsNothingButReleasesTheRun()
	{
		using var shutdown = new CancellationTokenSource();
		var controller = new OcrDocumentsRunController(shutdown.Token);
		controller.Begin();

		controller.Dispose();

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

	// Shutdown already fired before the user got to the picker: the run must come back
	// cancelled rather than starting a batch nothing will ever stop.
	[Fact]
	public void ARunBegunAfterShutdownIsAlreadyCancelled()
	{
		using var shutdown = new CancellationTokenSource();
		shutdown.Cancel();
		using var controller = new OcrDocumentsRunController(shutdown.Token);

		CancellationToken token = controller.Begin();

		Assert.True(token.IsCancellationRequested);
	}
}
