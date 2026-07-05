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

	[Fact]
	public async Task MarkDataArrived_FromAnotherThread_IsObservedByConsumer()
	{
		var gate = new WaveformRenderGate();

		await Task.Run(gate.MarkDataArrived);

		Assert.True(gate.ConsumeShouldRender());
	}
}
