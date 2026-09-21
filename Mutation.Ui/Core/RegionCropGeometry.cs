using System;

namespace Mutation.Ui.Core;

/// <summary>
/// Turns the two corners the user marked into the rectangle of the screenshot to keep.
///
/// <para>
/// Captures were coming back short. A rectangle drawn tightly around a word produced a picture
/// with the last letter missing and the one before it sliced down the middle, every time, on
/// both the plain screenshot path and the OCR one. Drawn loosely it looked fine, which is why
/// it went unnoticed for so long: the slack absorbed the loss (issue #393).
/// </para>
///
/// <para>
/// The old sum ran the corners through four things that all had to agree — the size the
/// drawing area reported, the size the screenshot came out at, the window's drawing area
/// covering exactly the virtual screen, and the pointer positions being in the units the rest
/// of the sum assumed. Read on their own the four do agree, so the fault could not be found by
/// reading; one of them tells a different story on a real desktop and nothing could say which.
/// </para>
///
/// <para>
/// So the sum is gone. The screenshot is in virtual-screen pixels, and Windows reports the
/// pointer in virtual-screen pixels, so <see cref="FromScreenPixels"/> subtracts the screen's
/// origin and stops. No display scale, no drawing-area size, no device-independent pixels —
/// none of the four is in the answer at all. <see cref="FromDrawingArea"/> keeps the old route
/// for the keyboard path, which has no pointer to read, and for the rare mouse capture where
/// the pointer cannot be read; it is corrected to round outward rather than to nearest.
/// </para>
///
/// <para>
/// Arithmetic only, with no window and no Win32 in it, so the rules can be checked without a
/// screen and without flinging the developer's pointer across the desktop. The same split as
/// <see cref="OverlayDrawingArea"/> and <see cref="KeyboardRegionSelector"/>, and for the same
/// reason.
/// </para>
/// </summary>
internal static class RegionCropGeometry
{
	/// <summary>
	/// How far the two routes may differ before the difference is worth writing down. The
	/// screen-pixel route counts the pixel under the pointer at each end, and the
	/// drawing-area route rounds a fractional edge outward, so one pixel of daylight on any
	/// edge is ordinary. More than that means one of the two is wrong, and which is a thing
	/// worth knowing.
	/// </summary>
	public const int AgreementTolerancePixels = 2;

	/// <summary>
	/// The crop rectangle, worked out from where the pointer actually was when the button went
	/// down and came up.
	/// <para>
	/// Both ends are inclusive: the pixel under the pointer belongs to the selection at the
	/// start and at the finish. Half-open would quietly drop the last row and column of what
	/// the user drew around, which is the whole complaint this method exists to answer.
	/// </para>
	/// </summary>
	/// <param name="first">Where the pointer was when the selection started, in virtual-screen pixels.</param>
	/// <param name="second">Where it was when the selection ended. Either corner may be the smaller.</param>
	/// <param name="virtualLeft">Left edge of the virtual screen, which is pixel zero of the screenshot.</param>
	/// <param name="virtualTop">Top edge of the virtual screen, which is row zero of the screenshot.</param>
	public static PixelRect FromScreenPixels(
		CursorPoint first,
		CursorPoint second,
		int virtualLeft,
		int virtualTop,
		int bitmapWidth,
		int bitmapHeight)
	{
		int left = Math.Min(first.X, second.X) - virtualLeft;
		int top = Math.Min(first.Y, second.Y) - virtualTop;
		int right = Math.Max(first.X, second.X) - virtualLeft;
		int bottom = Math.Max(first.Y, second.Y) - virtualTop;

		// Plus one because both ends are inclusive and the rectangle's far edges are not.
		return Clamp(left, top, right + 1, bottom + 1, bitmapWidth, bitmapHeight);
	}

	/// <summary>
	/// The crop rectangle, worked out from two corners in the overlay's own coordinates — the
	/// device-independent pixels the pointer and keyboard caret are reported in.
	/// <para>
	/// Both edges are converted and then subtracted, rather than converting a corner and a
	/// length separately. A corner and a length rounded apart do not add up: each is allowed
	/// its own half-pixel of error and they do not have to cancel, so the far edge can land
	/// inside where it belongs. Two converted edges always do add up.
	/// </para>
	/// <para>
	/// The near edge takes the floor and the far edge the ceiling, so an edge falling between
	/// two pixels keeps the pixel rather than dropping it. Rounding to nearest, which is what
	/// this used to do, is as willing to shrink the selection as to grow it — and a capture
	/// that is a pixel too wide costs nothing, while one a pixel too narrow cuts the upright
	/// off a letter and leaves OCR guessing.
	/// </para>
	/// </summary>
	/// <param name="drawingWidth">Width of the overlay's drawing area, in its own coordinates.</param>
	/// <param name="drawingHeight">Height of the overlay's drawing area, in its own coordinates.</param>
	public static PixelRect FromDrawingArea(
		double firstX,
		double firstY,
		double secondX,
		double secondY,
		double drawingWidth,
		double drawingHeight,
		int bitmapWidth,
		int bitmapHeight)
	{
		double scaleX = bitmapWidth / Math.Max(1.0, drawingWidth);
		double scaleY = bitmapHeight / Math.Max(1.0, drawingHeight);

		double nearX = Math.Min(firstX, secondX) * scaleX;
		double farX = Math.Max(firstX, secondX) * scaleX;
		double nearY = Math.Min(firstY, secondY) * scaleY;
		double farY = Math.Max(firstY, secondY) * scaleY;

		return Clamp(
			(int)Math.Floor(nearX),
			(int)Math.Floor(nearY),
			(int)Math.Ceiling(farX),
			(int)Math.Ceiling(farY),
			bitmapWidth,
			bitmapHeight);
	}

	/// <summary>
	/// Whether two answers for the same selection are near enough to be the same answer. Used
	/// to decide whether a capture is worth a line in the log, not to decide which to use.
	/// </summary>
	public static bool Agree(PixelRect first, PixelRect second, int tolerancePixels = AgreementTolerancePixels)
		=> Math.Abs(first.Left - second.Left) <= tolerancePixels
		&& Math.Abs(first.Top - second.Top) <= tolerancePixels
		&& Math.Abs(first.Width - second.Width) <= tolerancePixels
		&& Math.Abs(first.Height - second.Height) <= tolerancePixels;

	/// <summary>
	/// Brings a rectangle inside the screenshot, keeping at least one pixel each way.
	/// <para>
	/// The far edges arrive exclusive. Never empty: a click that does not move is still a
	/// capture, and handing back nothing would be reported to the user as a cancelled one.
	/// </para>
	/// </summary>
	private static PixelRect Clamp(int left, int top, int right, int bottom, int bitmapWidth, int bitmapHeight)
	{
		// No picture to cut from. Says so rather than inventing a pixel that is not there.
		if (bitmapWidth <= 0 || bitmapHeight <= 0)
			return new PixelRect(0, 0, 0, 0);

		left = Math.Clamp(left, 0, bitmapWidth - 1);
		top = Math.Clamp(top, 0, bitmapHeight - 1);
		right = Math.Clamp(right, left + 1, bitmapWidth);
		bottom = Math.Clamp(bottom, top + 1, bitmapHeight);

		return new PixelRect(left, top, right - left, bottom - top);
	}
}
