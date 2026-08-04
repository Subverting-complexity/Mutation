using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Mutation.Ui.Core;
using Xunit;

namespace Mutation.Tests;

public class ContentReadyGateTests
{
	private static readonly TimeSpan Poll = TimeSpan.FromMilliseconds(50);
	private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(5);

	// Records what it was asked to wait for instead of actually waiting, so the polling
	// behaviour is exercised with no real time passing.
	private sealed class RecordingDelay
	{
		public List<TimeSpan> Delays { get; } = new();

		public Func<TimeSpan, Task> Func => interval =>
		{
			Delays.Add(interval);
			return Task.CompletedTask;
		};
	}

	[Fact]
	public async Task ContentAlreadyReady_ReturnsImmediatelyWithoutWaiting()
	{
		var delay = new RecordingDelay();

		bool ready = await ContentReadyGate.WaitAsync(() => true, delay.Func, Poll, Timeout);

		Assert.True(ready);
		Assert.Empty(delay.Delays);
	}

	[Fact]
	public async Task ContentReadyAfterAFewTurns_ReturnsReadyAndStopsPolling()
	{
		var delay = new RecordingDelay();
		int checks = 0;

		bool ready = await ContentReadyGate.WaitAsync(() => ++checks > 3, delay.Func, Poll, Timeout);

		Assert.True(ready);
		// Three "not ready" checks, so three waits, then the fourth check succeeds.
		Assert.Equal(3, delay.Delays.Count);
		Assert.All(delay.Delays, d => Assert.Equal(Poll, d));
	}

	// A window that never loads must not wedge startup: the caller has a degraded path
	// (a system message box) and has to be allowed to reach it.
	[Fact]
	public async Task ContentNeverReady_GivesUpAtTheTimeoutInsteadOfHanging()
	{
		var delay = new RecordingDelay();

		bool ready = await ContentReadyGate.WaitAsync(
			() => false,
			delay.Func,
			TimeSpan.FromMilliseconds(100),
			TimeSpan.FromMilliseconds(500));

		Assert.False(ready);
		Assert.Equal(5, delay.Delays.Count);
	}

	[Fact]
	public async Task ZeroTimeout_ChecksOnceAndGivesUp()
	{
		var delay = new RecordingDelay();
		int checks = 0;

		bool ready = await ContentReadyGate.WaitAsync(
			() => { checks++; return false; },
			delay.Func,
			Poll,
			TimeSpan.Zero);

		Assert.False(ready);
		Assert.Equal(1, checks);
		Assert.Empty(delay.Delays);
	}

	// The last poll lands exactly on the timeout; content that became ready during that
	// final wait is still reported as ready rather than being reported as a timeout.
	[Fact]
	public async Task ContentReadyOnTheFinalCheck_IsReportedAsReady()
	{
		var delay = new RecordingDelay();
		int checks = 0;

		bool ready = await ContentReadyGate.WaitAsync(
			() => ++checks > 2,
			delay.Func,
			TimeSpan.FromMilliseconds(100),
			TimeSpan.FromMilliseconds(200));

		Assert.True(ready);
		Assert.Equal(2, delay.Delays.Count);
	}

	[Fact]
	public async Task NonPositivePollInterval_IsRejected()
	{
		await Assert.ThrowsAsync<ArgumentOutOfRangeException>(
			() => ContentReadyGate.WaitAsync(() => false, _ => Task.CompletedTask, TimeSpan.Zero, Timeout));
	}

	[Fact]
	public async Task MissingCallbacks_AreRejected()
	{
		await Assert.ThrowsAsync<ArgumentNullException>(
			() => ContentReadyGate.WaitAsync(null!, _ => Task.CompletedTask, Poll, Timeout));
		await Assert.ThrowsAsync<ArgumentNullException>(
			() => ContentReadyGate.WaitAsync(() => false, null!, Poll, Timeout));
	}
}
