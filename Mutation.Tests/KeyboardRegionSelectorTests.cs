using Mutation.Ui.Core;

namespace Mutation.Tests;

// Issue #215: the screenshot region overlay was pointer-only, leaving four core hotkeys
// with no completable path for a user who cannot see the crosshair.
public class KeyboardRegionSelectorTests
{
	private static KeyboardRegionSelector NewSelector(double width = 1000, double height = 800)
	{
		var selector = new KeyboardRegionSelector();
		selector.Reset(width, height, width / 2, height / 2);
		return selector;
	}

	private static RegionKeyResult Press(KeyboardRegionSelector selector, RegionSelectionKey key, bool shift = false, bool control = false)
		=> selector.HandleKey(key, shift, control);

	[Fact]
	public void ArrowKeys_MoveTheCaret()
	{
		var selector = NewSelector();
		double startX = selector.CaretX;
		double startY = selector.CaretY;

		Press(selector, RegionSelectionKey.Right);
		Press(selector, RegionSelectionKey.Down);

		Assert.Equal(startX + KeyboardRegionSelector.NormalStep, selector.CaretX);
		Assert.Equal(startY + KeyboardRegionSelector.NormalStep, selector.CaretY);
	}

	[Fact]
	public void ModifiersChooseTheStepSize()
	{
		var selector = NewSelector();
		double startX = selector.CaretX;

		Press(selector, RegionSelectionKey.Right, control: true);
		Assert.Equal(startX + KeyboardRegionSelector.FineStep, selector.CaretX);

		Press(selector, RegionSelectionKey.Right, shift: true);
		Assert.Equal(startX + KeyboardRegionSelector.FineStep + KeyboardRegionSelector.CoarseStep, selector.CaretX);
	}

	[Fact]
	public void TheCaretStaysInsideTheOverlay()
	{
		var selector = NewSelector(width: 100, height: 100);

		for (int i = 0; i < 50; i++)
		{
			Press(selector, RegionSelectionKey.Left, shift: true);
			Press(selector, RegionSelectionKey.Up, shift: true);
		}
		Assert.Equal(0, selector.CaretX);
		Assert.Equal(0, selector.CaretY);

		for (int i = 0; i < 50; i++)
		{
			Press(selector, RegionSelectionKey.Right, shift: true);
			Press(selector, RegionSelectionKey.Down, shift: true);
		}
		Assert.Equal(100, selector.CaretX);
		Assert.Equal(100, selector.CaretY);
	}

	[Fact]
	public void HomeEndAndPaging_JumpToTheEdges()
	{
		var selector = NewSelector(width: 640, height: 480);

		Press(selector, RegionSelectionKey.Home);
		Assert.Equal(0, selector.CaretX);

		Press(selector, RegionSelectionKey.End);
		Assert.Equal(640, selector.CaretX);

		Press(selector, RegionSelectionKey.PageUp);
		Assert.Equal(0, selector.CaretY);

		Press(selector, RegionSelectionKey.PageDown);
		Assert.Equal(480, selector.CaretY);
	}

	[Fact]
	public void FirstCommitPinsTheAnchorAndSecondCommitCaptures()
	{
		var selector = NewSelector();
		Press(selector, RegionSelectionKey.Home);
		Press(selector, RegionSelectionKey.PageUp);

		var anchor = Press(selector, RegionSelectionKey.Commit);
		Assert.Equal(RegionKeyOutcome.AnchorSet, anchor.Outcome);
		Assert.True(selector.HasAnchor);

		for (int i = 0; i < 3; i++)
		{
			Press(selector, RegionSelectionKey.Right);
			Press(selector, RegionSelectionKey.Down);
		}

		var committed = Press(selector, RegionSelectionKey.Commit);
		Assert.Equal(RegionKeyOutcome.Committed, committed.Outcome);
		Assert.Equal(3 * KeyboardRegionSelector.NormalStep, selector.SelectionWidth);
		Assert.Equal(3 * KeyboardRegionSelector.NormalStep, selector.SelectionHeight);
		Assert.Equal(0, selector.SelectionLeft);
		Assert.Equal(0, selector.SelectionTop);
	}

	[Fact]
	public void ASelectionDraggedUpAndLeftIsStillNormalised()
	{
		var selector = NewSelector();
		Press(selector, RegionSelectionKey.End);
		Press(selector, RegionSelectionKey.PageDown);
		Press(selector, RegionSelectionKey.Commit);

		Press(selector, RegionSelectionKey.Left, shift: true);
		Press(selector, RegionSelectionKey.Up, shift: true);

		Assert.Equal(1000 - KeyboardRegionSelector.CoarseStep, selector.SelectionLeft);
		Assert.Equal(800 - KeyboardRegionSelector.CoarseStep, selector.SelectionTop);
		Assert.Equal(KeyboardRegionSelector.CoarseStep, selector.SelectionWidth);
		Assert.Equal(KeyboardRegionSelector.CoarseStep, selector.SelectionHeight);
	}

