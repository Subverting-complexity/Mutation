namespace Mutation.Ui.Core;

/// <summary>A rectangle in screen pixels.</summary>
internal readonly record struct PixelRect(int Left, int Top, int Width, int Height);

/// <summary>
/// Works out how large a window has to be, and where it has to sit, for the area it draws
/// into to land exactly on a target rectangle.
///
/// <para>
/// The capture overlay needs this because it shows one image stretched to fill its drawing
/// area, and that image is a picture of the whole screen. The picture only stays sharp when
/// the area it is stretched across holds exactly as many pixels as the picture does. Sizing
/// the window to the screen does not achieve that: a window is measured by its outer
/// rectangle, and the area inside it is smaller by whatever frame the window carries. On a
/// 1920 by 1080 display the drawing area came back as 1914 by 1074, six pixels short each
/// way, and the whole screenshot was being squeezed into it. That shrink of a third of a
/// percent moves nothing anyone can see, but it resamples every pixel, and text loses the
/// hard one-pixel edges that make it look crisp (issue #388).
/// </para>
///
/// <para>
/// Kept apart from the window itself, and from the Win32 calls that supply the numbers, so
/// the arithmetic can be checked without a window. The same split as
/// <see cref="KeyboardRegionSelector"/> and <see cref="PointerNudgePlanner"/>, and for the
/// same reason: the interesting part is a decision over four rectangles, and none of it
/// needs a screen to be true.
/// </para>
/// </summary>
internal static class OverlayDrawingArea
{
	/// <summary>
	/// The outer rectangle a window needs so that its drawing area covers
	/// <paramref name="target"/> exactly, or null when it already does, or when the numbers
	/// describe something that cannot be compensated for.
	/// </summary>
	/// <param name="window">The window's current outer rectangle, in screen pixels.</param>
	/// <param name="drawingWidth">Width of the area the window draws into.</param>
	/// <param name="drawingHeight">Height of the area the window draws into.</param>
	/// <param name="drawingOrigin">
	/// Where the drawing area's top-left corner sits on screen. Asked for rather than worked
	/// out by halving the frame, because a frame is not always shared evenly between the
	/// edges, and a window whose left border is thicker than its right would end up offset by
	/// the difference.
	/// </param>
	/// <param name="target">Where the drawing area should land. For the overlay, the whole virtual screen.</param>
	public static PixelRect? Fit(PixelRect window, int drawingWidth, int drawingHeight, (int X, int Y) drawingOrigin, PixelRect target)
	{
		// A window that has not been laid out yet reports nothing useful. Leave it alone
		// rather than acting on a measurement that says the drawing area has no size.
		if (drawingWidth <= 0 || drawingHeight <= 0)
			return null;

		int extraWidth = window.Width - drawingWidth;
		int extraHeight = window.Height - drawingHeight;

		// No frame at all, so the window is already all drawing area and nothing needs moving.
		// This is the answer a genuinely borderless window gives, and it must stay a no-op:
		// nudging it would move the content off the screen for no gain.
		if (extraWidth == 0 && extraHeight == 0)
			return null;

		// A drawing area larger than the window it lives in is not a frame, it is a bad
		// measurement. Growing the window by a negative amount would shrink it and make the
		// blur worse, so decline.
		if (extraWidth < 0 || extraHeight < 0)
			return null;

		int insetLeft = drawingOrigin.X - window.Left;
		int insetTop = drawingOrigin.Y - window.Top;

		// The drawing area starts inside the window and ends inside it. Anything else means
		// the two rectangles were not read at the same moment, which happens if the window is
		// being moved while it is measured.
		if (insetLeft < 0 || insetTop < 0 || insetLeft > extraWidth || insetTop > extraHeight)
			return null;

		return new PixelRect(
			target.Left - insetLeft,
			target.Top - insetTop,
			target.Width + extraWidth,
			target.Height + extraHeight);
	}
}
