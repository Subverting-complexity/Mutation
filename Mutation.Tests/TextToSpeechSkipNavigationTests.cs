using CognitiveSupport;
using Xunit;

namespace Mutation.Tests;

// Verifies the pure sentence-skip decision used by TextToSpeechService.SkipSentence.
// These rules are timing-sensitive and hard to test by ear, so the decision is kept
// side-effect free and exercised here with explicit clock values.
//
// Returned index meaning: -1 => beginning-of-text, lastIndex+1 => end-of-text,
// otherwise the sentence to play.
public class TextToSpeechSkipNavigationTests
{
	private const int Grace = 1500;
	private const int LastIndex = 9; // 10 sentences
	// Matches TextToSpeechService.NoRecentSkipTick: a sentinel far enough in the past
	// that the next press is never treated as part of a burst (without overflow).
	private const long NoRecentSkip = long.MinValue / 2;

	private static int Skip(
		int direction, long now, long lastSkipTick, int pendingSkipIndex,
		int navIndex, long sentenceEnteredAtTick) =>
		TextToSpeechService.ComputeSkipTarget(
			direction, now, lastSkipTick, pendingSkipIndex,
			navIndex, sentenceEnteredAtTick, LastIndex, Grace);

	// ---- Backward: fresh-press (media-player) semantics ----

	[Fact]
	public void FreshBackPress_Settled_RestartsCurrentSentence()
	{
		// Listened to sentence 5 for a while (entered long ago) -> media-player restart.
		int target = Skip(direction: -1, now: 100_000, lastSkipTick: NoRecentSkip,
			pendingSkipIndex: 5, navIndex: 5, sentenceEnteredAtTick: 0);
		Assert.Equal(5, target);
	}

	[Fact]
	public void FreshBackPress_JustEntered_GoesToPreviousSentence()
	{
		// Only just entered sentence 5 (within grace) -> step back to 4.
		int target = Skip(direction: -1, now: 100_000, lastSkipTick: NoRecentSkip,
			pendingSkipIndex: 5, navIndex: 5, sentenceEnteredAtTick: 100_000 - 200);
		Assert.Equal(4, target);
	}

	[Fact]
	public void FreshBackPress_AtFirstSentence_SignalsBeginningOfText()
	{
		// Nothing before the first sentence: a back-press there announces beginning,
		// even when settled (this is what makes "previous up to the start" reachable).
		int target = Skip(direction: -1, now: 100_000, lastSkipTick: NoRecentSkip,
			pendingSkipIndex: 0, navIndex: 0, sentenceEnteredAtTick: 0);
		Assert.Equal(-1, target);
	}

	// ---- Backward: burst (rapid tapping) semantics ----

	[Fact]
	public void BurstBack_MarchesBack_EvenWhilePlaybackAdvancesNavIndex()
	{
		// The core requirement: each tap ~1s apart steps back one more, regardless of
		// playback rolling navIndex forward into later sentences between taps.
		long t1 = 100_000;
		int pending = 5; // after the first press restarted/targeted sentence 5

		int t2 = Skip(direction: -1, now: t1 + 1000, lastSkipTick: t1,
			pendingSkipIndex: pending, navIndex: 8, sentenceEnteredAtTick: t1 + 900);
		Assert.Equal(4, t2);
		pending = t2;

		int t3 = Skip(direction: -1, now: t1 + 2000, lastSkipTick: t1 + 1000,
			pendingSkipIndex: pending, navIndex: 7, sentenceEnteredAtTick: t1 + 1900);
		Assert.Equal(3, t3);
		pending = t3;

		int t4 = Skip(direction: -1, now: t1 + 3000, lastSkipTick: t1 + 2000,
			pendingSkipIndex: pending, navIndex: 9, sentenceEnteredAtTick: t1 + 2900);
		Assert.Equal(2, t4);
	}

	[Fact]
	public void BurstBack_AtFirstSentence_SignalsBeginningOfText()
	{
		long t1 = 100_000;
		int target = Skip(direction: -1, now: t1 + 500, lastSkipTick: t1,
			pendingSkipIndex: 0, navIndex: 0, sentenceEnteredAtTick: t1 + 400);
		Assert.Equal(-1, target);
	}

	[Fact]
	public void GapLongerThanGrace_EndsBurst_FreshSemanticsApply()
	{
		long t1 = 100_000;
		// Next press comes after the grace window -> not a burst. Settled in current
		// sentence (navIndex 6) -> media-player restart of 6, not a step from pending.
		int target = Skip(direction: -1, now: t1 + Grace + 1, lastSkipTick: t1,
			pendingSkipIndex: 3, navIndex: 6, sentenceEnteredAtTick: 0);
		Assert.Equal(6, target);
	}

	[Fact]
	public void FirstBackPress_AfterStart_IsNeverTreatedAsBurst()
	{
		// NoRecentSkip sentinel must not overflow into a false "burst" on the first tap.
		// pending=7 is deliberately far from navIndex=4: a (wrong) burst would yield 6,
		// while correct fresh just-entered semantics yield navIndex-1 = 3.
		int target = Skip(direction: -1, now: 50, lastSkipTick: NoRecentSkip,
			pendingSkipIndex: 7, navIndex: 4, sentenceEnteredAtTick: 0);
		Assert.Equal(3, target);
	}

	// ---- Forward: always steps from the live playback position ----

	[Fact]
	public void Forward_StepsFromLivePlayback()
	{
		int target = Skip(direction: 1, now: 100_000, lastSkipTick: NoRecentSkip,
			pendingSkipIndex: 0, navIndex: 3, sentenceEnteredAtTick: 0);
		Assert.Equal(4, target);
	}

	[Fact]
	public void Forward_InBurst_IgnoresStaleAnchor_UsesLivePlayback()
	{
		// Even within a burst, forward advances from live navIndex, never the frozen
		// anchor — a stale anchor must not drag forward steps backward.
		long t1 = 100_000;
		int target = Skip(direction: 1, now: t1 + 1000, lastSkipTick: t1,
			pendingSkipIndex: 2, navIndex: 5, sentenceEnteredAtTick: t1 + 900);
		Assert.Equal(6, target);
	}

	[Fact]
	public void Forward_AfterListeningPastNavigation_ReachesEndOfText()
	{
		// Regression: navigated to the second-to-last sentence (anchor lags), then
		// listened so playback rolled navIndex onto the last sentence. A forward tap
		// within the grace window must announce end-of-text, NOT re-read the last
		// sentence (the previously reported bug).
		long t1 = 100_000;
		int target = Skip(direction: 1, now: t1 + 1000, lastSkipTick: t1,
			pendingSkipIndex: LastIndex - 1, navIndex: LastIndex, sentenceEnteredAtTick: t1 + 900);
		Assert.Equal(LastIndex + 1, target);
	}

	[Fact]
	public void Forward_OnLastSentence_SignalsEndOfText()
	{
		int target = Skip(direction: 1, now: 100_000, lastSkipTick: NoRecentSkip,
			pendingSkipIndex: LastIndex, navIndex: LastIndex, sentenceEnteredAtTick: 0);
		Assert.Equal(LastIndex + 1, target);
	}
}
