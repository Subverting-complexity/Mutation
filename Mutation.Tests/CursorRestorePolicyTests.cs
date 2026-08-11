using Mutation.Ui.Core;
using Xunit;

namespace Mutation.Tests;

/// <summary>
/// How far a deferred pointer restore may go once the user has started choosing a region
/// (issue #371). The rule these pin is that putting the pointer back and restarting the
/// selection are two different things, and only the first is safe mid-selection.
/// </summary>
public class CursorRestorePolicyTests
{
	[Fact]
	public void NothingStartedYet_PointerGoesBackAndTheCrosshairFollowsIt()
	{
		// The ordinary case: the overlay has just opened, so there is no selection to protect
		// and the crosshair should be redrawn wherever the pointer really ended up.
		Assert.Equal(
			DeferredCursorRestore.MoveAndReseed,
			CursorRestorePolicy.Decide(dragInFlight: false, keyboardCornerPinned: false));
	}

	[Fact]
	public void DragInFlight_NothingIsTouched()
	{
		// A button is down and a rectangle is being dragged out. Moving the pointer would
		// redraw the user's rectangle from under them.
		Assert.Equal(
			DeferredCursorRestore.StandDown,
			CursorRestorePolicy.Decide(dragInFlight: true, keyboardCornerPinned: false));
	}

	[Fact]
	public void KeyboardCornerPinned_PointerGoesBackButTheSelectionSurvives()
	{
		// The case that made this rule worth naming. A second hotkey press on a capture that is
		// already running brings the overlay forward, which runs a restore — and re-seeding
		// there would clear the pinned corner without saying so, leaving someone who cannot see
		// the overlay to press Enter and pin a fresh corner instead of capturing.
		Assert.Equal(
			DeferredCursorRestore.MoveOnly,
			CursorRestorePolicy.Decide(dragInFlight: false, keyboardCornerPinned: true));
	}

	[Fact]
	public void DragWins_EvenWithAKeyboardCornerPinned()
	{
		// A mouse drag supersedes a half-finished keyboard selection, and the pointer is then
		// the user's drawing hand, so standing down beats moving it back.
		Assert.Equal(
			DeferredCursorRestore.StandDown,
			CursorRestorePolicy.Decide(dragInFlight: true, keyboardCornerPinned: true));
	}
}
