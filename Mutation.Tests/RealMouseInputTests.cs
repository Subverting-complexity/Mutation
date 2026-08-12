using Mutation.Ui.Core;
using Xunit;

namespace Mutation.Tests;

/// <summary>
/// What counts as a hand on the mouse (issue #379).
///
/// <para>
/// This is the rule that makes it safe to keep putting the pointer back for a second and a half
/// after a capture. Get it wrong in one direction and Mutation drags the pointer out from under
/// its user; get it wrong in the other and it gives up the moment a magnifier moves the pointer,
/// which is the case it exists for. It used to live inside the hook, where nothing could reach
/// it, and it was true only because a comment said so.
/// </para>
/// </summary>
public class RealMouseInputTests
{
	[Fact]
	public void MovementFromAMouse_IsTheUser()
	{
		Assert.True(RealMouseInput.IsHandOnTheMouse(RealMouseInput.MouseMove, flags: 0));
	}

	[Fact]
	public void InjectedMovement_IsNotTheUser()
	{
		// A magnifier moving the pointer to the caret, or Mutation's own wiggle. Treating either
		// as a hand on the mouse would end the hold at the first thing it is meant to correct.
		Assert.False(RealMouseInput.IsHandOnTheMouse(RealMouseInput.MouseMove, RealMouseInput.InjectedFlag));
	}

	[Fact]
	public void MovementInjectedFromLowerIntegrity_IsAlsoNotTheUser()
	{
		Assert.False(RealMouseInput.IsHandOnTheMouse(RealMouseInput.MouseMove, RealMouseInput.LowerIntegrityInjectedFlag));
	}

	[Fact]
	public void BothInjectedMarksTogether_IsStillNotTheUser()
	{
		Assert.False(RealMouseInput.IsHandOnTheMouse(
			RealMouseInput.MouseMove,
			RealMouseInput.InjectedFlag | RealMouseInput.LowerIntegrityInjectedFlag));
	}

	[Fact]
	public void OtherMarksOnRealMovement_DoNotMakeItInjected()
	{
		// Only the two injected marks decide this. An unrelated bit set high must not be read as
		// one of them, which is what a comparison against the whole word rather than a mask would
		// do.
		Assert.True(RealMouseInput.IsHandOnTheMouse(RealMouseInput.MouseMove, flags: 0x8000_0000));
	}

	[Theory]
	[InlineData(RealMouseInput.LeftButtonDown)]
	[InlineData(RealMouseInput.LeftButtonUp)]
	[InlineData(RealMouseInput.RightButtonDown)]
	[InlineData(RealMouseInput.RightButtonUp)]
	[InlineData(RealMouseInput.MiddleButtonDown)]
	[InlineData(RealMouseInput.MiddleButtonUp)]
	[InlineData(RealMouseInput.ExtraButtonDown)]
	[InlineData(RealMouseInput.ExtraButtonUp)]
	[InlineData(RealMouseInput.Wheel)]
	[InlineData(RealMouseInput.HorizontalWheel)]
	public void AnyButtonOrWheel_IsTheUserEvenWhenInjected(int message)
	{
		// Remote desktop and some KVM software deliver a real hand's movement marked as injected.
		// A button is a deliberate act however it arrived, and mistaking a user for a program is
		// the expensive way round.
		Assert.True(RealMouseInput.IsHandOnTheMouse(message, RealMouseInput.InjectedFlag));
		Assert.True(RealMouseInput.IsHandOnTheMouse(message, flags: 0));
	}

	[Fact]
	public void AMessageThatIsNeitherMovementNorAButton_IsNotTheUser()
	{
		// WM_NCMOUSEMOVE, for instance. Nothing was done with the mouse.
		Assert.False(RealMouseInput.IsHandOnTheMouse(0x00A0, flags: 0));
	}
}
