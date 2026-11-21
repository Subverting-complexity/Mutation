using System;
using CognitiveSupport;
using Xunit;

namespace Mutation.Tests;

public class IntervalTimerTextTests
{
	[Fact]
	public void Off_WhenThereIsNoCountdown()
	{
		Assert.Equal("off", IntervalTimerText.Countdown(null));
	}

	[Theory]
	[InlineData(45.0, "next in 45 seconds")]
	[InlineData(1.0, "next in 1 second")]
	[InlineData(0.2, "next in 1 second")]
	[InlineData(0.0, "next in 1 second")]
	[InlineData(59.0, "next in 59 seconds")]
	[InlineData(59.5, "next in 1 minute")]
	[InlineData(60.0, "next in 1 minute")]
	[InlineData(61.0, "next in 2 minutes")]
	[InlineData(250.0, "next in 5 minutes")]       // 4 min 10 s rounds up
	[InlineData(299.0, "next in 5 minutes")]
	[InlineData(3540.0, "next in 59 minutes")]
	[InlineData(3570.0, "next in 1 hour")]         // 59 min 30 s rounds up to the hour
	[InlineData(3600.0, "next in 1 hour")]
	[InlineData(3601.0, "next in 1 hour and 1 minute")]
	[InlineData(4320.0, "next in 1 hour and 12 minutes")]
	[InlineData(7200.0, "next in 2 hours")]
	[InlineData(86400.0, "next in 24 hours")]
	public void Countdown_Wording(double seconds, string expected)
	{
		Assert.Equal(expected, IntervalTimerText.Countdown(TimeSpan.FromSeconds(seconds)));
	}

	[Theory]
	[InlineData("Stretch", "Stretch timer")]
	[InlineData("  Stretch  ", "Stretch timer")]
	[InlineData("Tea timer", "Tea timer")]
	[InlineData("Timer 2", "Timer 2 timer")]
	[InlineData("", "Timer")]
	[InlineData(null, "Timer")]
	public void AccessibleName(string? name, string expected)
	{
		Assert.Equal(expected, IntervalTimerText.AccessibleName(name));
	}

	[Fact]
	public void NextDefaultName_StartsAtOne()
	{
		Assert.Equal("Timer 1", IntervalTimerText.NextDefaultName(Array.Empty<string>()));
	}

	[Fact]
	public void NextDefaultName_SkipsNamesInUse_IgnoringCase()
	{
		Assert.Equal("Timer 3", IntervalTimerText.NextDefaultName(new[] { "timer 1", "Stretch", "Timer 2", null }));
	}

	[Fact]
	public void NextDefaultName_FillsAGap()
	{
		Assert.Equal("Timer 2", IntervalTimerText.NextDefaultName(new[] { "Timer 1", "Timer 3" }));
	}
}
