using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Mutation.Ui.Core;
using Xunit;

namespace Mutation.Tests;

public class MicrophoneLevelWriteCoordinatorTests
{
	// Stands in for a device with no software level control, for the tests that only
	// exercise the write side and never look at the probe's result.
	private static readonly CaptureLevelState Unsupported = new(IsSupported: false, Level: null);

	[Fact]
	public void Constructor_NullWrite_Throws()
	{
		Assert.Throws<ArgumentNullException>(() => new MicrophoneLevelWriteCoordinator(null!, () => Unsupported));
	}

	[Fact]
	public void Constructor_NullRead_Throws()
	{
		Assert.Throws<ArgumentNullException>(() => new MicrophoneLevelWriteCoordinator(
			_ => new CaptureLevelResult(CaptureLevelOutcome.Applied), null!));
	}

	[Fact]
	public async Task RequestLatestAsync_ReturnsTheWriteResult()
	{
		var coordinator = new MicrophoneLevelWriteCoordinator(
			_ => new CaptureLevelResult(CaptureLevelOutcome.Failed), () => Unsupported);

		var result = await coordinator.RequestLatestAsync(30);

		Assert.Equal(new CaptureLevelResult(CaptureLevelOutcome.Failed), result);
	}

	[Fact]
	public async Task RequestLatestAsync_RunsTheWriteOffTheCallingThread()
	{
		// The write signals it started, then blocks. If it ran synchronously on this
		// thread, RequestLatestAsync would block inside it and never hand back a task —
		// so reaching the asserts below, with "started" already signalled, proves the
		// write runs on another thread.
		var started = new ManualResetEventSlim(false);
		var release = new ManualResetEventSlim(false);
		var coordinator = new MicrophoneLevelWriteCoordinator(_ =>
		{
			started.Set();
			release.Wait();
			return new CaptureLevelResult(CaptureLevelOutcome.Applied);
		}, () => Unsupported);

		var task = coordinator.RequestLatestAsync(50);

		Assert.True(started.Wait(TimeSpan.FromSeconds(5)), "write did not start on a background thread");
		Assert.False(task.IsCompleted);
		Assert.True(coordinator.IsWriting);

		release.Set();
		var result = await task;

		Assert.Equal(new CaptureLevelResult(CaptureLevelOutcome.Applied), result);
		AssertEventually(() => !coordinator.IsWriting, "worker did not settle after the write");
	}

	[Fact]
	public async Task RequestLatestAsync_CoalescesToLatest_AppliesFirstAndLast_DropsMiddle()
	{
		var started = new ManualResetEventSlim(false);
		var release = new ManualResetEventSlim(false);
		var applied = new List<int>();
		bool firstCall = true;

		CaptureLevelResult Write(int level)
		{
			bool isFirst;
			lock (applied)
			{
				isFirst = firstCall;
				firstCall = false;
				applied.Add(level);
			}

			// Block only the first write, so the burst below queues behind it.
			if (isFirst)
			{
				started.Set();
				release.Wait();
			}

			return new CaptureLevelResult(CaptureLevelOutcome.Applied);
		}

		var coordinator = new MicrophoneLevelWriteCoordinator(Write, () => Unsupported);

		var first = coordinator.RequestLatestAsync(10);
		Assert.True(started.Wait(TimeSpan.FromSeconds(5)), "first write did not start");

		// While the first write is blocked, three more requests queue. Each supersedes
		// the previous still-queued one, so only the last survives to be written.
		var superseded1 = coordinator.RequestLatestAsync(20);
		var superseded2 = coordinator.RequestLatestAsync(30);
		var latest = coordinator.RequestLatestAsync(40);

		// The two middle requests were dropped before they ran.
		Assert.Null(await superseded1);
		Assert.Null(await superseded2);

		release.Set();

		Assert.NotNull(await first);
		Assert.NotNull(await latest);

		lock (applied)
		{
			// First and last reach the device, in order; the middle values never do.
			Assert.Equal(new[] { 10, 40 }, applied);
		}

		AssertEventually(() => !coordinator.IsWriting, "worker did not settle");
	}

	[Fact]
	public async Task RequestLatestAsync_TreatsAThrowingWriteAsFailed()
	{
		var coordinator = new MicrophoneLevelWriteCoordinator(
			_ => throw new InvalidOperationException("device fault"), () => Unsupported);

		var result = await coordinator.RequestLatestAsync(10);

		Assert.Equal(new CaptureLevelResult(CaptureLevelOutcome.Failed), result);
		AssertEventually(() => !coordinator.IsWriting, "worker did not settle after a throw");
	}

	[Fact]
	public async Task RequestLatestAsync_AllowsANewWriteAfterThePreviousCompletes()
	{
		var applied = new List<int>();
		var coordinator = new MicrophoneLevelWriteCoordinator(level =>
		{
			lock (applied) applied.Add(level);
			return new CaptureLevelResult(CaptureLevelOutcome.Applied);
		}, () => Unsupported);

		Assert.NotNull(await coordinator.RequestLatestAsync(10));
		Assert.NotNull(await coordinator.RequestLatestAsync(20));

		lock (applied) Assert.Equal(new[] { 10, 20 }, applied);
		AssertEventually(() => !coordinator.IsWriting, "worker did not settle");
	}

	// ----- Level probes (issues #224, #263) -----

	[Fact]
	public async Task ReadLevelStateAsync_ReturnsSupportAndLevelTogether()
	{
		var coordinator = new MicrophoneLevelWriteCoordinator(
			_ => new CaptureLevelResult(CaptureLevelOutcome.Applied),
			() => new CaptureLevelState(IsSupported: true, Level: 35));

		Assert.Equal(new CaptureLevelState(true, 35), await coordinator.ReadLevelStateAsync());
	}

