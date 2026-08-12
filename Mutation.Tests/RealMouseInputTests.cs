using Mutation.Ui.Core;
using Xunit;

namespace Mutation.Tests;

/// <summary>
/// What one mouse event says about who is driving the mouse (issues #379 and #382).
///
/// <para>
/// This is the rule the pointer hold and the wiggle rest on. Get it wrong in one direction and
/// Mutation drags the pointer out from under its user; get it wrong in the other and it gives up
/// the moment a magnifier moves the pointer, which is the case it exists for. The flags alone
/// were not enough: a magnifier's driver produces events flagged exactly like hardware, and a
/// resting hand produces a continuous stream of them — step size is what separates the two, so
/// step size is pinned here alongside the flags.
/// </para>
/// </summary>
public class RealMouseInputTests
{
	[Fact]
	public void ASmallUnflaggedMove_IsAHandStep()
	{
		Assert.Equal(
			RealMouseEventKind.HandStep,
			RealMouseInput.Classify(RealMouseInput.MouseMove, flags: 0, stepPixels: 1));
	}

	[Fact]
	public void AnUnmovedUnflaggedMove_IsStillAHandStep()
	{
		// A hand resting on a high-polling-rate mouse fires events that do not change the pixel
		// at all. They are still the hand.
		Assert.Equal(
			RealMouseEventKind.HandStep,
			RealMouseInput.Classify(RealMouseInput.MouseMove, flags: 0, stepPixels: 0));
	}

	[Fact]
	public void TheLargestHandSizedStep_IsStillAHandStep()
	{
		Assert.Equal(
			RealMouseEventKind.HandStep,
			RealMouseInput.Classify(RealMouseInput.MouseMove, flags: 0, RealMouseInput.HandStepLimitPixels));
	}

	[Fact]
	public void AnUnflaggedMoveTooBigForAHand_IsATeleport()
	{
		// ZoomText's pointer routing, measured: a single unflagged event tens of pixels long.
		// Reading it as a hand would surrender the pointer to the very grab the hold exists to
		// undo.
		Assert.Equal(
			RealMouseEventKind.Teleport,
			RealMouseInput.Classify(RealMouseInput.MouseMove, flags: 0, RealMouseInput.HandStepLimitPixels + 1));
	}

	[Fact]
	public void InjectedMovement_IsSynthetic_WhateverItsSize()
	{
		// Mutation's own wiggle, or any SendInput. Counting it as a hand would end the hold at
		// the first thing it is meant to correct.
		Assert.Equal(
			RealMouseEventKind.Synthetic,
			RealMouseInput.Classify(RealMouseInput.MouseMove, RealMouseInput.InjectedFlag, stepPixels: 1));
		Assert.Equal(
			RealMouseEventKind.Synthetic,
			RealMouseInput.Classify(RealMouseInput.MouseMove, RealMouseInput.LowerIntegrityInjectedFlag, stepPixels: 40));
		Assert.Equal(
			RealMouseEventKind.Synthetic,
			RealMouseInput.Classify(
				RealMouseInput.MouseMove,
				RealMouseInput.InjectedFlag | RealMouseInput.LowerIntegrityInjectedFlag,
				stepPixels: 2));
	}

	[Fact]
	public void OtherMarksOnRealMovement_DoNotMakeItSynthetic()
	{
		// Only the two injected marks decide this. An unrelated bit set high must not be read as
		// one of them, which is what a comparison against the whole word rather than a mask
		// would do.
		Assert.Equal(
			RealMouseEventKind.HandStep,
			RealMouseInput.Classify(RealMouseInput.MouseMove, flags: 0x8000_0000, stepPixels: 1));
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
	public void AnyButtonOrWheel_IsAHandActEvenWhenInjected(int message)
	{
		// Remote desktop and some KVM software deliver a real hand's input marked as injected.
		// A button is a deliberate act however it arrived, and mistaking a user for a program is
		// the expensive way round.
		Assert.Equal(RealMouseEventKind.HandAct, RealMouseInput.Classify(message, RealMouseInput.InjectedFlag, stepPixels: 0));
		Assert.Equal(RealMouseEventKind.HandAct, RealMouseInput.Classify(message, flags: 0, stepPixels: 0));
	}

	[Fact]
	public void AMessageThatIsNeitherMovementNorAButton_IsNothing()
	{
		// WM_NCMOUSEMOVE, for instance. Nothing was done with the mouse.
		Assert.Equal(RealMouseEventKind.Nothing, RealMouseInput.Classify(0x00A0, flags: 0, stepPixels: 0));
	}
}
