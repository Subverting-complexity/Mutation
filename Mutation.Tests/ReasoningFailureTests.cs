using CognitiveSupport;

namespace Mutation.Tests;

public class ReasoningFailureTests
{
	[Theory]
	[InlineData(400, "adaptive thinking is not supported on this model")]
	[InlineData(400, "output_config.effort: 'max' is not supported for this model")]
	[InlineData(400, "thinking.type: Input should be 'enabled' or 'disabled'")]
	[InlineData(400, "max_tokens must be greater than thinking.budget_tokens")]
	[InlineData(400, "Extra inputs are not permitted: output_config")]
	[InlineData(403, "This organization does not have access to the effort parameter")]
	[InlineData(422, "Unsupported value for reasoning")]
	public void ClientErrorNamingTheReasoningSetting_IsTheCause(int status, string message) =>
		Assert.True(ReasoningFailure.IsCause(status, message));

	[Theory]
	[InlineData(400, "max_tokens must be greater than 0")]
	[InlineData(400, "speed is not supported for model claude-opus-4-7")]
	[InlineData(400, "messages: text content blocks must be non-empty")]
	[InlineData(400, "")]
	[InlineData(400, null)]
	public void ClientErrorNotNamingTheReasoningSetting_IsNotTheCause(int status, string? message) =>
		Assert.False(ReasoningFailure.IsCause(status, message));

	[Theory]
	[InlineData(401)] // a bad API key fails the same way with or without a level
	[InlineData(429)]
	[InlineData(500)]
	[InlineData(529)]
	[InlineData(200)]
	public void StatusOutsideTheRefusalRange_IsNeverTheCause(int status) =>
		Assert.False(ReasoningFailure.IsCause(status, "thinking is not supported"));
}
