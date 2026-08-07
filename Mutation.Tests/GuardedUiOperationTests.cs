using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Mutation.Ui.Core;

namespace Mutation.Tests;

// Covers issue #234: FinalizeTranscript restored the transcript box and the auto-action
// suppression in its last two statements, after three awaits and two beeps, with no
// try/finally. Any throw on the delivery path skipped both — and because the method is
// async void the exception went to the global handler, so the user simply found that
// the transcript box no longer accepted typing, for the rest of the session, with
// nothing said about why.
public class GuardedUiOperationTests
{
	[Fact]
	public async Task Restores_WhenTheWorkSucceeds()
	{
		bool restored = false;

		await GuardedUiOperation.RunAsync(
			work: () => Task.CompletedTask,
			onFailure: _ => Assert.Fail("The work succeeded; nothing to report."),
			restore: () => restored = true);

		Assert.True(restored);
	}

	[Fact]
	public async Task Restores_WhenTheWorkThrows()
	{
		bool restored = false;

		await GuardedUiOperation.RunAsync(
			work: () => throw new InvalidOperationException("beep player is gone"),
			onFailure: _ => { },
			restore: () => restored = true);

		Assert.True(restored);
	}

	// The throw can land after an await, on the continuation rather than synchronously.
	[Fact]
	public async Task Restores_WhenTheWorkThrowsAfterAnAwait()
	{
		bool restored = false;

		await GuardedUiOperation.RunAsync(
			work: async () =>
			{
				await Task.Yield();
				throw new InvalidOperationException("automation peer failed inside ShowStatus");
			},
			onFailure: _ => { },
			restore: () => restored = true);

		Assert.True(restored);
	}

	// A failure the user is never told about is the thing that made #234 baffling
	// rather than merely annoying.
	[Fact]
	public async Task ReportsTheFailure_BeforeRestoring()
	{
		var order = new List<string>();
		var thrown = new InvalidOperationException("delivery failed");
		Exception? reported = null;

		await GuardedUiOperation.RunAsync(
			work: () => throw thrown,
			onFailure: ex => { reported = ex; order.Add("report"); },
			restore: () => order.Add("restore"));

		Assert.Same(thrown, reported);
		Assert.Equal(new[] { "report", "restore" }, order);
	}

	// The reporting path is beeps and automation peers — exactly what was failing in the
	// first place. It must not be able to take the restoration down with it.
	[Fact]
	public async Task Restores_WhenTheFailureReportItselfThrows()
	{
		bool restored = false;
		Exception? secondary = null;

		await GuardedUiOperation.RunAsync(
			work: () => throw new InvalidOperationException("delivery failed"),
			onFailure: _ => throw new InvalidOperationException("no audio device for the failure beep"),
			restore: () => restored = true,
			onReportFailed: ex => secondary = ex);

		Assert.True(restored);
		Assert.NotNull(secondary);
		Assert.Equal("no audio device for the failure beep", secondary!.Message);
	}

	// The whole point is that the async void caller never sees an exception, so the
	// global handler is never what tells the user something went wrong.
	[Fact]
	public async Task DoesNotPropagateTheFailure()
	{
		var run = GuardedUiOperation.RunAsync(
			work: () => throw new InvalidOperationException("delivery failed"),
			onFailure: _ => { },
			restore: () => { });

		await run;

		Assert.True(run.IsCompletedSuccessfully);
	}

	[Fact]
	public async Task RestoresExactlyOnce()
	{
		int restores = 0;

		await GuardedUiOperation.RunAsync(
			work: () => throw new InvalidOperationException("delivery failed"),
			onFailure: _ => { },
			restore: () => restores++);

		Assert.Equal(1, restores);
	}
}
