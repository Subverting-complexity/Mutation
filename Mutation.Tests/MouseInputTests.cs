using Mutation.Ui.Services;

namespace Mutation.Tests;

/// <summary>
/// The coordinate arithmetic behind injected mouse movement (issue #377).
/// <para>
/// Injected absolute movement is not expressed in pixels. Windows takes a position on a
/// 0 to 65535 grid stretched across the virtual desktop, so every move has to be converted, and
/// the conversion is the kind that goes wrong by one and stays wrong quietly. The pointer is
/// placed exactly afterwards, which hides a small error — the movement would simply be reported
/// a pixel from where the pointer went, and a magnifier would follow it to the wrong place.
/// </para>
/// </summary>
public class MouseInputTests
{
	[Fact]
	public void TheFirstPixelIsTheStartOfTheGrid()
	{
		Assert.True(MouseInput.TryNormaliseAxis(0, 0, 1920, out int normalised));
		Assert.Equal(0, normalised);
	}

	[Fact]
	public void TheLastPixelIsTheEndOfTheGrid()
	{
		// Dividing by the width rather than the width less one would leave the last column
		// permanently out of reach.
		Assert.True(MouseInput.TryNormaliseAxis(1919, 0, 1920, out int normalised));
		Assert.Equal(65535, normalised);
	}

	[Fact]
	public void TheMiddleLandsInTheMiddle()
	{
		Assert.True(MouseInput.TryNormaliseAxis(960, 0, 1921, out int normalised));
		Assert.Equal(32768, normalised);
	}

	[Fact]
	public void AMonitorLeftOfThePrimaryIsMeasuredFromTheVirtualOrigin()
	{
		// The virtual screen starts at a negative x when a second monitor sits to the left, and
		// the position has to be measured from there rather than from zero.
		Assert.True(MouseInput.TryNormaliseAxis(-1920, -1920, 3840, out int atTheFarLeft));
		Assert.Equal(0, atTheFarLeft);

		Assert.True(MouseInput.TryNormaliseAxis(1919, -1920, 3840, out int atTheFarRight));
		Assert.Equal(65535, atTheFarRight);
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
