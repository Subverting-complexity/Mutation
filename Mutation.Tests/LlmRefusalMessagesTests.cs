using CognitiveSupport;
using Mutation.Ui.Core;

namespace Mutation.Tests;

public class LlmRefusalMessagesTests
{
	[Fact]
	public void RefusalRidesWithTheDeliveryAnnouncement_SoNeitherIsTalkedOver()
	{
		// Status supersedes rather than queues, so a refusal raised on its own during
		// dictation would be wiped by the delivery line that follows it a moment later.
		string composed = LlmRefusalMessages.RefusedThen("Transcript ready.");

		Assert.StartsWith(LlmRefusalException.UserMessage, composed);
		Assert.Contains("basic formatting rules", composed);
		Assert.EndsWith("Transcript ready.", composed);
	}
}
