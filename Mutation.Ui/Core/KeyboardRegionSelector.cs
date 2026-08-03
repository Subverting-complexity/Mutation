using System;

namespace Mutation.Ui.Core;

/// <summary>
/// What a keystroke did to the keyboard-driven region selection, so the caller knows
/// what to redraw and what to announce.
/// </summary>
internal enum RegionKeyOutcome
{
	/// <summary>The key means nothing here; let it bubble.</summary>
	Ignored,

	/// <summary>The caret or the in-progress selection moved.</summary>
	Moved,

	/// <summary>The first corner was pinned; the caret now drags the opposite corner.</summary>
	AnchorSet,

	/// <summary>The whole screen was selected, ready to capture.</summary>
	SelectedWholeScreen,

	/// <summary>The selection is final and the capture should run.</summary>
	Committed,

	/// <summary>Selection was abandoned before an anchor was set.</summary>
	AnchorCleared,

	/// <summary>The key was understood but could not be honoured; say why.</summary>
	Rejected,
}

internal readonly record struct RegionKeyResult(RegionKeyOutcome Outcome, string Announcement)
{
	public static readonly RegionKeyResult Ignored = new(RegionKeyOutcome.Ignored, string.Empty);
}

/// <summary>
/// The keyboard half of the screenshot region overlay.
///
/// The overlay used to be pointer-only, which left four of the app's core hotkeys
/// (screenshot, screenshot+OCR, and both reading-order variants) with no completable
/// path for a user who cannot see the crosshair — Escape was the only key it handled
/// (issue #215).
///
/// The model is a caret plus an optional anchor. Arrow keys move the caret; the first
/// Enter/Space pins the anchor; the second commits the rectangle between them. Ctrl+A
/// selects the whole screen so a capture can always be completed in two keystrokes.
///
/// Coordinates are overlay-space DIPs, matching the pointer path, and all state is
/// plain numbers so the behaviour is testable without a window.
/// </summary>
internal sealed class KeyboardRegionSelector
{
	public const double FineStep = 1;
	public const double NormalStep = 10;
	public const double CoarseStep = 100;

	// Anything smaller crops to nothing once mapped to bitmap pixels.
	public const double MinimumSelectionSize = 1;

	private double _width;
	private double _height;

	public double CaretX { get; private set; }
	public double CaretY { get; private set; }

	public bool HasAnchor { get; private set; }
	public double AnchorX { get; private set; }
	public double AnchorY { get; private set; }

	/// <summary>Left edge of the rectangle currently described, in overlay DIPs.</summary>
	public double SelectionLeft => HasAnchor ? Math.Min(AnchorX, CaretX) : CaretX;
	public double SelectionTop => HasAnchor ? Math.Min(AnchorY, CaretY) : CaretY;
	public double SelectionWidth => HasAnchor ? Math.Abs(CaretX - AnchorX) : 0;
	public double SelectionHeight => HasAnchor ? Math.Abs(CaretY - AnchorY) : 0;

	/// <summary>
	/// (Re)starts a selection over an overlay of the given size, with the caret at the
	/// supplied position — normally where the mouse already is, so switching between
	/// mouse and keyboard mid-selection is not jarring.
	/// </summary>
	public void Reset(double width, double height, double caretX, double caretY)
	{
		_width = Math.Max(0, width);
		_height = Math.Max(0, height);
		HasAnchor = false;
		AnchorX = 0;
		AnchorY = 0;
		CaretX = Clamp(caretX, _width);
		CaretY = Clamp(caretY, _height);
	}

	public void Resize(double width, double height)
	{
		_width = Math.Max(0, width);
		_height = Math.Max(0, height);
		CaretX = Clamp(CaretX, _width);
		CaretY = Clamp(CaretY, _height);
		AnchorX = Clamp(AnchorX, _width);
		AnchorY = Clamp(AnchorY, _height);
	}

	/// <summary>Moves the caret to where the pointer left it, so the two paths agree.</summary>
	public void SyncCaret(double x, double y)
	{
		CaretX = Clamp(x, _width);
		CaretY = Clamp(y, _height);
	}

