using Mutation.Ui.Core;

namespace Mutation.Tests;

// Issue #393: a rectangle drawn tightly around a word produced a picture with the last letter
// missing and the one before it sliced through. Measured on the reported capture — 61 pixels
// wide, holding "Ther" with the r cut off at the edge — the crop kept roughly 60% to 80% of
// the width asked for, on every capture, on both the plain screenshot path and the OCR one.
//
// These tests pin the two rules that answer it: a corner read in the screenshot's own units
// needs no conversion at all, and a corner that does need converting rounds outward, never in.
public class RegionCropGeometryTests
{
	private const int ScreenWidth = 1920;
	private const int ScreenHeight = 1080;

	// The screenshot is in virtual-screen pixels and so is the pointer, so the answer is a
	// subtraction. No scale can be got wrong because no scale is applied.
	[Fact]
	public void AScreenPixelSelectionIsTheDistanceBetweenTheCornersPlusBothEnds()
	{
		var crop = RegionCropGeometry.FromScreenPixels(
			new CursorPoint(100, 200),
			new CursorPoint(180, 240),
			virtualLeft: 0,
			virtualTop: 0,
			ScreenWidth,
			ScreenHeight);

		// 100 through 180 inclusive is 81 pixels, not 80. The pixel under the pointer belongs
		// to the selection at both ends; taking 80 is exactly how the last column went missing.
		Assert.Equal(new PixelRect(100, 200, 81, 41), crop);
	}

	// A second monitor to the left or above puts the virtual screen's origin in negative
	// numbers, and pixel zero of the screenshot is that origin, not zero.
	[Fact]
	public void ScreenPixelsAreMeasuredFromTheVirtualScreensOwnOrigin()
	{
		var crop = RegionCropGeometry.FromScreenPixels(
			new CursorPoint(-1820, -100),
			new CursorPoint(-1780, -60),
			virtualLeft: -1920,
			virtualTop: -200,
			ScreenWidth,
			ScreenHeight);

		Assert.Equal(new PixelRect(100, 100, 41, 41), crop);
	}

	[Fact]
	public void DraggingRightToLeftAndBottomToTopGivesTheSameRectangle()
	{
		var downThenUp = RegionCropGeometry.FromScreenPixels(
			new CursorPoint(100, 200), new CursorPoint(180, 240), 0, 0, ScreenWidth, ScreenHeight);
		var upThenDown = RegionCropGeometry.FromScreenPixels(
			new CursorPoint(180, 240), new CursorPoint(100, 200), 0, 0, ScreenWidth, ScreenHeight);

		Assert.Equal(downThenUp, upThenDown);
	}

	// A click that does not move is still a capture. Handing back nothing would be reported to
	// the user as a cancelled one, which is not what happened.
	[Fact]
	public void AClickThatDoesNotMoveStillYieldsOnePixel()
	{
		var crop = RegionCropGeometry.FromScreenPixels(
			new CursorPoint(500, 500), new CursorPoint(500, 500), 0, 0, ScreenWidth, ScreenHeight);

		Assert.Equal(new PixelRect(500, 500, 1, 1), crop);
	}

	[Fact]
	public void ASelectionRunningOffTheEdgeIsBroughtBackInside()
	{
		var crop = RegionCropGeometry.FromScreenPixels(
			new CursorPoint(-500, -500),
			new CursorPoint(ScreenWidth + 500, ScreenHeight + 500),
			0, 0, ScreenWidth, ScreenHeight);

		Assert.Equal(new PixelRect(0, 0, ScreenWidth, ScreenHeight), crop);
	}

	// The drawing-area route, which the keyboard path uses and the mouse path falls back to.
	// At 100% the two coordinate spaces are the same, so the numbers pass straight through.
	[Fact]
	public void AtOneHundredPercentTheDrawingAreaRouteChangesNothing()
	{
		var crop = RegionCropGeometry.FromDrawingArea(
			100, 200, 180, 240,
			drawingWidth: ScreenWidth, drawingHeight: ScreenHeight,
			ScreenWidth, ScreenHeight);

		Assert.Equal(new PixelRect(100, 200, 80, 40), crop);
	}

