using System;
using System.Threading;
using System.Threading.Tasks;
using Mutation.Ui.Core;
using Xunit;

namespace Mutation.Tests;

public class MicrophoneMuteToggleCoordinatorTests
{
	[Fact]
	public void Constructor_NullToggle_Throws()
	{
		Assert.Throws<ArgumentNullException>(() => new MicrophoneMuteToggleCoordinator(null!));
	}

	[Fact]
	public async Task ToggleAsync_ReturnsToggleResult()
	{
		var expected = new MuteToggleResult(Success: true, IsMuted: true);
		var coordinator = new MicrophoneMuteToggleCoordinator(() => expected);

		var result = await coordinator.ToggleAsync();

		Assert.Equal(expected, result);
		Assert.False(coordinator.IsToggling);
	}

	[Fact]
	public async Task ToggleAsync_RunsToggleOffTheCallingThread()
	{
		// The delegate signals when it starts and then blocks. If the toggle ran
		// synchronously on this thread, ToggleAsync would block inside the delegate
		// and never hand back a task — so simply reaching the asserts below, with
		// "started" already signalled, proves the work runs on another thread.
		var started = new ManualResetEventSlim(false);
		var release = new ManualResetEventSlim(false);
		var coordinator = new MicrophoneMuteToggleCoordinator(() =>
		{
			started.Set();
			release.Wait();
			return new MuteToggleResult(Success: true, IsMuted: true);
		});

		var toggle = coordinator.ToggleAsync();

		Assert.True(started.Wait(TimeSpan.FromSeconds(5)), "toggle did not start on a background thread");
		Assert.False(toggle.IsCompleted);
		Assert.True(coordinator.IsToggling);

		release.Set();
		var result = await toggle;

		Assert.Equal(new MuteToggleResult(Success: true, IsMuted: true), result);
		Assert.False(coordinator.IsToggling);
	}

	[Fact]
	public async Task ToggleAsync_CoalescesOverlappingRequests()
	{
		var release = new ManualResetEventSlim(false);
		int calls = 0;
		var coordinator = new MicrophoneMuteToggleCoordinator(() =>
		{
			Interlocked.Increment(ref calls);
			release.Wait();
			return new MuteToggleResult(Success: true, IsMuted: true);
		});

		// The first call claims the single in-flight slot synchronously (before
		// awaiting), so the overlapping second call is dropped, not queued.
		var first = coordinator.ToggleAsync();
		var second = await coordinator.ToggleAsync();

		Assert.Null(second);

		release.Set();
		var firstResult = await first;

		Assert.NotNull(firstResult);
		Assert.Equal(1, calls);
		Assert.False(coordinator.IsToggling);
	}

	[Fact]
	public async Task ToggleAsync_AllowsANewToggleAfterThePreviousCompletes()
	{
		int calls = 0;
		var coordinator = new MicrophoneMuteToggleCoordinator(() =>
		{
			Interlocked.Increment(ref calls);
			return new MuteToggleResult(Success: true, IsMuted: calls % 2 == 1);
		});

		var first = await coordinator.ToggleAsync();
		var second = await coordinator.ToggleAsync();

		Assert.NotNull(first);
		Assert.NotNull(second);
		Assert.Equal(2, calls);
	}
}