	public RegionKeyResult HandleKey(RegionSelectionKey key, bool shift, bool control)
	{
		switch (key)
		{
			case RegionSelectionKey.Left:
				return Move(-Step(shift, control), 0);
			case RegionSelectionKey.Right:
				return Move(Step(shift, control), 0);
			case RegionSelectionKey.Up:
				return Move(0, -Step(shift, control));
			case RegionSelectionKey.Down:
				return Move(0, Step(shift, control));
			case RegionSelectionKey.Home:
				return MoveTo(0, CaretY);
			case RegionSelectionKey.End:
				return MoveTo(_width, CaretY);
			case RegionSelectionKey.PageUp:
				return MoveTo(CaretX, 0);
			case RegionSelectionKey.PageDown:
				return MoveTo(CaretX, _height);
			case RegionSelectionKey.SelectAll when control:
				return SelectWholeScreen();
			case RegionSelectionKey.Commit:
				return Commit();
			case RegionSelectionKey.Back:
				return ClearAnchor();
			default:
				return RegionKeyResult.Ignored;
		}
	}

	private static double Step(bool shift, bool control)
	{
		if (control)
			return FineStep;
		if (shift)
			return CoarseStep;
		return NormalStep;
	}

	private RegionKeyResult Move(double dx, double dy) => MoveTo(CaretX + dx, CaretY + dy);

	private RegionKeyResult MoveTo(double x, double y)
	{
		CaretX = Clamp(x, _width);
		CaretY = Clamp(y, _height);
		return new RegionKeyResult(RegionKeyOutcome.Moved, DescribePosition());
	}

	private RegionKeyResult SelectWholeScreen()
	{
		HasAnchor = true;
		AnchorX = 0;
		AnchorY = 0;
		CaretX = _width;
		CaretY = _height;
		return new RegionKeyResult(
			RegionKeyOutcome.SelectedWholeScreen,
			$"Whole screen selected, {Describe(_width)} by {Describe(_height)}. Press Enter to capture.");
	}

	private RegionKeyResult Commit()
	{
		if (!HasAnchor)
		{
			HasAnchor = true;
			AnchorX = CaretX;
			AnchorY = CaretY;
			return new RegionKeyResult(
				RegionKeyOutcome.AnchorSet,
				$"Region corner set at {Describe(CaretX)}, {Describe(CaretY)}. Move with the arrow keys, then press Enter to capture.");
		}

		// A zero-width or zero-height region is discarded further down the capture
		// pipeline without a word, which would leave two presses of Enter looking like
		// the overlay had simply ignored them.
		if (SelectionWidth < MinimumSelectionSize || SelectionHeight < MinimumSelectionSize)
		{
			return new RegionKeyResult(
				RegionKeyOutcome.Rejected,
				"Region is empty. Move with the arrow keys to size it, then press Enter to capture.");
		}

		return new RegionKeyResult(
			RegionKeyOutcome.Committed,
			$"Capturing {Describe(SelectionWidth)} by {Describe(SelectionHeight)} region.");
	}

	private RegionKeyResult ClearAnchor()
	{
		if (!HasAnchor)
			return RegionKeyResult.Ignored;

		HasAnchor = false;
		AnchorX = 0;
		AnchorY = 0;
		return new RegionKeyResult(RegionKeyOutcome.AnchorCleared, "Region corner cleared.");
	}

	private string DescribePosition() => HasAnchor
		? $"{Describe(SelectionWidth)} by {Describe(SelectionHeight)}"
		: $"{Describe(CaretX)}, {Describe(CaretY)}";

	private static string Describe(double value) =>
		Math.Round(value).ToString(System.Globalization.CultureInfo.CurrentCulture);

	private static double Clamp(double value, double max)
	{
		if (double.IsNaN(value))
			return 0;
		return Math.Clamp(value, 0, Math.Max(0, max));
	}
}

/// <summary>
/// The keys the overlay acts on, decoupled from <c>VirtualKey</c> so the selection
/// model stays testable outside a XAML app.
/// </summary>
internal enum RegionSelectionKey
{
	None,
	Left,
	Right,
	Up,
	Down,
	Home,
	End,
	PageUp,
	PageDown,
	Commit,
	Back,
	SelectAll,
}
