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
///
/// What is *said* is in bitmap pixels, because that is the unit the capture is announced
/// in. The two used to disagree: moving read out DIPs and the capture read out pixels, so
/// on a 150% display a user sizing a region by the spoken numbers was fed two readings
/// that did not agree (issue #265). See <see cref="SetBitmapSize"/>.
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
	private double _bitmapWidth;
	private double _bitmapHeight;

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

	/// <summary>
	/// The size of the captured bitmap, which is what everything spoken is scaled to. The
	/// overlay is measured in DIPs and the capture in bitmap pixels, and on any display
	/// above 100% those are different numbers for the same edge.
	/// <para>
	/// Told as a size rather than as a ratio so a later <see cref="Resize"/> — the overlay
	/// re-laid out across a changed set of monitors — cannot leave a stale scale behind.
	/// Zero, or never calling this, leaves everything spoken in DIPs, which is what a 100%
	/// display gives anyway.
	/// </para>
	/// </summary>
	public void SetBitmapSize(double width, double height)
	{
		_bitmapWidth = double.IsFinite(width) ? Math.Max(0, width) : 0;
		_bitmapHeight = double.IsFinite(height) ? Math.Max(0, height) : 0;
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
			$"Whole screen selected, {DescribeX(_width)} by {DescribeY(_height)} pixels. Press Enter to capture.");
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
				$"Region corner set at {DescribeX(CaretX)}, {DescribeY(CaretY)} pixels. Move with the arrow keys, then press Enter to capture.");
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

		// No announcement: only the caller knows the region's size in bitmap pixels, and
		// it announces the capture for the mouse path too.
		return new RegionKeyResult(RegionKeyOutcome.Committed, string.Empty);
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

	// Once a corner is pinned, size alone is not enough: without the origin there is no
	// way to tell where on the screen the rectangle sits, and no way to recover it short
	// of clearing the corner and starting again.
	private string DescribePosition() => HasAnchor
		? $"{DescribeX(SelectionWidth)} by {DescribeY(SelectionHeight)}, from {DescribeX(SelectionLeft)}, {DescribeY(SelectionTop)}"
		: $"{DescribeX(CaretX)}, {DescribeY(CaretY)}";

	// Said on every arrow press, so the unit is left off here and stated on the two
	// announcements that are not a running readout. A number per keystroke is what makes
	// sizing by ear bearable; "410 pixels, 250 pixels" ten times a second is not.
	private string DescribeX(double dips) => Describe(dips * ScaleX);

	private string DescribeY(double dips) => Describe(dips * ScaleY);

	private double ScaleX => _bitmapWidth > 0 && _width > 0 ? _bitmapWidth / _width : 1;

	private double ScaleY => _bitmapHeight > 0 && _height > 0 ? _bitmapHeight / _height : 1;

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
