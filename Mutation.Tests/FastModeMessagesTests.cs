using CognitiveSupport;

namespace Mutation.Tests;

public class FastModeMessagesTests
{
	[Fact]
	public void Describe_Unavailable_NamesBothHumanActions()
	{
		string message = FastModeMessages.Describe(FastModeFallbackReason.Unavailable);

		Assert.Contains("standard speed", message);
		Assert.Contains("research-preview access", message);
		Assert.Contains("turn Fast mode off", message);
	}

	[Fact]
	public void Describe_Busy_SaysWaitRatherThanRequestAccess()
	{
		string message = FastModeMessages.Describe(FastModeFallbackReason.Busy);

		Assert.Contains("capacity", message);
		Assert.DoesNotContain("research-preview access", message);
	}

	[Fact]
	public void Describe_ReasonsProduceDifferentWording()
	{
		Assert.NotEqual(
			FastModeMessages.Describe(FastModeFallbackReason.Unavailable),
			FastModeMessages.Describe(FastModeFallbackReason.Busy));
	}

	[Fact]
	public void AppendTo_NoReason_LeavesTheOutcomeExactlyAsItWas()
	{
		Assert.Equal("Transcript ready and copied.", FastModeMessages.AppendTo("Transcript ready and copied.", null));
	}

	[Fact]
	public void AppendTo_KeepsTheOutcomeAndAddsTheNotice()
	{
		// The confirmation that the text landed must survive; announcing the notice
		// separately would supersede it on the status channel.
		string composed = FastModeMessages.AppendTo("Transcript ready and copied.", FastModeFallbackReason.Unavailable);

		Assert.StartsWith("Transcript ready and copied.", composed);
		Assert.Contains(FastModeMessages.Unavailable, composed);
	}

	[Fact]
	public void AppendTo_EmptyOutcome_YieldsTheNoticeAlone()
	{
		Assert.Equal(FastModeMessages.Busy, FastModeMessages.AppendTo("  ", FastModeFallbackReason.Busy));
	}

	[Fact]
	public void DescribeForLog_KeepsTheProvidersOwnText()
	{
		var fallback = new FastModeFallback(FastModeFallbackReason.Unavailable, "  speed is not permitted  ");

		string logLine = FastModeMessages.DescribeForLog(fallback);

		Assert.Contains("speed is not permitted", logLine);
		Assert.Contains("Unavailable", logLine);
	}

	[Fact]
	public void DescribeForLog_TolerantOfAnEmptyProviderMessage()
	{
		var fallback = new FastModeFallback(FastModeFallbackReason.Busy, "   ");

		Assert.Contains("(no message)", FastModeMessages.DescribeForLog(fallback));
	}
}