	// This is the rule the old code broke. It rounded the corner and the length separately and
	// to nearest, so each was allowed its own half-pixel of error and they did not have to
	// cancel. Both edges are converted here and the far one rounds up, so the answer can only
	// ever be generous.
	[Theory]
	[InlineData(1.25)]
	[InlineData(1.5)]
	[InlineData(1.75)]
	[InlineData(2.0)]
	public void AConvertedEdgeNeverLandsInsideWhereItBelongs(double scale)
	{
		double drawingWidth = ScreenWidth / scale;
		double drawingHeight = ScreenHeight / scale;

		// Deliberately awkward corners: every edge falls between two pixels at every scale.
		const double firstX = 100.37, firstY = 200.61, secondX = 180.42, secondY = 240.83;

		var crop = RegionCropGeometry.FromDrawingArea(
			firstX, firstY, secondX, secondY,
			drawingWidth, drawingHeight,
			ScreenWidth, ScreenHeight);

		Assert.True(crop.Left <= firstX * scale, $"left {crop.Left} cut into the selection");
		Assert.True(crop.Top <= firstY * scale, $"top {crop.Top} cut into the selection");
		Assert.True(crop.Left + crop.Width >= secondX * scale, $"right {crop.Left + crop.Width} cut into the selection");
		Assert.True(crop.Top + crop.Height >= secondY * scale, $"bottom {crop.Top + crop.Height} cut into the selection");
	}

	// The specific regression: rounding a corner and a length apart could take a pixel off the
	// far edge. Converting both edges cannot, whatever the corners are.
	[Theory]
	[InlineData(1.25)]
	[InlineData(1.5)]
	public void TheFarEdgeIsNeverShorterThanRoundingBothEdgesWouldGive(double scale)
	{
		double drawingWidth = ScreenWidth / scale;

		for (int tenths = 0; tenths < 40; tenths++)
		{
			double firstX = 100 + tenths * 0.1;
			double secondX = firstX + 37.45;

			var crop = RegionCropGeometry.FromDrawingArea(
				firstX, 0, secondX, 10,
				drawingWidth, ScreenHeight / scale,
				ScreenWidth, ScreenHeight);

			Assert.True(
				crop.Left + crop.Width >= (int)Math.Ceiling(secondX * scale),
				$"at {firstX:0.0} the right edge stopped at {crop.Left + crop.Width}");
		}
	}

	[Fact]
	public void ADrawingAreaSelectionThatDoesNotMoveStillYieldsOnePixel()
	{
		var crop = RegionCropGeometry.FromDrawingArea(
			400, 400, 400, 400,
			drawingWidth: 1280, drawingHeight: 720,
			ScreenWidth, ScreenHeight);

		Assert.Equal(1, crop.Width);
		Assert.Equal(1, crop.Height);
	}

	// Nothing has been captured yet, so there is no pixel to hand back and none is invented.
	[Fact]
	public void NoBitmapMeansNoRectangle()
	{
		var crop = RegionCropGeometry.FromDrawingArea(10, 10, 50, 50, 1280, 720, 0, 0);

		Assert.Equal(new PixelRect(0, 0, 0, 0), crop);
	}

	// One pixel of daylight between the two routes is ordinary: one counts both end pixels and
	// the other rounds a fractional edge outward. More than that is the signal worth logging.
	[Fact]
	public void SmallDifferencesBetweenTheTwoRoutesAreNotADisagreement()
	{
		var fromScreen = new PixelRect(100, 200, 81, 41);
		var fromDrawingArea = new PixelRect(100, 200, 80, 40);

		Assert.True(RegionCropGeometry.Agree(fromScreen, fromDrawingArea));
	}

	// The reported fault, as the log would have seen it: the drawing-area route keeping about
	// four fifths of what the pointer says was selected.
	[Fact]
	public void AMissingScaleFactorIsADisagreement()
	{
		var fromScreen = new PixelRect(100, 200, 77, 54);
		var fromDrawingArea = new PixelRect(100, 200, 61, 43);

		Assert.False(RegionCropGeometry.Agree(fromScreen, fromDrawingArea));
	}
}
