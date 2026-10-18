using Mutation.Ui.Core;

namespace Mutation.Tests;

public class LlmFailureMessagesTests
{
	[Fact]
	public void FailureRidesWithTheDeliveryAnnouncement_SoNeitherIsTalkedOver()
	{
		string composed = LlmFailureMessages.FailedThen("server error", "Transcript ready.");

		Assert.Equal(
			"The language model step failed: server error. Your transcript has only the basic formatting rules applied. Transcript ready.",
			composed);
	}

	[Theory]
	[InlineData("Anthropic API returned 401 Unauthorized.", "Anthropic API returned 401 Unauthorized")]
	[InlineData("Bad gateway. ", "Bad gateway")]
	[InlineData("No full stop", "No full stop")]
	[InlineData("...", "an unknown error")]
	public void Describe_DropsTheClosingFullStop_SoTheSentenceHasOnlyOne(string message, string expected)
	{
		string described = LlmFailureMessages.Describe(new InvalidOperationException(message));

		Assert.Equal(expected, described);
		Assert.DoesNotContain("..", LlmFailureMessages.FailedThen(described, "Transcript ready."));
	}

	[Fact]
	public void Describe_CallsACancellationATimeout()
	{
		// A cancel the user asked for is caught before this; one that reaches here is every
		// try running out of time, and "The operation was canceled." would blame the user.
		Assert.Equal("the model did not answer in time", LlmFailureMessages.Describe(new TaskCanceledException()));
		Assert.Equal("the model did not answer in time", LlmFailureMessages.Describe(new TimeoutException("late")));
	}
}
