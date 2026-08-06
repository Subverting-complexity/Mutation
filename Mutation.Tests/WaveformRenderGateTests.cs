using System.Linq;
using System.Threading;
using System.Threading.Tasks;
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

	// The audio capture callback marks arrivals while the render timer consumes them, on
	// two different threads with no lock between them. Awaiting a Task.Run would publish
	// the write for free and prove nothing, so this runs the producers concurrently with
	// a consumer loop and checks the two invariants that matter: every render signal the
	// consumer sees was earned by a real arrival, and once the producers are finished no
	// arrival is left stranded without a render.
	[Fact]
	public async Task MarkDataArrived_UnderConcurrentProducers_NeitherLosesNorInventsRenders()
	{
		const int Producers = 4;
		const int ArrivalsEach = 20_000;

		var gate = new WaveformRenderGate();
		using var producersDone = new CountdownEvent(Producers);
		int renders = 0;

		var consumer = Task.Run(() =>
		{
			// Keep consuming until the producers have stopped, then drain what is left.
			while (!producersDone.IsSet)
			{
				if (gate.ConsumeShouldRender())
					renders++;
			}

			if (gate.ConsumeShouldRender())
				renders++;
		});

		var producers = Enumerable.Range(0, Producers).Select(_ => Task.Run(() =>
		{
			for (int i = 0; i < ArrivalsEach; i++)
				gate.MarkDataArrived();
			producersDone.Signal();
		})).ToArray();

		await Task.WhenAll(producers);
		await consumer;

		// Coalescing means renders <= arrivals, never the other way around: a render the
		// consumer was never told to do would be a spurious redraw every frame.
		Assert.InRange(renders, 1, Producers * ArrivalsEach);
		// And nothing is left pending — the final drain claimed the last arrival.
		Assert.False(gate.ConsumeShouldRender());
	}
}
