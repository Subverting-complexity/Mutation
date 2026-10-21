using System;

namespace Mutation.Ui.Views;

/// <summary>
/// Where a modal editor window opens: half the width and half the height of the work area
/// it opens on, centred in it. Pure arithmetic in physical pixels, the unit both
/// <c>DisplayArea.WorkArea</c> and <c>AppWindow.MoveAndResize</c> use, so it can be tested
/// without a screen.
/// <para>
/// A fraction of the screen rather than a fixed size. The prompt editor used to open at
/// 600 by 600 pixels, which on a scaled display left its settings filling the window and the
/// prompt instructions box squeezed out of sight (issue #441).
/// </para>
/// </summary>
internal static class ModalWindowPlacement
{
	/// <summary>The share of the work area's width and height the window takes.</summary>
	public const double ScreenFraction = 0.5;

	/// <param name="workX">Left edge of the work area, in virtual-screen pixels.</param>
	/// <param name="workY">Top edge of the work area, in virtual-screen pixels.</param>
	/// <param name="workWidth">Width of the work area. Zero or less gives a zero-size result.</param>
	/// <param name="workHeight">Height of the work area. Zero or less gives a zero-size result.</param>
	public static (int X, int Y, int Width, int Height) Centred(int workX, int workY, int workWidth, int workHeight)
	{
		int width = Math.Max(0, (int)(workWidth * ScreenFraction));
		int height = Math.Max(0, (int)(workHeight * ScreenFraction));
		int x = workX + (Math.Max(0, workWidth) - width) / 2;
		int y = workY + (Math.Max(0, workHeight) - height) / 2;
		return (x, y, width, height);
	}
}
