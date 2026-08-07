using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;
using Mutation.Ui.Core;
using Xunit;

namespace Mutation.Tests;

public class MicrophoneSwitchCoordinatorTests
{
	private static MicrophoneSwitchCoordinator Create(
		Func<string, bool>? selectDevice = null,
		Action? restartCapture = null,
		Action? stopCapture = null,
		Action<Exception>? onError = null) =>
		new(
			selectDevice ?? (_ => true),
			restartCapture ?? (() => { }),
			stopCapture ?? (() => { }),
			onError);

	[Fact]
	public void Constructor_NullDelegates_Throw()
	{
		Assert.Throws<ArgumentNullException>(() => new MicrophoneSwitchCoordinator(null!, () => { }, () => { }));
		Assert.Throws<ArgumentNullException>(() => new MicrophoneSwitchCoordinator(_ => true, null!, () => { }));
		Assert.Throws<ArgumentNullException>(() => new MicrophoneSwitchCoordinator(_ => true, () => { }, null!));
	}

	[Theory]
	[InlineData("")]
	[InlineData(null)]
	public void SwitchAsync_WithoutADeviceId_Throws(string? deviceId)
	{
		var coordinator = Create();

		// Thrown synchronously, not handed back on a faulted task: a caller with no
		// device ID has a bug, not a device problem.
		Assert.Throws<ArgumentException>(() => { _ = coordinator.SwitchAsync(deviceId!); });
	}

	[Fact]
	public async Task SwitchAsync_SelectsTheDeviceThenRestartsCapture()
	{
		var steps = new ConcurrentQueue<string>();
		var coordinator = Create(
			selectDevice: id => { steps.Enqueue($"select:{id}"); return true; },
			restartCapture: () => steps.Enqueue("restart"));

		var result = await coordinator.SwitchAsync("mic-a");

		Assert.Equal(MicrophoneSwitchOutcome.Switched, result!.Value.Outcome);
		Assert.True(result.Value.Switched);
		Assert.Null(result.Value.FailureMessage);
		Assert.Equal(new[] { "select:mic-a", "restart" }, steps);
		Assert.False(coordinator.IsSwitching);
	}

	[Fact]
	public async Task SwitchAsync_RunsTheDeviceWorkOffTheCallingThread()
	{
		// The delegate signals when it starts and then blocks. If the switch ran
		// synchronously on this thread, SwitchAsync would block inside the delegate and
		// never hand back a task — so simply reaching the asserts below, with "started"
		// already signalled, proves the work runs on another thread. That is the whole
		// point of the type: a wedged device must not be able to freeze the window.
		var started = new ManualResetEventSlim(false);
		var release = new ManualResetEventSlim(false);
		var coordinator = Create(selectDevice: _ =>
		{
			started.Set();
			release.Wait();
			return true;
		});

		var pending = coordinator.SwitchAsync("mic-a");

		Assert.True(started.Wait(TimeSpan.FromSeconds(5)), "the switch did not start on a background thread");
		Assert.False(pending.IsCompleted);
		Assert.True(coordinator.IsSwitching);

		release.Set();

		Assert.Equal(MicrophoneSwitchOutcome.Switched, (await pending)!.Value.Outcome);
	}

	[Fact]
	public async Task SwitchAsync_DeviceGone_ReportsUnavailableAndLeavesCaptureAlone()
	{
		int restarts = 0;
		var coordinator = Create(
			selectDevice: _ => false,
			restartCapture: () => Interlocked.Increment(ref restarts));

		var result = await coordinator.SwitchAsync("mic-gone");

		Assert.Equal(MicrophoneSwitchOutcome.Unavailable, result!.Value.Outcome);
		Assert.False(result.Value.Switched);
		Assert.Equal(0, restarts);
	}

	[Fact]
	public async Task SwitchAsync_DeviceFaults_ReportsTheFailureAndHandsTheExceptionToTheLogger()
	{
		Exception? logged = null;
		var coordinator = Create(
			selectDevice: _ => throw new InvalidOperationException("driver is wedged"),
			onError: ex => logged = ex);

		var result = await coordinator.SwitchAsync("mic-a");

		Assert.Equal(MicrophoneSwitchOutcome.Failed, result!.Value.Outcome);
		Assert.Equal("driver is wedged", result.Value.FailureMessage);
		Assert.IsType<InvalidOperationException>(logged);
	}

	[Fact]
	public async Task SwitchAsync_CaptureRestartFaults_ReportsTheFailure()
	{
		var coordinator = Create(restartCapture: () => throw new InvalidOperationException("waveInOpen failed"));

		var result = await coordinator.SwitchAsync("mic-a");

		Assert.Equal(MicrophoneSwitchOutcome.Failed, result!.Value.Outcome);
		Assert.Equal("waveInOpen failed", result.Value.FailureMessage);
	}

