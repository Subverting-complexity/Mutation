using CognitiveSupport;
using Mutation.Ui.Services;

namespace Mutation.Tests;

public class FastModeNoticeTrackerTests
{
	[Fact]
	public void ShouldAnnounce_FirstTimeOnly_ForThePromptAndReason()
	{
		var tracker = new FastModeNoticeTracker();

		Assert.True(tracker.ShouldAnnounce(1, FastModeFallbackReason.Unavailable));
		Assert.False(tracker.ShouldAnnounce(1, FastModeFallbackReason.Unavailable));
		Assert.False(tracker.ShouldAnnounce(1, FastModeFallbackReason.Unavailable));
	}

	[Fact]
	public void ShouldAnnounce_TracksEachPromptSeparately()
	{
		var tracker = new FastModeNoticeTracker();

		Assert.True(tracker.ShouldAnnounce(1, FastModeFallbackReason.Unavailable));
		Assert.True(tracker.ShouldAnnounce(2, FastModeFallbackReason.Unavailable));
		Assert.False(tracker.ShouldAnnounce(2, FastModeFallbackReason.Unavailable));
	}

	[Fact]
	public void ShouldAnnounce_ACapacityNoticeIsStillHeardAfterAnAccessNotice()
	{
		// The two need different actions from the user, so the second must not be
		// swallowed just because the first was already announced.
		var tracker = new FastModeNoticeTracker();

		Assert.True(tracker.ShouldAnnounce(1, FastModeFallbackReason.Unavailable));
		Assert.True(tracker.ShouldAnnounce(1, FastModeFallbackReason.Busy));
		Assert.False(tracker.ShouldAnnounce(1, FastModeFallbackReason.Busy));
	}

	[Fact]
	public void Reset_MakesTheNoticeAnnounceableAgain()
	{
		var tracker = new FastModeNoticeTracker();
		tracker.ShouldAnnounce(1, FastModeFallbackReason.Unavailable);

		tracker.Reset();

		Assert.True(tracker.ShouldAnnounce(1, FastModeFallbackReason.Unavailable));
	}

	[Fact]
	public void ShouldAnnounce_ExactlyOneCallerWinsUnderConcurrency()
	{
		// Prompts run from hotkey threads as well as the UI thread.
		var tracker = new FastModeNoticeTracker();
		int winners = 0;

		Parallel.For(0, 200, _ =>
		{
			if (tracker.ShouldAnnounce(7, FastModeFallbackReason.Unavailable))
				Interlocked.Increment(ref winners);
		});

		Assert.Equal(1, winners);
	}
}
