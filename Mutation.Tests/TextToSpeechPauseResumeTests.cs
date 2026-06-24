using CognitiveSupport;
using Xunit;

namespace Mutation.Tests;

// Verifies the pure pause/resume decisions used by TextToSpeechService.Resume, plus the
// no-op guards on the Pause/Resume entry points. The threshold logic is timing-sensitive
// and hard to test by ear, so the decision is kept side-effect free and exercised here
// with explicit elapsed-time values, no real waits — the same approach used for the
// sentence-skip and word-rewind decisions.
public class TextToSpeechPauseResumeTests
{
	private const string Text = "The quick brown fox jumps over the lazy dog";
	// Word starts: The=0 quick=4 brown=10 fox=16 jumps=20 over=26 the=31 lazy=35 dog=40

	// ---- ShouldRewindAfterPause: the time-threshold gate ----

	[Fact]
	public void WithinThreshold_DoesNotRewind()
	{
		// Paused 3s, threshold 10s -> resume seamlessly from the exact word.
		Assert.False(TextToSpeechService.ShouldRewindAfterPause(3_000, 10));
	}

	[Fact]
	public void ExactlyAtThreshold_DoesNotRewind()
	{
		// "at or below the threshold" resumes without rewinding.
		Assert.False(TextToSpeechService.ShouldRewindAfterPause(10_000, 10));
	}

	[Fact]
	public void BeyondThreshold_Rewinds()
	{
		// One millisecond past the threshold -> rewind for context.
		Assert.True(TextToSpeechService.ShouldRewindAfterPause(10_001, 10));
	}

	[Fact]
	public void ZeroThreshold_AlwaysRewinds_EvenForAnInstantPause()
	{
		Assert.True(TextToSpeechService.ShouldRewindAfterPause(0, 0));
		Assert.True(TextToSpeechService.ShouldRewindAfterPause(5_000, 0));
	}

	[Fact]
	public void NegativeThreshold_AlwaysRewinds()
	{
		// Defensive: a misconfigured negative threshold behaves like 0 (always rewind).
		Assert.True(TextToSpeechService.ShouldRewindAfterPause(0, -1));
	}

	// ---- ComputeResumePosition: where playback actually picks up ----

	[Fact]
	public void NoRewind_KeepsExactPosition()
	{
		// Within-threshold resume holds the exact word position unchanged.
		Assert.Equal(26, TextToSpeechService.ComputeResumePosition(Text, 26, 5, rewind: false));
	}

	[Fact]
	public void Rewind_MovesPositionBackByWordCount()
	{
		// Beyond-threshold resume rewinds: from "over" (26) back 3 words -> "brown" (10).
		Assert.Equal(10, TextToSpeechService.ComputeResumePosition(Text, 26, 3, rewind: true));
	}

	[Fact]
	public void Rewind_WithZeroWordCount_KeepsPosition()
	{
		// Rewinding by zero words is a no-op even on the rewind branch.
		Assert.Equal(26, TextToSpeechService.ComputeResumePosition(Text, 26, 0, rewind: true));
	}

	// ---- Instance guards: Pause/Resume are no-ops when there is nothing to act on ----

	[Fact]
	public void FreshService_IsNotPaused()
	{
		using var tts = new TextToSpeechService();
		Assert.False(tts.IsPaused);
	}

	[Fact]
	public void Pause_WhenNothingPlaying_IsNoOp()
	{
		using var tts = new TextToSpeechService();
		// No read has started, so no synthesizer exists yet: Pause must not throw or
		// transition into a paused state.
		tts.Pause(8, 100, null);
		Assert.False(tts.IsPaused);
	}

	[Fact]
	public void Resume_WhenNotPaused_IsNoOp()
	{
		using var tts = new TextToSpeechService();
		tts.Resume(8, 100, null, resumeRewindWordCount: 5, rewindAfterPauseSeconds: 10);
		Assert.False(tts.IsPaused);
	}

	[Fact]
	public void Stop_WhenNotPaused_LeavesNotPaused()
	{
		using var tts = new TextToSpeechService();
		tts.Stop();
		Assert.False(tts.IsPaused);
	}
}
