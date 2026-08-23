using Mutation.Ui.Core;

namespace Mutation.Tests;

// Issue #388: the capture overlay was sized to the screen, but a window's drawing area is
// smaller than its outer rectangle by the window frame, so the full-size screenshot was
// stretched into a slightly smaller space and resampled. Measured on a 1920 by 1080 display
// the drawing area came back as 1914 by 1074, and the preview kept 30.8% of the hard edges
// the real desktop had.
public class OverlayDrawingAreaTests
{
	private static readonly PixelRect Screen = new(0, 0, 1920, 1080);

	/// <summary>The measurement taken from the affected machine: a symmetric three-pixel frame.</summary>
	[Fact]
	public void GrowsTheWindowByTheFrameSoTheDrawingAreaCoversTheScreen()
	{
		var fitted = OverlayDrawingArea.Fit(
			window: new PixelRect(0, 0, 1920, 1080),
			drawingWidth: 1914,
			drawingHeight: 1074,
			drawingOrigin: (3, 3),
			target: Screen);

		Assert.Equal(new PixelRect(-3, -3, 1926, 1086), fitted);
	}

	/// <summary>
	/// The whole point of the exercise: whatever comes back, the drawing area inside it has to
	/// land on the screen exactly. Checked as the property rather than as a fixed answer, so a
	/// change to the arithmetic cannot pass by matching a number that was itself wrong.
	/// </summary>
	[Theory]
	[InlineData(3, 3, 3, 3)]      // symmetric, the common case
	[InlineData(8, 0, 8, 31)]     // a left border and a title bar, nothing on the right
	[InlineData(0, 0, 6, 6)]      // the whole frame on the right and bottom
	[InlineData(1, 2, 4, 9)]      // lopsided in both directions
	public void TheDrawingAreaLandsOnTheScreenWhateverTheFrameLooksLike(int insetLeft, int insetTop, int extraWidth, int extraHeight)
	{
		var window = new PixelRect(0, 0, 1920, 1080);
		int drawingWidth = window.Width - extraWidth;
		int drawingHeight = window.Height - extraHeight;

		var fitted = OverlayDrawingArea.Fit(
			window,
			drawingWidth,
			drawingHeight,
			(window.Left + insetLeft, window.Top + insetTop),
			Screen);

		Assert.NotNull(fitted);
		// Where the drawing area ends up once the window is placed where Fit asked.
		Assert.Equal(Screen.Left, fitted!.Value.Left + insetLeft);
		Assert.Equal(Screen.Top, fitted.Value.Top + insetTop);
		Assert.Equal(Screen.Width, fitted.Value.Width - extraWidth);
		Assert.Equal(Screen.Height, fitted.Value.Height - extraHeight);
	}

	/// <summary>
	/// A borderless window is already right, so it must be left exactly where it is. Nudging it
	/// would push its content off the screen to correct a frame that is not there.
	/// </summary>
	[Fact]
	public void LeavesAWindowWithNoFrameAlone()
	{
		var fitted = OverlayDrawingArea.Fit(
			window: new PixelRect(0, 0, 1920, 1080),
			drawingWidth: 1920,
			drawingHeight: 1080,
			drawingOrigin: (0, 0),
			target: Screen);

		Assert.Null(fitted);
	}

	/// <summary>A window that has not been laid out reports no drawing area, which is not a frame.</summary>
	[Theory]
	[InlineData(0, 0)]
	[InlineData(0, 1074)]
	[InlineData(1914, 0)]
	[InlineData(-1, -1)]
	public void DeclinesWhenTheDrawingAreaHasNoSize(int drawingWidth, int drawingHeight)
	{
		var fitted = OverlayDrawingArea.Fit(
			window: new PixelRect(0, 0, 1920, 1080),
			drawingWidth,
			drawingHeight,
			drawingOrigin: (3, 3),
			target: Screen);

		Assert.Null(fitted);
	}

	/// <summary>
	/// A drawing area bigger than its own window is a bad reading, not a frame. Acting on it
	/// would shrink the window and make the blur worse than doing nothing.
	/// </summary>
	[Fact]
	public void DeclinesWhenTheDrawingAreaIsLargerThanTheWindow()
	{
		var fitted = OverlayDrawingArea.Fit(
			window: new PixelRect(0, 0, 1920, 1080),
			drawingWidth: 1930,
			drawingHeight: 1090,
			drawingOrigin: (0, 0),
			target: Screen);

		Assert.Null(fitted);
	}

	/// <summary>
	/// The two rectangles are read one after the other, so a window moving in between can
	/// produce a pair that do not describe the same window. An origin outside its own frame is
	/// how that shows up.
	/// </summary>
	[Theory]
	[InlineData(-4, 3)]   // drawing area starts left of the window
	[InlineData(3, -4)]   // and above it
	[InlineData(99, 3)]   // inset wider than the whole frame
	[InlineData(3, 99)]
	public void DeclinesWhenTheDrawingAreaDoesNotSitInsideItsWindow(int insetLeft, int insetTop)
	{
		var fitted = OverlayDrawingArea.Fit(
			window: new PixelRect(0, 0, 1920, 1080),
			drawingWidth: 1914,
			drawingHeight: 1074,
			drawingOrigin: (insetLeft, insetTop),
			target: Screen);

		Assert.Null(fitted);
	}

	/// <summary>
	/// The virtual screen does not start at the origin once a second monitor sits left of or
	/// above the primary one, and the target is carried through rather than assumed to be zero.
	/// </summary>
	[Fact]
	public void CarriesAVirtualScreenThatDoesNotStartAtTheOrigin()
	{
		var virtualScreen = new PixelRect(-1920, -120, 3840, 1200);

		var fitted = OverlayDrawingArea.Fit(
			window: new PixelRect(-1920, -120, 3840, 1200),
			drawingWidth: 3834,
			drawingHeight: 1194,
			drawingOrigin: (-1917, -117),
			target: virtualScreen);

		Assert.Equal(new PixelRect(-1923, -123, 3846, 1206), fitted);
	}

	/// <summary>
	/// A thicker frame is what a higher display scale looks like here, since everything is in
	/// physical pixels. Measuring rather than assuming is what makes that work, so the answer
	/// has to track the frame it is given.
	/// </summary>
	[Fact]
	public void FollowsAThickerFrameRatherThanAssumingThreePixels()
	{
		var fitted = OverlayDrawingArea.Fit(
			window: new PixelRect(0, 0, 1920, 1080),
			drawingWidth: 1911,
			drawingHeight: 1071,
			drawingOrigin: (4, 4),
			target: Screen);

		Assert.Equal(new PixelRect(-4, -4, 1929, 1089), fitted);
	}
}
