using Mutation.Ui.Core;

namespace Mutation.Tests;

// The latch that decides whether a settings page may read its own errors out loud yet
// (issue #350).
public class FirstTouchGateTests
{
	[Fact]
	public void APageStartsUntouched()
	{
		Assert.False(new FirstTouchGate().HasBeenTouched);
	}

	[Fact]
	public void TouchingItOnce_OpensItAndSaysSo()
	{
		var gate = new FirstTouchGate();
		int announced = 0;
		gate.Touched += (_, _) => announced++;

		gate.Touch();

		Assert.True(gate.HasBeenTouched);
		Assert.Equal(1, announced);
	}

	[Fact]
	public void TouchingItAgain_ChangesNothingAndSaysNothing()
	{
		// Every handler on the page calls this without checking first, so the second call and
		// every one after it has to be free.
		var gate = new FirstTouchGate();
		int announced = 0;
		gate.Touched += (_, _) => announced++;

		gate.Touch();
		gate.Touch();
		gate.Touch();

		Assert.True(gate.HasBeenTouched);
		Assert.Equal(1, announced);
	}

	[Fact]
	public void AGateWithNobodyListening_StillOpens()
	{
		var gate = new FirstTouchGate();

		gate.Touch();

		Assert.True(gate.HasBeenTouched);
	}

	[Fact]
	public void ItNeverCloses()
	{
		// One direction only, on purpose. A page cannot become unused, and a gate that could
		// shut again would let a row that had spoken once fall silent later for no reason the
		// user could see.
		var gate = new FirstTouchGate();
		gate.Touch();

		Assert.True(gate.HasBeenTouched);
	}
}
