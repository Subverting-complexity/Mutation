using Mutation.Ui.Services;

namespace Mutation.Tests;

/// <summary>
/// The coordinate arithmetic behind injected mouse movement (issue #377).
///
/// <para>
/// Injected absolute movement is not expressed in pixels. Windows takes a position on a grid of
/// 65536 steps stretched across the virtual desktop, so every move has to be converted — and it
/// then converts the value back into a pixel by a rule it does not document. Two conventions are
/// in circulation and they disagree at the edges.
/// </para>
///
/// <para>
/// So these tests pin the properties that hold whichever rule Windows applies, rather than the
/// exact numbers a formula happens to produce. The one that carries the feature is that
/// neighbouring pixels never share a grid step: if they did, a one-pixel wiggle would go out as
/// an event saying the mouse had not moved, and a magnifier filtering exactly that would ignore
/// the whole thing. The pointer would still be placed correctly, so nothing would look wrong —
/// the wiggle would simply never work, for some pointer positions and not others.
/// </para>
/// </summary>
public class MouseInputTests
{
	private const int Width = 1920;

	/// <summary>
	/// The conversion back to a pixel most widely reported for Windows: multiply by the width and
	/// take the high word. Used to check that a grid step lands on the pixel it was meant for
	/// rather than the one before it.
	/// </summary>
	private static int ToPixel(int step, int origin, int extent) => origin + (int)(((long)step * extent) >> 16);

	[Fact]
	public void NeighbouringPixelsNeverShareAGridStep()
	{
		// The property the wiggle depends on. A shared step means an event that reports no
		// movement at all.
		int previous = -1;
		for (int x = 0; x < Width; x++)
		{
			Assert.True(MouseInput.TryNormaliseAxis(x, 0, Width, out int step));
			Assert.NotEqual(previous, step);
			previous = step;
		}
	}

	[Fact]
	public void EveryPixelConvertsBackToItself()
	{
		// Aiming at the middle of the pixel rather than at its leading edge is what makes this
		// hold at both ends, where rounding used to fall on the wrong side of the boundary.
		for (int x = 0; x < Width; x++)
		{
			Assert.True(MouseInput.TryNormaliseAxis(x, 0, Width, out int step));
			Assert.Equal(x, ToPixel(step, 0, Width));
		}
	}

	[Fact]
	public void EveryStepStaysOnTheGrid()
	{
		Assert.True(MouseInput.TryNormaliseAxis(0, 0, Width, out int first));
		Assert.True(MouseInput.TryNormaliseAxis(Width - 1, 0, Width, out int last));

		Assert.InRange(first, 0, 65535);
		Assert.InRange(last, 0, 65535);
	}

	[Fact]
	public void APositionPastTheEdgeIsHeldOnTheGrid()
	{
		// The wiggle asks for a position beyond the edge on purpose, to find out whether the
		// pointer can go that way at all. Windows confines the pointer either way; a coordinate
		// off the grid would only muddle whatever is reading the event.
		Assert.True(MouseInput.TryNormaliseAxis(Width + 500, 0, Width, out int past));
		Assert.True(MouseInput.TryNormaliseAxis(-500, 0, Width, out int before));

		Assert.Equal(65535, past);
		Assert.Equal(0, before);
	}

	[Fact]
	public void AMonitorLeftOfThePrimaryIsMeasuredFromTheVirtualOrigin()
	{
		// The virtual screen starts at a negative x when a second monitor sits to the left, and a
		// position has to be measured from there rather than from zero.
		const int origin = -1920;
		const int extent = 3840;

		Assert.True(MouseInput.TryNormaliseAxis(origin, origin, extent, out int atTheFarLeft));
		Assert.True(MouseInput.TryNormaliseAxis(origin + extent - 1, origin, extent, out int atTheFarRight));

		Assert.Equal(origin, ToPixel(atTheFarLeft, origin, extent));
		Assert.Equal(origin + extent - 1, ToPixel(atTheFarRight, origin, extent));
	}

	[Theory]
	[InlineData(1)]
	[InlineData(0)]
	[InlineData(-100)]
	public void AVirtualScreenWithNoRoomToMoveIsRefused(int extent)
	{
		// Nothing to divide by, and nowhere for the pointer to go.
		Assert.False(MouseInput.TryNormaliseAxis(0, 0, extent, out _));
	}
}
