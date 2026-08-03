using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Mutation.Ui.Core;
using Xunit;

namespace Mutation.Tests;

public class ApplicationCloseSequenceTests
{
	private static ApplicationCloseSequence Create(
		Action? persist = null,
		Func<Task>? stopAudio = null,
		Action? release = null,
		Action<string, Exception>? onStepFailed = null) =>
		new(
			persist ?? (() => { }),
			stopAudio ?? (() => Task.CompletedTask),
			release ?? (() => { }),
			onStepFailed);

	[Theory]
	[InlineData("persist")]
	[InlineData("stop")]
	[InlineData("release")]
	public void Constructor_NullStep_Throws(string missing)
	{
		Action? persist = missing == "persist" ? null : () => { };
		Func<Task>? stop = missing == "stop" ? null : () => Task.CompletedTask;
		Action? release = missing == "release" ? null : () => { };

		Assert.Throws<ArgumentNullException>(() =>
			new ApplicationCloseSequence(persist!, stop!, release!));
	}

	[Fact]
	public async Task RunAsync_RunsStepsInOrder()
	{
		var order = new List<string>();
		var sequence = Create(
			persist: () => order.Add("persist"),
			stopAudio: () => { order.Add("stop"); return Task.CompletedTask; },
			release: () => order.Add("release"));

		await sequence.RunAsync();

		Assert.Equal(new[] { "persist", "stop", "release" }, order);
	}

	// The bug this class exists to prevent (issue #223): closing the window while
	// recording made stopping the recorder yield, and the app-level Closed handler then
	// terminated the process with Environment.Exit before the settings and window state
	// were written. Persistence must therefore be complete before the first suspension
	// point, not merely ordered before the save in source.
	[Fact]
	public async Task RunAsync_PersistsBeforeTheStopCanYield()
	{
		bool persisted = false;
		var stopCompletion = new TaskCompletionSource();
		var sequence = Create(
			persist: () => persisted = true,
			stopAudio: () => stopCompletion.Task);

		var run = sequence.RunAsync();

		// The recorder is still stopping — exactly the moment the app-level handler
		// would tear the process down — and the state is already on disk.
		Assert.False(run.IsCompleted);
		Assert.True(persisted, "state was not persisted before the sequence yielded");

		stopCompletion.SetResult();
		await run;
	}

	[Fact]
	public async Task RunAsync_ReleasesResourcesWhenStoppingTheAudioThrows()
	{
		bool released = false;
		var sequence = Create(
			stopAudio: () => throw new InvalidOperationException("recorder wedged"),
			release: () => released = true);

		await sequence.RunAsync();

		Assert.True(released, "resources were not released after a failed stop");
	}

	[Fact]
	public async Task RunAsync_ReleasesResourcesWhenTheStopTaskFaults()
	{
		bool released = false;
		var sequence = Create(
			stopAudio: () => Task.FromException(new InvalidOperationException("recorder wedged")),
			release: () => released = true);

		await sequence.RunAsync();

		Assert.True(released, "resources were not released after a faulted stop");
	}

	[Fact]
	public async Task RunAsync_ContinuesWhenPersistenceThrows()
	{
		var order = new List<string>();
		var sequence = Create(
			persist: () => throw new InvalidOperationException("disk full"),
			stopAudio: () => { order.Add("stop"); return Task.CompletedTask; },
			release: () => order.Add("release"));

		await sequence.RunAsync();

		Assert.Equal(new[] { "stop", "release" }, order);
	}

	[Fact]
	public async Task RunAsync_ReportsFailingSteps()
	{
		var failures = new List<string>();
		var sequence = Create(
			persist: () => throw new InvalidOperationException("disk full"),
			stopAudio: () => throw new InvalidOperationException("recorder wedged"),
			release: () => throw new InvalidOperationException("already disposed"),
			onStepFailed: (step, _) => failures.Add(step));

		await sequence.RunAsync();

		Assert.Equal(new[] { "persist state", "stop audio", "release resources" }, failures);
	}

	[Fact]
	public async Task Completion_SignalsOnlyAfterTheReleaseStep()
	{
		bool released = false;
		var stopCompletion = new TaskCompletionSource();
		var sequence = Create(
			stopAudio: () => stopCompletion.Task,
			release: () => released = true);

		var run = sequence.RunAsync();
		Assert.False(sequence.Completion.IsCompleted);

		stopCompletion.SetResult();
		await run;
		await sequence.Completion;

		Assert.True(released);
	}

	[Fact]
	public async Task Completion_DoesNotFaultWhenEveryStepFails()
	{
		var sequence = Create(
			persist: () => throw new InvalidOperationException("disk full"),
			stopAudio: () => throw new InvalidOperationException("recorder wedged"),
			release: () => throw new InvalidOperationException("already disposed"));

		await sequence.RunAsync();

		// The app-level handler awaits this unguarded, so it must never fault.
		await sequence.Completion;
	}
}
