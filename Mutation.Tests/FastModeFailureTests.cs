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
	public void Classify_Forbidden_IsMissingAccess()
	{
		Assert.Equal(FastModeFallbackReason.Unavailable, FastModeFailure.Classify(403, "no details"));
	}

	[Theory]
	[InlineData("The speed parameter is not available for this account.")]
	[InlineData("Unknown beta: fast-mode-2026-02-01")]
	[InlineData("Fast mode requires research preview access")]
	public void Classify_BadRequestNamingTheFastModeSurface_IsMissingAccess(string message)
	{
		Assert.Equal(FastModeFallbackReason.Unavailable, FastModeFailure.Classify(400, message));
	}

	[Theory]
	// Anthropic phrasing for a model that errors on Fast mode (e.g. Opus 4.7).
	[InlineData(400, "speed is not supported for model claude-opus-4-7")]
	[InlineData(400, "Model claude-opus-4-7 does not support fast mode.")]
	[InlineData(400, "Unsupported value for speed.")]
	// OpenAI phrasing for a rejected service tier. The SDK surfaces the whole response
	// body, so the offending parameter name travels with the message.
	[InlineData(400, "Invalid value: 'fast'. Supported values are: 'auto', 'default', 'flex' and 'priority'. (param: service_tier)")]
	// A 403 that still names the model as the problem must not become "request access".
	[InlineData(403, "fast mode is not supported for this model")]
	public void Classify_ProviderBlamesTheModel_IsUnsupportedModelNotMissingAccess(int status, string message)
	{
		// Telling someone to go join a research-preview waitlist when the real fix is
		// "pick another model" sends them off to solve a problem they do not have.
		Assert.Equal(FastModeFallbackReason.UnsupportedModel, FastModeFailure.Classify(status, message));
	}

	[Fact]
	public void Classify_UnrelatedBadRequest_IsNotFastModesDoing()
	{
		Assert.Null(FastModeFailure.Classify(400, "max_tokens must be greater than 0"));
	}

	[Fact]
	public void Classify_Unauthorized_IsNeverFastModesDoing()
	{
		// Even a 401 whose text happens to mention speed is a credentials problem;
		// retrying at standard speed would fail identically.
		Assert.Null(FastModeFailure.Classify(401, "invalid x-api-key for speed requests"));
	}

	[Theory]
	[InlineData(500)]
	[InlineData(529)]
	[InlineData(200)]
	public void Classify_NullForNon4xxStatuses(int status)
	{
		Assert.Null(FastModeFailure.Classify(status, "speed"));
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

	[Theory]
	[InlineData("service_tier is not valid")]
	[InlineData("unknown service tier")]
	public void MentionsFastMode_RecognisesTheOpenAiServiceTierSurfaceToo(string message)
	{
		Assert.True(FastModeFailure.MentionsFastMode(message));
	}
}