	[Fact]
	public async Task ReadLevelStateAsync_ProbesTheDeviceExactlyOnce()
	{
		// The UI needs both facts to set its controls up; asking separately would double
		// the exposure to a stalled device and could straddle a device change.
		int probes = 0;
		var coordinator = new MicrophoneLevelWriteCoordinator(
			_ => new CaptureLevelResult(CaptureLevelOutcome.Applied),
			() =>
			{
				Interlocked.Increment(ref probes);
				return new CaptureLevelState(IsSupported: true, Level: 35);
			});

		await coordinator.ReadLevelStateAsync();

		Assert.Equal(1, probes);
	}

	[Fact]
	public async Task ReadLevelStateAsync_TreatsAThrowingProbeAsUnsupported()
	{
		// Leaves the controls disabled rather than pretending a level.
		var coordinator = new MicrophoneLevelWriteCoordinator(
			_ => new CaptureLevelResult(CaptureLevelOutcome.Applied),
			() => throw new InvalidOperationException("stale proxy"));

		var state = await coordinator.ReadLevelStateAsync();

		Assert.False(state.IsSupported);
		Assert.Null(state.Level);
	}

	[Fact]
	public async Task ReadLevelStateAsync_RunsTheProbeOffTheCallingThread()
	{
		// The mic-change path calls this from the UI thread; a probe that ran inline
		// would freeze the window, and the screen reader with it (issue #263).
		var started = new ManualResetEventSlim(false);
		var release = new ManualResetEventSlim(false);
		var coordinator = new MicrophoneLevelWriteCoordinator(
			_ => new CaptureLevelResult(CaptureLevelOutcome.Applied),
			() =>
			{
				started.Set();
				release.Wait();
				return new CaptureLevelState(IsSupported: true, Level: 20);
			});

		var task = coordinator.ReadLevelStateAsync();

		Assert.True(started.Wait(TimeSpan.FromSeconds(5)), "probe did not start on a background thread");
		Assert.False(task.IsCompleted);

		release.Set();
		Assert.Equal(new CaptureLevelState(true, 20), await task);
	}

	[Fact]
	public async Task ReadCurrentLevelAsync_ReturnsTheReadValue()
	{
		var coordinator = new MicrophoneLevelWriteCoordinator(
			_ => new CaptureLevelResult(CaptureLevelOutcome.Applied), () => new CaptureLevelState(true, 42));

		Assert.Equal(42, await coordinator.ReadCurrentLevelAsync());
	}

	[Fact]
	public async Task ReadCurrentLevelAsync_TreatsAThrowingReadAsUnknown()
	{
		var coordinator = new MicrophoneLevelWriteCoordinator(
			_ => new CaptureLevelResult(CaptureLevelOutcome.Applied),
			() => throw new InvalidOperationException("stale proxy"));

		Assert.Null(await coordinator.ReadCurrentLevelAsync());
	}

	[Fact]
	public async Task ReadCurrentLevelAsync_RunsTheReadOffTheCallingThread()
	{
		// The read signals it started, then blocks. If it ran inline on this thread,
		// ReadCurrentLevelAsync would never hand back a task — so reaching the asserts
		// with "started" already signalled proves the read runs elsewhere. This is the
		// property that keeps a stalled device from freezing the UI on activation.
		var started = new ManualResetEventSlim(false);
		var release = new ManualResetEventSlim(false);
		var coordinator = new MicrophoneLevelWriteCoordinator(
			_ => new CaptureLevelResult(CaptureLevelOutcome.Applied),
			() =>
			{
				started.Set();
				release.Wait();
				return new CaptureLevelState(true, 60);
			});

		var task = coordinator.ReadCurrentLevelAsync();

		Assert.True(started.Wait(TimeSpan.FromSeconds(5)), "read did not start on a background thread");
		Assert.False(task.IsCompleted);

		release.Set();
		Assert.Equal(60, await task);
	}

	[Fact]
	public async Task ReadCurrentLevelAsync_DoesNotOverlapAnInFlightWrite()
	{
		// Both touch the same COM endpoint, so the coordinator must keep them apart.
		var writeStarted = new ManualResetEventSlim(false);
		var releaseWrite = new ManualResetEventSlim(false);
		bool writing = false;
		bool overlapped = false;

		var coordinator = new MicrophoneLevelWriteCoordinator(
			_ =>
			{
				writing = true;
				writeStarted.Set();
				releaseWrite.Wait();
				writing = false;
				return new CaptureLevelResult(CaptureLevelOutcome.Applied);
			},
			() =>
			{
				if (writing)
					overlapped = true;
				return new CaptureLevelState(true, 15);
			});

		var write = coordinator.RequestLatestAsync(80);
		Assert.True(writeStarted.Wait(TimeSpan.FromSeconds(5)), "write did not start");

		var read = coordinator.ReadCurrentLevelAsync();
		// The read is queued behind the write rather than running against the endpoint
		// while it is being written.
		var first = await Task.WhenAny(read, Task.Delay(TimeSpan.FromMilliseconds(200)));
		Assert.NotSame(read, first);

		releaseWrite.Set();

		Assert.NotNull(await write);
		Assert.Equal(15, await read);
		Assert.False(overlapped, "the read observed an in-flight write");
	}

	// Spins briefly for a condition the worker satisfies just after an awaited task
	// completes (it clears its running flag on the loop pass after signalling the
	// result), avoiding a race with a bare assertion.
	private static void AssertEventually(Func<bool> condition, string message)
	{
		var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(5);
		while (DateTime.UtcNow < deadline)
		{
			if (condition())
				return;
			Thread.Sleep(10);
		}

		Assert.True(condition(), message);
	}
}
