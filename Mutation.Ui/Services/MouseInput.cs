using System;
using System.Runtime.InteropServices;

namespace Mutation.Ui.Services;

/// <summary>
/// Moves the mouse pointer as real input, rather than by placing the cursor.
///
/// <para>
/// The two are not the same thing, and the difference is the whole reason this exists.
/// <c>SetCursorPos</c> puts the cursor somewhere; it does not put a mouse event into the input
/// stream. Assistive tools watch the mouse the way they watch the keyboard — through a low-level
/// hook — so a cursor moved that way has, as far as they are concerned, not been moved at all.
/// A magnifier asked to follow the mouse will sit exactly where it was while the pointer travels
/// across the screen in front of it.
/// </para>
///
/// <para>
/// This repository has met the same distinction on the other device. <see cref="KeyboardInput"/>
/// records it: a shortcut sent with the meaning of a key but not its position looked complete to
/// ordinary applications, and the readers and magnifiers watching the low-level hook let it pass
/// as something that had not really been typed (issue #335). Injected movement is that lesson
/// again, in the mouse.
/// </para>
///
/// <para>
/// The <c>INPUT</c> union comes from <see cref="KeyboardInput"/> rather than being declared again
/// here. That is deliberate: its size is what Windows validates, a second copy could drift from
/// it, and the failure when it does is silent — every call reports success and nothing moves
/// (PR #328).
/// </para>
/// </summary>
internal static class MouseInput
{
	private const uint InputMouse = 0;

	private const uint MouseEventMove = 0x0001;
	private const uint MouseEventAbsolute = 0x8000;

	/// <summary>
	/// Measures the absolute coordinates against the whole virtual desktop rather than the
	/// primary monitor. Without it, a position on a second screen is folded back onto the first.
	/// </summary>
	private const uint MouseEventVirtualDesk = 0x4000;

	private const int SM_XVIRTUALSCREEN = 76;
	private const int SM_YVIRTUALSCREEN = 77;
	private const int SM_CXVIRTUALSCREEN = 78;
	private const int SM_CYVIRTUALSCREEN = 79;

	/// <summary>
	/// The number of steps on the grid absolute mouse input is expressed on, per axis. The
	/// largest value that can be sent is one less than this.
	/// </summary>
	private const long AbsoluteGridSteps = 65536;

	private const long HighestGridStep = AbsoluteGridSteps - 1;

	[DllImport("user32.dll")]
	private static extern int GetSystemMetrics(int nIndex);

	/// <summary>
	/// Reports one mouse movement to <paramref name="position"/> as injected input. Returns
	/// whether Windows accepted the event.
	/// <para>
	/// Where the pointer actually ends up is not this method's business, and must not be. The
	/// absolute form of injected movement is normalised onto a 0 to 65535 grid across the virtual
	/// desktop, so on any ordinary screen several pixels share a grid step and the pointer can
	/// land a pixel from the one asked for. The caller places the pointer exactly afterwards; the
	/// job here is only to make the movement visible to whatever is watching the input stream.
	/// </para>
	/// </summary>
	public static bool TryReportMoveTo(int x, int y)
	{
		try
		{
			int left = GetSystemMetrics(SM_XVIRTUALSCREEN);
			int top = GetSystemMetrics(SM_YVIRTUALSCREEN);
			int width = GetSystemMetrics(SM_CXVIRTUALSCREEN);
			int height = GetSystemMetrics(SM_CYVIRTUALSCREEN);

			if (!TryNormaliseAxis(x, left, width, out int dx) || !TryNormaliseAxis(y, top, height, out int dy))
				return false;

			var input = new KeyboardInput.INPUT
			{
				type = InputMouse,
				U = new KeyboardInput.INPUTUNION
				{
					mi = new KeyboardInput.MOUSEINPUT
					{
						dx = dx,
						dy = dy,
						mouseData = 0,
						dwFlags = MouseEventMove | MouseEventAbsolute | MouseEventVirtualDesk,
						time = 0,
						dwExtraInfo = IntPtr.Zero,
					},
				},
			};

			return KeyboardInput.Send(new[] { input }) == 1;
		}
		catch
		{
			// Injected movement is what makes the wiggle visible to a magnifier, not what makes
			// it happen. A failure here must leave the caller free to move the pointer anyway.
			return false;
		}
	}

	/// <summary>
	/// Maps a screen coordinate onto the normalised grid. Fails on a virtual screen Windows
	/// reports as a single pixel or less, where there is nothing to divide by and nowhere to move.
	///
	/// <para>
	/// It aims at the middle of the target pixel rather than at its leading edge, and that is the
	/// point of it. Windows does not document how it converts a grid step back into a pixel, and
	/// the two conventions in circulation disagree at the edges — one divides the grid by the
	/// number of pixels, the other by one less than that. Aim at a pixel's leading edge and
	/// rounding can fall on the wrong side of the boundary, so the step maps back to the pixel
	/// before the one intended. Aim at the middle and the value is comfortably inside the right
	/// pixel whichever convention is applied.
	/// </para>
	///
	/// <para>
	/// What that buys is the property the whole feature rests on: neighbouring pixels always get
	/// different grid steps, so a wiggle of a single pixel is always reported as movement. Under
	/// the leading-edge form, some positions shared a step with the one beside them, and the
	/// event went out saying the mouse had not moved — for a magnifier filtering exactly that,
	/// an entire wiggle that reported nothing. It also makes the reported position and the placed
	/// position agree, which is what stops the two from fighting over the last pixel.
	/// </para>
	///
	/// <para>
	/// Internal so the arithmetic can be checked without a screen.
	/// </para>
	/// </summary>
	internal static bool TryNormaliseAxis(int value, int origin, int extent, out int normalised)
	{
		normalised = 0;
		if (extent <= 1)
			return false;

		long offset = value - origin;
		// (offset + 0.5) / extent, on the grid, without leaving whole numbers.
		long step = ((2 * offset + 1) * AbsoluteGridSteps) / (2L * extent);
		// The planner asks for positions past the edge of the virtual screen on purpose, to find
		// out whether the pointer can go that way at all. Windows confines the pointer either
		// way; sending a coordinate off the grid to say so would not.
		normalised = (int)Math.Clamp(step, 0, HighestGridStep);
		return true;
	}
}