	[Fact]
	public async Task SwitchAsync_AThrowingErrorReporterStillLetsTheOutcomeThrough()
	{
		var coordinator = Create(
			selectDevice: _ => throw new InvalidOperationException("driver is wedged"),
			onError: _ => throw new InvalidOperationException("the log is also broken"));

		var result = await coordinator.SwitchAsync("mic-a");

		Assert.Equal(MicrophoneSwitchOutcome.Failed, result!.Value.Outcome);
	}

	[Fact]
	public async Task SwitchAsync_QueuedRequestIsSupersededByANewerOne()
	{
		var started = new ManualResetEventSlim(false);
		var release = new ManualResetEventSlim(false);
		var selected = new ConcurrentQueue<string>();
		var coordinator = Create(selectDevice: id =>
		{
			selected.Enqueue(id);
			// Only the first switch blocks; the rest run straight through.
			if (id == "mic-a")
			{
				started.Set();
				release.Wait();
			}
			return true;
		});

		var first = coordinator.SwitchAsync("mic-a");
		Assert.True(started.Wait(TimeSpan.FromSeconds(5)), "the first switch did not start");

		// Both queue behind the running first one, so the middle request never reaches
		// the device at all — the user's final choice is the only one applied.
		var second = coordinator.SwitchAsync("mic-b");
		var third = coordinator.SwitchAsync("mic-c");

		Assert.Null(await second);

		release.Set();

		// The first switch had already started, so it did touch the device — but a
		// newer choice landed while it ran, so it reports nothing rather than letting
		// the UI settle its controls on a device the user has moved on from.
		Assert.Null(await first);
		Assert.Equal(MicrophoneSwitchOutcome.Switched, (await third)!.Value.Outcome);
		Assert.Equal(new[] { "mic-a", "mic-c" }, selected);
		Assert.False(coordinator.IsSwitching);
	}

	[Fact]
	public async Task SwitchAsync_LastRequestOfABurstIsTheOneApplied()
	{
		var started = new ManualResetEventSlim(false);
		var release = new ManualResetEventSlim(false);
		string? lastSelected = null;
		var coordinator = Create(selectDevice: id =>
		{
			if (id == "mic-a")
			{
				started.Set();
				release.Wait();
			}
			lastSelected = id;
			return true;
		});

		var first = coordinator.SwitchAsync("mic-a");
		Assert.True(started.Wait(TimeSpan.FromSeconds(5)), "the first switch did not start");

		for (int i = 0; i < 5; i++)
			_ = coordinator.SwitchAsync($"mic-{i}");
		var last = coordinator.SwitchAsync("mic-final");

		release.Set();
		await first;

		Assert.Equal(MicrophoneSwitchOutcome.Switched, (await last)!.Value.Outcome);
		Assert.Equal("mic-final", lastSelected);
	}

	[Fact]
	public async Task ReleaseAsync_StopsCaptureWithoutSelectingADevice()
	{
		int selects = 0;
		int stops = 0;
		var coordinator = Create(
			selectDevice: _ => { Interlocked.Increment(ref selects); return true; },
			stopCapture: () => Interlocked.Increment(ref stops));

		var result = await coordinator.ReleaseAsync();

		Assert.Equal(MicrophoneSwitchOutcome.Switched, result!.Value.Outcome);
		Assert.Equal(0, selects);
		Assert.Equal(1, stops);
	}

	[Fact]
	public async Task ReleaseAsync_AndSwitchAsync_ShareOneWorkerSoTheyCannotOvertakeEachOther()
	{
		var started = new ManualResetEventSlim(false);
		var release = new ManualResetEventSlim(false);
		var steps = new ConcurrentQueue<string>();
		var coordinator = Create(
			selectDevice: id =>
			{
				steps.Enqueue($"select:{id}");
				if (id == "mic-a")
				{
					started.Set();
					release.Wait();
				}
				return true;
			},
			restartCapture: () => steps.Enqueue("restart"),
			stopCapture: () => steps.Enqueue("stop"));

		var first = coordinator.SwitchAsync("mic-a");
		Assert.True(started.Wait(TimeSpan.FromSeconds(5)), "the first switch did not start");

		var stop = coordinator.ReleaseAsync();

		release.Set();
		await first;
		await stop;

		Assert.Equal(new[] { "select:mic-a", "restart", "stop" }, steps);
	}

	[Fact]
	public async Task SwitchAsync_AfterThePreviousOneSettles_RunsAgain()
	{
		var selected = new ConcurrentQueue<string>();
		var coordinator = Create(selectDevice: id => { selected.Enqueue(id); return true; });

		var first = await coordinator.SwitchAsync("mic-a");
		var second = await coordinator.SwitchAsync("mic-b");

		Assert.Equal(MicrophoneSwitchOutcome.Switched, first!.Value.Outcome);
		Assert.Equal(MicrophoneSwitchOutcome.Switched, second!.Value.Outcome);
		Assert.Equal(new[] { "mic-a", "mic-b" }, selected);
	}
}
