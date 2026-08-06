using System;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using Mutation.Ui.Core;
using Xunit;

namespace Mutation.Tests;

public class WaveformRenderGateTests
{
	[Fact]
	public void ConsumeShouldRender_WithoutData_ReturnsFalse()
	{
		var gate = new WaveformRenderGate();

		Assert.False(gate.ConsumeShouldRender());
	}

	[Fact]
	public void ConsumeShouldRender_AfterDataArrived_ReturnsTrueThenFalse()
	{
		var gate = new WaveformRenderGate();
		gate.MarkDataArrived();

		Assert.True(gate.ConsumeShouldRender());
		Assert.False(gate.ConsumeShouldRender());
	}

	[Fact]
	public void ConsumeShouldRender_CoalescesMultipleArrivalsIntoOneRender()
	{
		var gate = new WaveformRenderGate();
		gate.MarkDataArrived();
		gate.MarkDataArrived();
		gate.MarkDataArrived();

		Assert.True(gate.ConsumeShouldRender());
		Assert.False(gate.ConsumeShouldRender());
	}

	[Fact]
	public void ConsumeShouldRender_ReRendersAfterEachNewArrival()
	{
		var gate = new WaveformRenderGate();

		gate.MarkDataArrived();
		Assert.True(gate.ConsumeShouldRender());
		Assert.False(gate.ConsumeShouldRender());

		gate.MarkDataArrived();
		Assert.True(gate.ConsumeShouldRender());
		Assert.False(gate.ConsumeShouldRender());
	}

	// The capture callback marks arrivals while the render tick consumes them, on two
	// different threads. Awaiting a Task.Run would publish the write for free and prove
	// nothing, so several producers run against the consumer here for real.
	//
	// What this pins down: with capture running, the render tick keeps being told to
	// redraw, and once capture stops the gate settles clear and stays clear. It does not
	// claim to catch a torn read-and-clear — on x64 a plain bool field would survive that
	// too, which is why the deleted version proved nothing. Threads, not thread-pool
	// tasks, so a stall cannot occupy a pool slot; every loop yields rather than spinning
	// hot; and the whole thing is bounded by a deadline so a broken gate fails rather
	// than hangs.
	[Fact]
	public void ConsumeShouldRender_WithCaptureRunning_KeepsSignalling_ThenSettlesClear()
	{
		const int Producers = 4;
		const int TargetRenders = 1_000;

		var gate = new WaveformRenderGate();
		using var stop = new CancellationTokenSource();

		var producers = Enumerable.Range(0, Producers).Select(_ =>
		{
			var thread = new Thread(() =>
			{
				while (!stop.IsCancellationRequested)
				{
					gate.MarkDataArrived();
					Thread.Yield();
				}
			})
			{ IsBackground = true, Name = "waveform-gate-producer" };
			thread.Start();
			return thread;
		}).ToArray();

		int renders = 0;
		try
		{
			// The test thread plays the render tick. A working gate reaches the target in
			// milliseconds; ten seconds is four orders of magnitude of slack for a loaded
			// agent, so only a gate that stopped signalling can time out here.
			var elapsed = Stopwatch.StartNew();
			while (renders < TargetRenders && elapsed.Elapsed < TimeSpan.FromSeconds(10))
			{
				if (gate.ConsumeShouldRender())
					renders++;
				else
					Thread.Yield();
			}
		}
		finally
		{
			stop.Cancel();
			foreach (var producer in producers)
				producer.Join(TimeSpan.FromSeconds(5));
		}

		Assert.Equal(TargetRenders, renders);

		// Capture has stopped. Draining once must leave the gate clear and keep it clear —
		// a consume that failed to clear is the ~30 FPS idle redraw this gate exists to
		// stop, and it would have raced through the loop above without ever settling.
		gate.ConsumeShouldRender();
		Assert.False(gate.ConsumeShouldRender());
	}
}
