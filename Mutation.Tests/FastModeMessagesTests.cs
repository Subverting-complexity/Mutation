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
