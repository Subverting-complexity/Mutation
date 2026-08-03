using CognitiveSupport;

namespace Mutation.Tests;

public class FastModeFailureTests
{
	[Theory]
	[InlineData(429)]
	[InlineData(529)]
	public void IsCapacity_TrueForFastModeRateLimitAndOverload(int status)
	{
		Assert.True(FastModeFailure.IsCapacity(status));
	}

	[Theory]
	[InlineData(500)]
	[InlineData(403)]
	[InlineData(null)]
	public void IsCapacity_FalseForEverythingElse(int? status)
	{
		Assert.False(FastModeFailure.IsCapacity(status));
	}

	[Fact]
	public void IsUnavailable_TrueForForbidden_RegardlessOfMessage()
	{
		Assert.True(FastModeFailure.IsUnavailable(403, "no details"));
	}

	[Theory]
	[InlineData("The speed parameter is not available for this account.")]
	[InlineData("Unknown beta: fast-mode-2026-02-01")]
	[InlineData("Fast mode requires research preview access")]
	public void IsUnavailable_TrueForBadRequestNamingTheFastModeSurface(string message)
	{
		Assert.True(FastModeFailure.IsUnavailable(400, message));
	}

	[Fact]
	public void IsUnavailable_FalseForUnrelatedBadRequest()
	{
		Assert.False(FastModeFailure.IsUnavailable(400, "max_tokens must be greater than 0"));
	}

	[Fact]
	public void IsUnavailable_FalseForUnauthorized_SoABadKeyIsNotMislabelled()
	{
		// Even a 401 whose text happens to mention speed is a credentials problem;
		// retrying at standard speed would fail identically.
		Assert.False(FastModeFailure.IsUnavailable(401, "invalid x-api-key for speed requests"));
	}

	[Theory]
	[InlineData(500)]
	[InlineData(529)]
	[InlineData(200)]
	public void IsUnavailable_FalseForNon4xxStatuses(int status)
	{
		Assert.False(FastModeFailure.IsUnavailable(status, "speed"));
	}

	[Theory]
	[InlineData(null)]
	[InlineData("")]
	[InlineData("   ")]
	[InlineData("something else entirely")]
	public void MentionsFastMode_FalseWhenTheTextDoesNotNameIt(string? message)
	{
		Assert.False(FastModeFailure.MentionsFastMode(message));
	}
}
