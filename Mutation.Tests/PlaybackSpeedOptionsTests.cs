using CognitiveSupport;

namespace Mutation.Tests;

public class PlaybackSpeedOptionsTests
{
	[Fact]
	public void Speeds_AreTheExpectedMultipliers()
	{
		Assert.Equal(
			new[] { 0.5, 0.75, 1.0, 1.25, 1.5, 1.75, 2.0, 2.25, 2.5, 2.75, 3.0 },
			PlaybackSpeedOptions.Speeds);
	}

	[Fact]
	public void Default_IsNormalSpeedAndIsAnOption()
	{
		Assert.Equal(1.0, PlaybackSpeedOptions.Default);
		Assert.Contains(PlaybackSpeedOptions.Default, PlaybackSpeedOptions.Speeds);
	}

	[Theory]
	[InlineData(0.5, 0.5)]
	[InlineData(1.0, 1.0)]
	[InlineData(3.0, 3.0)]
	[InlineData(1.25, 1.25)]
	public void Normalize_ReturnsExactValueWhenAlreadyAllowed(double input, double expected)
	{
		Assert.Equal(expected, PlaybackSpeedOptions.Normalize(input));
	}

	[Theory]
	[InlineData(0.6, 0.5)]   // closer to 0.5 than 0.75
	[InlineData(0.7, 0.75)]  // closer to 0.75
	[InlineData(1.1, 1.0)]   // closer to 1.0 than 1.25
	public void Normalize_SnapsToNearestAllowedValue(double input, double expected)
	{
		Assert.Equal(expected, PlaybackSpeedOptions.Normalize(input));
	}

	[Theory]
	[InlineData(0.1)]
	[InlineData(0.0)]
	[InlineData(-5.0)]
	public void Normalize_ClampsBelowRangeToSlowest(double input)
	{
		Assert.Equal(0.5, PlaybackSpeedOptions.Normalize(input));
	}

	[Theory]
	[InlineData(3.5)]
	[InlineData(10.0)]
	public void Normalize_ClampsAboveRangeToFastest(double input)
	{
		Assert.Equal(3.0, PlaybackSpeedOptions.Normalize(input));
	}
}
