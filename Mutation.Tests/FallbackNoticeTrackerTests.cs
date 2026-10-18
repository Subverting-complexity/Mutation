using CognitiveSupport;
using Mutation.Ui.Services;

namespace Mutation.Tests;

public class FallbackNoticeTrackerTests
{
	[Fact]
	public void ShouldAnnounce_FirstTimeOnly_ForThePromptAndReason()
	{
		var tracker = new FallbackNoticeTracker();

		Assert.True(tracker.ShouldAnnounce(1, FastModeFallbackReason.Unavailable));
		Assert.False(tracker.ShouldAnnounce(1, FastModeFallbackReason.Unavailable));
		Assert.False(tracker.ShouldAnnounce(1, FastModeFallbackReason.Unavailable));
	}

	[Fact]
	public void ShouldAnnounce_TracksEachPromptSeparately()
	{
		var tracker = new FallbackNoticeTracker();

		Assert.True(tracker.ShouldAnnounce(1, FastModeFallbackReason.Unavailable));
		Assert.True(tracker.ShouldAnnounce(2, FastModeFallbackReason.Unavailable));
		Assert.False(tracker.ShouldAnnounce(2, FastModeFallbackReason.Unavailable));
	}

	[Fact]
	public void ShouldAnnounce_ACapacityNoticeIsStillHeardAfterAnAccessNotice()
	{
		// The two need different actions from the user, so the second must not be
		// swallowed just because the first was already announced.
		var tracker = new FallbackNoticeTracker();

		Assert.True(tracker.ShouldAnnounce(1, FastModeFallbackReason.Unavailable));
		Assert.True(tracker.ShouldAnnounce(1, FastModeFallbackReason.Busy));
		Assert.False(tracker.ShouldAnnounce(1, FastModeFallbackReason.Busy));
	}

	[Fact]
	public void ShouldAnnounce_ANewTrackerStartsClear_AsAtSessionStart()
	{
		var previousSession = new FallbackNoticeTracker();
		previousSession.ShouldAnnounce(1, FastModeFallbackReason.Unavailable);

		// Suppression is per app session and never persisted: access can be granted
		// server-side at any time, so a restart must tell the user again.
		Assert.True(new FallbackNoticeTracker().ShouldAnnounce(1, FastModeFallbackReason.Unavailable));
	}

	[Fact]
	public void ShouldAnnounce_AReasoningFallbackIsHeardOncePerPromptModelAndLevel()
	{
		var tracker = new FallbackNoticeTracker();

		Assert.True(tracker.ShouldAnnounce(1, "gpt-5", ReasoningLevel.High));
		Assert.False(tracker.ShouldAnnounce(1, "gpt-5", ReasoningLevel.High));
		Assert.True(tracker.ShouldAnnounce(1, "gpt-5", ReasoningLevel.Max));
		Assert.True(tracker.ShouldAnnounce(2, "gpt-5", ReasoningLevel.High));
	}

	[Fact]
	public void ShouldAnnounce_ASecondModelRefusingTheSameLevelIsHeardToo()
	{
		// The notice names the model; staying quiet about the second one would leave the
		// user believing the prompt now reasons.
		var tracker = new FallbackNoticeTracker();

		Assert.True(tracker.ShouldAnnounce(1, "model-a", ReasoningLevel.ExtraHigh));
		Assert.True(tracker.ShouldAnnounce(1, "model-b", ReasoningLevel.ExtraHigh));
		// Casing does not make a different model, as it does not for the services.
		Assert.False(tracker.ShouldAnnounce(1, "MODEL-B", ReasoningLevel.ExtraHigh));
	}

	[Fact]
	public void ShouldAnnounce_FastModeAndReasoningNoticesDoNotUseEachOtherUp()
	{
		// Both enums have a member with the value 1; the kinds must still be told apart.
		var tracker = new FallbackNoticeTracker();

		Assert.True(tracker.ShouldAnnounce(1, FastModeFallbackReason.Unavailable));
		Assert.True(tracker.ShouldAnnounce(1, "m", ReasoningLevel.Off));
	}

	[Fact]
	public void ClaimReasoningNotice_WordsTheFirstFallback_AndThenStaysQuiet()
	{
		var tracker = new FallbackNoticeTracker();

		Assert.Equal(
			ReasoningMessages.Fallback("gpt-5", ReasoningLevel.High),
			tracker.ClaimReasoningNotice(3, "gpt-5", ReasoningLevel.High));
		Assert.Null(tracker.ClaimReasoningNotice(3, "gpt-5", ReasoningLevel.High));
	}

	[Fact]
	public void ClaimReasoningNotice_IsNull_WhenNothingWasRefused()
	{
		Assert.Null(new FallbackNoticeTracker().ClaimReasoningNotice(3, "gpt-5", null));
	}

	[Fact]
	public void ShouldAnnounce_ExactlyOneCallerWinsUnderConcurrency()
	{
		// Prompts run from hotkey threads as well as the UI thread.
		var tracker = new FallbackNoticeTracker();
		int winners = 0;

		Parallel.For(0, 200, _ =>
		{
			if (tracker.ShouldAnnounce(7, FastModeFallbackReason.Unavailable))
				Interlocked.Increment(ref winners);
		});

		Assert.Equal(1, winners);
	}
}
