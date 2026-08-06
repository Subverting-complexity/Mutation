using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Mutation.Ui.Core;

namespace Mutation.Tests;

public class DebouncerTests
{
	// A delay stand-in the test drives by hand: each call records a pending "gate"
	// the test completes explicitly (as if the time elapsed), so the coalescing and
	// cancellation behaviour is exercised deterministically with no real time passing.
	private sealed class ManualDelay
	{
		private readonly List<(TaskCompletionSource Tcs, CancellationToken Token)> _pending = new();
		private readonly object _lock = new();

		public Func<TimeSpan, CancellationToken, Task> Func => (_, token) =>
		{
			var tcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
			lock (_lock)
				_pending.Add((tcs, token));
			token.Register(() => tcs.TrySetCanceled(token));
			return tcs.Task;
		};

		public int Count
		{
			get { lock (_lock) return _pending.Count; }
		}

		// Complete the most recently scheduled delay as if its quiet period elapsed.
		public void CompleteLast()
		{
			TaskCompletionSource tcs;
			lock (_lock)
				tcs = _pending[_pending.Count - 1].Tcs;
			tcs.TrySetResult();
		}
	}

	[Fact]
	public void Trigger_RapidBurst_RunsActionOnce()
	{
		var clock = new ManualDelay();
		int runs = 0;
		using var ran = new ManualResetEventSlim(false);
		using var debouncer = new Debouncer(
			TimeSpan.FromMilliseconds(500),
			() => { Interlocked.Increment(ref runs); ran.Set(); },
			clock.Func);

		debouncer.Trigger();
		debouncer.Trigger();
		debouncer.Trigger();

		// Three triggers scheduled three delays; the first two were superseded and
		// cancelled. Only completing the last one may run the action.
		Assert.Equal(3, clock.Count);
		clock.CompleteLast();

		Assert.True(ran.Wait(TimeSpan.FromSeconds(5)), "Action should run after the last trigger settles.");
		// Let any wrongly-scheduled run have a chance to fire; the count must stay at one.
		Thread.Sleep(50);
		Assert.Equal(1, Volatile.Read(ref runs));
	}

	[Fact]
	public void Trigger_AfterPreviousRunCompleted_RunsAgain()
	{
		var clock = new ManualDelay();
		int runs = 0;
		using var ran = new AutoResetEvent(false);
		using var debouncer = new Debouncer(
			TimeSpan.FromMilliseconds(500),
			() => { Interlocked.Increment(ref runs); ran.Set(); },
			clock.Func);

		debouncer.Trigger();
		clock.CompleteLast();
		Assert.True(ran.WaitOne(TimeSpan.FromSeconds(5)), "First run should fire.");

		debouncer.Trigger();
		clock.CompleteLast();
		Assert.True(ran.WaitOne(TimeSpan.FromSeconds(5)), "A trigger after the first run settled should fire again.");

		Assert.Equal(2, Volatile.Read(ref runs));
	}

	[Fact]
	public void Dispose_WithPendingRun_DoesNotRunAction()
	{
		var clock = new ManualDelay();
		int runs = 0;
		var debouncer = new Debouncer(
			TimeSpan.FromMilliseconds(500),
			() => Interlocked.Increment(ref runs),
			clock.Func);

		debouncer.Trigger();
		debouncer.Dispose();

		// Even if the quiet period "elapses" after disposal, the pending run was cancelled.
		clock.CompleteLast();
		Thread.Sleep(50);
		Assert.Equal(0, Volatile.Read(ref runs));
	}

	[Fact]
	public void Trigger_AfterDispose_IsNoOp()
	{
		var clock = new ManualDelay();
		int runs = 0;
		var debouncer = new Debouncer(
			TimeSpan.FromMilliseconds(500),
			() => Interlocked.Increment(ref runs),
			clock.Func);
		debouncer.Dispose();

		debouncer.Trigger();

		Assert.Equal(0, clock.Count);
		Assert.Equal(0, Volatile.Read(ref runs));
	}

