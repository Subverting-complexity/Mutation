using Mutation.Ui.Views;

namespace Mutation.Tests;

// Where the prompt editor opens. It used to be a fixed 600 by 600 pixels, which hid the
// prompt instructions box on a scaled display (issue #441).
public class ModalWindowPlacementTests
{
	[Fact]
	public void Centred_TakesHalfTheWidthAndHeight()
	{
		var (_, _, width, height) = ModalWindowPlacement.Centred(0, 0, 1920, 1040);

		Assert.Equal(960, width);
		Assert.Equal(520, height);
	}

	[Fact]
	public void Centred_CentresInTheWorkArea()
	{
		var (x, y, _, _) = ModalWindowPlacement.Centred(0, 0, 1920, 1040);

		Assert.Equal(480, x);
		Assert.Equal(260, y);
	}

	[Fact]
	public void Centred_OnASecondScreen_OffsetsByThatScreensOrigin()
	{
		// A screen to the left of the primary one has a negative origin.
		var (x, y, width, height) = ModalWindowPlacement.Centred(-2560, 40, 2560, 1400);

		Assert.Equal(-2560 + 640, x);
		Assert.Equal(40 + 350, y);
		Assert.Equal(1280, width);
		Assert.Equal(700, height);
	}

	[Fact]
	public void Centred_OddSizes_StayInsideTheWorkArea()
	{
		var (x, y, width, height) = ModalWindowPlacement.Centred(0, 0, 1001, 701);

		Assert.True(x + width <= 1001);
		Assert.True(y + height <= 701);
	}

	[Fact]
	public void Centred_EmptyWorkArea_GivesNoSize()
	{
		var (_, _, width, height) = ModalWindowPlacement.Centred(10, 10, 0, -5);

		Assert.Equal(0, width);
		Assert.Equal(0, height);
	}
}
