using Mutation.Ui.Core;

namespace Mutation.Tests;

// The three-way decision behind every control that doubles as its own stop button. It was
// written inline, twice, and the third case is the one that keeps getting missed —
// see issue #309 for the press that produced silence, and #299 for the one that replayed
// the message it had already given.
public class CancellablePressPlannerTests
{
	[Fact]
	public void NothingRunning_Starts()
		=> Assert.Equal(CancellablePressAction.Start, CancellablePressPlanner.For(running: false, cancelRequested: false));

	[Fact]
	public void Running_Cancels()
		=> Assert.Equal(CancellablePressAction.Cancel, CancellablePressPlanner.For(running: true, cancelRequested: false));

	[Fact]
	public void RunningAndAlreadyAskedToStop_IsItsOwnAnswer()
	{
		// Not Cancel — that replays the beep and the request line the user already got.
		// Not Start — nothing new may begin on top of a call still winding down.
		var action = CancellablePressPlanner.For(running: true, cancelRequested: true);

		Assert.Equal(CancellablePressAction.AlreadyStopping, action);
		Assert.NotEqual(CancellablePressAction.Cancel, action);
		Assert.NotEqual(CancellablePressAction.Start, action);
	}

	[Fact]
	public void AStaleCancelFlagWithNothingRunning_StillStarts()
	{
		// The flag is only meaningful while its operation is live. Once the run has
		// released the slot, the next press is an ordinary start.
		Assert.Equal(CancellablePressAction.Start, CancellablePressPlanner.For(running: false, cancelRequested: true));
	}

	[Fact]
	public void APressIsNeverIgnored()
	{
		// Every combination produces an action the caller has to answer. An enabled control
		// that produces silence reads as one that did not register (issue #227).
		foreach (bool running in new[] { true, false })
			foreach (bool cancelRequested in new[] { true, false })
				Assert.True(System.Enum.IsDefined(CancellablePressPlanner.For(running, cancelRequested)));
	}
}