	// The run is fire-and-forget, so without a callback a throwing action is silent:
	// the settings write fails and the user hears nothing (issue #233).
	[Fact]
	public void Trigger_ActionThrows_InvokesTheFailureCallback()
	{
		var clock = new ManualDelay();
		Exception? reported = null;
		using var reportedSignal = new ManualResetEventSlim(false);
		var failure = new IOException("The process cannot access the file.");
		using var debouncer = new Debouncer(
			TimeSpan.FromMilliseconds(500),
			() => throw failure,
			clock.Func,
			onError: ex => { reported = ex; reportedSignal.Set(); });

		debouncer.Trigger();
		clock.CompleteLast();

		Assert.True(reportedSignal.Wait(TimeSpan.FromSeconds(5)), "The failure callback should be invoked.");
		Assert.Same(failure, reported);
	}

	[Fact]
	public void Trigger_ActionThrows_LeavesTheDebouncerUsable()
	{
		var clock = new ManualDelay();
		bool shouldThrow = true;
		int successfulRuns = 0;
		using var ran = new AutoResetEvent(false);
		using var debouncer = new Debouncer(
			TimeSpan.FromMilliseconds(500),
			() =>
			{
				if (shouldThrow)
					throw new IOException("locked");
				Interlocked.Increment(ref successfulRuns);
				ran.Set();
			},
			clock.Func,
			onError: _ => ran.Set());

		debouncer.Trigger();
		clock.CompleteLast();
		Assert.True(ran.WaitOne(TimeSpan.FromSeconds(5)), "The failing run should settle.");

		shouldThrow = false;
		debouncer.Trigger();
		clock.CompleteLast();

		Assert.True(ran.WaitOne(TimeSpan.FromSeconds(5)), "A later trigger should still run.");
		Assert.Equal(1, Volatile.Read(ref successfulRuns));
	}

	// The callback runs on an unobserved task; if it could throw out of RunAsync it
	// would take the process down instead of reporting a save failure.
	[Fact]
	public void Trigger_FailureCallbackItselfThrows_IsSwallowed()
	{
		var clock = new ManualDelay();
		using var called = new ManualResetEventSlim(false);
		using var debouncer = new Debouncer(
			TimeSpan.FromMilliseconds(500),
			() => throw new IOException("locked"),
			clock.Func,
			onError: _ => { called.Set(); throw new InvalidOperationException("reporter failed"); });

		debouncer.Trigger();
		clock.CompleteLast();

		Assert.True(called.Wait(TimeSpan.FromSeconds(5)), "The failure callback should be invoked.");
		Thread.Sleep(50);
	}

	// Cancellation is the normal case (a newer trigger superseded this run) and must
	// never be reported to the user as a failed save.
	[Fact]
	public void Trigger_SupersededRun_DoesNotInvokeTheFailureCallback()
	{
		var clock = new ManualDelay();
		int reports = 0;
		using var ran = new ManualResetEventSlim(false);
		using var debouncer = new Debouncer(
			TimeSpan.FromMilliseconds(500),
			() => ran.Set(),
			clock.Func,
			onError: _ => Interlocked.Increment(ref reports));

		debouncer.Trigger();
		debouncer.Trigger();
		clock.CompleteLast();

		Assert.True(ran.Wait(TimeSpan.FromSeconds(5)), "The surviving run should fire.");
		Thread.Sleep(50);
		Assert.Equal(0, Volatile.Read(ref reports));
	}

	[Fact]
	public void Trigger_ActionThrows_WithNoCallback_DoesNotFaultTheCaller()
	{
		var clock = new ManualDelay();
		using var debouncer = new Debouncer(
			TimeSpan.FromMilliseconds(500),
			() => throw new IOException("locked"),
			clock.Func);

		debouncer.Trigger();
		clock.CompleteLast();
		Thread.Sleep(50);
	}

	[Fact]
	public void Constructor_NullAction_Throws() =>
		Assert.Throws<ArgumentNullException>(() =>
			new Debouncer(TimeSpan.FromMilliseconds(1), null!));

	[Fact]
	public void Constructor_NegativeDelay_Throws() =>
		Assert.Throws<ArgumentOutOfRangeException>(() =>
			new Debouncer(TimeSpan.FromMilliseconds(-1), () => { }));
}