	[Fact]
	public void ControlA_SelectsTheWholeScreenAsAKeyboardFallback()
	{
		var selector = NewSelector(width: 1920, height: 1080);

		var result = Press(selector, RegionSelectionKey.SelectAll, control: true);

		Assert.Equal(RegionKeyOutcome.SelectedWholeScreen, result.Outcome);
		Assert.Equal(0, selector.SelectionLeft);
		Assert.Equal(0, selector.SelectionTop);
		Assert.Equal(1920, selector.SelectionWidth);
		Assert.Equal(1080, selector.SelectionHeight);

		// And Enter completes the capture — a whole-screen grab in two keystrokes.
		Assert.Equal(RegionKeyOutcome.Committed, Press(selector, RegionSelectionKey.Commit).Outcome);
	}

	[Fact]
	public void PlainA_IsNotSelectAll()
	{
		var selector = NewSelector();

		Assert.Equal(RegionKeyOutcome.Ignored, Press(selector, RegionSelectionKey.SelectAll).Outcome);
	}

	[Fact]
	public void CommittingAnEmptyRegionIsRefusedOutLoud()
	{
		// Two presses of Enter without moving used to produce a zero-sized rect that the
		// capture pipeline discarded silently.
		var selector = NewSelector();
		Press(selector, RegionSelectionKey.Commit);

		var result = Press(selector, RegionSelectionKey.Commit);

		Assert.Equal(RegionKeyOutcome.Rejected, result.Outcome);
		Assert.Contains("empty", result.Announcement, StringComparison.OrdinalIgnoreCase);
	}

	[Fact]
	public void BackspaceClearsAPinnedCorner()
	{
		var selector = NewSelector();
		Press(selector, RegionSelectionKey.Commit);
		Assert.True(selector.HasAnchor);

		var cleared = Press(selector, RegionSelectionKey.Back);

		Assert.Equal(RegionKeyOutcome.AnchorCleared, cleared.Outcome);
		Assert.False(selector.HasAnchor);
		Assert.Equal(0, selector.SelectionWidth);
	}

	[Fact]
	public void BackspaceWithoutAnAnchorIsIgnored()
	{
		var selector = NewSelector();

		Assert.Equal(RegionKeyOutcome.Ignored, Press(selector, RegionSelectionKey.Back).Outcome);
	}

	[Fact]
	public void EveryActedOnKeyAnnouncesSomething()
	{
		// A blind user gets no feedback from the crosshair, so each accepted key has to
		// say what happened.
		var selector = NewSelector();
		RegionSelectionKey[] keys =
		[
			RegionSelectionKey.Left, RegionSelectionKey.Right, RegionSelectionKey.Up, RegionSelectionKey.Down,
			RegionSelectionKey.Home, RegionSelectionKey.End, RegionSelectionKey.PageUp, RegionSelectionKey.PageDown,
			RegionSelectionKey.Commit,
		];

		foreach (var key in keys)
		{
			var result = Press(selector, key);
			Assert.NotEqual(RegionKeyOutcome.Ignored, result.Outcome);
			Assert.False(string.IsNullOrWhiteSpace(result.Announcement), $"{key} announced nothing.");
		}
	}

	[Fact]
	public void ResizingKeepsTheSelectionInsideTheNewBounds()
	{
		var selector = NewSelector(width: 1000, height: 800);
		Press(selector, RegionSelectionKey.End);
		Press(selector, RegionSelectionKey.PageDown);

		selector.Resize(400, 300);

		Assert.Equal(400, selector.CaretX);
		Assert.Equal(300, selector.CaretY);
	}

	[Fact]
	public void SyncCaret_TracksThePointerAndClamps()
	{
		var selector = NewSelector(width: 500, height: 500);

		selector.SyncCaret(123, 456);
		Assert.Equal(123, selector.CaretX);
		Assert.Equal(456, selector.CaretY);

		selector.SyncCaret(9999, -20);
		Assert.Equal(500, selector.CaretX);
		Assert.Equal(0, selector.CaretY);
	}

	[Fact]
	public void ResetStartsWithoutAnAnchorAtTheGivenCaret()
	{
		var selector = new KeyboardRegionSelector();

		selector.Reset(800, 600, 42, 84);

		Assert.False(selector.HasAnchor);
		Assert.Equal(42, selector.CaretX);
		Assert.Equal(84, selector.CaretY);
		Assert.Equal(0, selector.SelectionWidth);
		Assert.Equal(0, selector.SelectionHeight);
	}
}
