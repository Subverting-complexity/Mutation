using CognitiveSupport;
using Xunit;

namespace Mutation.Tests;

// Pure-logic tests for the speak-position hotkey and periodic progress announcements:
// the characters->minutes estimate, the reading-position snapshot, the threshold-
// crossing decision, and the spoken phrasing. None of these need a synthesizer.
public class ReadingPositionAndProgressTests
{
	// ---- ReadingEstimate: chars -> minutes (one shared source of truth) ----

	[Theory]
	[InlineData(0, 0d)]
	[InlineData(900, 1d)]     // 900 chars / 5 per word = 180 words / 180 wpm = 1 min
	[InlineData(5400, 6d)]    // 5400 / 5 / 180 = 6 min
	public void EstimateMinutes_MatchesSharedFormula(int chars, double expected)
	{
		Assert.Equal(expected, ReadingEstimate.EstimateMinutes(chars), 3);
	}

	[Theory]
	[InlineData(0, 1)]        // always at least one minute when spoken
	[InlineData(900, 1)]
	[InlineData(5400, 6)]
	public void SpokenWholeMinutes_NeverBelowOne(int chars, int expected)
	{
		Assert.Equal(expected, ReadingEstimate.SpokenWholeMinutes(chars));
	}

	[Theory]
	[InlineData(0, 0)]        // remaining may be zero -> "less than a minute"
	[InlineData(900, 1)]
	public void RemainingWholeMinutes_AllowsZero(int chars, int expected)
	{
		Assert.Equal(expected, ReadingEstimate.RemainingWholeMinutes(chars));
	}

	[Theory]
	[InlineData(900, 1, false)]    // ~1.0 min is not strictly greater than 1
	[InlineData(1100, 1, true)]    // ~1.2 min exceeds 1
	[InlineData(1000, 2, false)]   // short read stays silent under a 2-minute floor
	[InlineData(5400, 2, true)]    // 6 min exceeds 2
	public void ExceedsMinutes_GatesAnnouncements(int chars, int minimumMinutes, bool expected)
	{
		Assert.Equal(expected, ReadingEstimate.ExceedsMinutes(chars, minimumMinutes));
	}

	// ---- ReadingPosition.Create: percentage / sentence / minutes ----

	[Fact]
	public void Create_EmptyText_IsNotReading()
	{
		var p = ReadingPosition.Create(currentPosition: 0, textLength: 0, sentenceIndex: 0, sentenceCount: 0);
		Assert.False(p.IsReading);
		Assert.Equal(ReadingPosition.NotReading, p);
	}

	[Fact]
	public void Create_NoSentences_IsNotReading()
	{
		var p = ReadingPosition.Create(currentPosition: 10, textLength: 100, sentenceIndex: 0, sentenceCount: 0);
		Assert.False(p.IsReading);
	}

	[Fact]
	public void Create_RoundsPercentToNearestFive()
	{
		// 380 / 1000 = 38% -> nearest 5% = 40%.
		var p = ReadingPosition.Create(currentPosition: 380, textLength: 1000, sentenceIndex: 3, sentenceCount: 22);
		Assert.True(p.IsReading);
		Assert.Equal(40, p.PercentComplete);
		Assert.Equal(4, p.SentenceNumber); // 1-based
		Assert.Equal(22, p.SentenceCount);
	}

	[Fact]
	public void Create_SingleSentence_ReportsOneOfOne()
	{
		var p = ReadingPosition.Create(currentPosition: 0, textLength: 200, sentenceIndex: 0, sentenceCount: 1);
		Assert.Equal(1, p.SentenceNumber);
		Assert.Equal(1, p.SentenceCount);
		Assert.Equal(0, p.PercentComplete);
	}

	[Fact]
	public void Create_EndOfText_IsHundredPercentWithNoMinutesLeft()
	{
		var p = ReadingPosition.Create(currentPosition: 1000, textLength: 1000, sentenceIndex: 21, sentenceCount: 22);
		Assert.Equal(100, p.PercentComplete);
		Assert.Equal(22, p.SentenceNumber);
		Assert.Equal(0, p.MinutesRemaining);
	}

	[Fact]
	public void Create_ClampsSentenceIndexAndPosition()
	{
		// Position past the end and sentence index past the count both clamp.
		var p = ReadingPosition.Create(currentPosition: 5000, textLength: 1000, sentenceIndex: 99, sentenceCount: 10);
		Assert.Equal(100, p.PercentComplete);
		Assert.Equal(10, p.SentenceNumber);
	}

	// ---- ReadingProgress: threshold crossing ----

	[Theory]
	[InlineData(0, 25, 25, 25)]
	[InlineData(0, 50, 25, 50)]
	[InlineData(0, 24, 25, 0)]   // not reached yet
	[InlineData(50, 50, 25, 0)]  // already announced exactly this point
	[InlineData(75, 80, 25, 0)]  // nothing new below 100
	public void HighestThresholdCrossed_BasicSteps(int last, int current, int step, int expected)
	{
		Assert.Equal(expected, ReadingProgress.HighestThresholdCrossed(last, current, step));
	}

	[Fact]
	public void HighestThresholdCrossed_SingleBigJump_AnnouncesOnlyHighest()
	{
		// A 25% -> 75% jump announces once at 75 and marks 50 passed silently.
		int crossed = ReadingProgress.HighestThresholdCrossed(lastAnnouncedPercent: 25, currentPercent: 75, stepPercent: 25);
		Assert.Equal(75, crossed);

		// After recording 75, the skipped 50 never re-announces.
		Assert.Equal(0, ReadingProgress.HighestThresholdCrossed(75, 76, 25));
	}

	[Theory]
	[InlineData(0, 100, 25, 75)]  // 100% is never a progress threshold
	[InlineData(75, 100, 25, 0)]
	[InlineData(0, 100, 30, 90)]  // largest multiple of 30 below 100
	public void HighestThresholdCrossed_NeverAnnouncesHundred(int last, int current, int step, int expected)
	{
		Assert.Equal(expected, ReadingProgress.HighestThresholdCrossed(last, current, step));
	}

	[Fact]
	public void HighestThresholdCrossed_ZeroStep_IsSafe()
	{
		Assert.Equal(0, ReadingProgress.HighestThresholdCrossed(0, 50, 0));
	}

	// ---- ProgressDeferral: hold the announcement until a sentence boundary ----

	[Fact]
	public void Deferral_CrossingThreshold_HoldsItPendingNotImmediate()
	{
		var d = new ProgressDeferral();

		// Crossing a threshold mid-sentence records it as pending; nothing is "taken" yet.
		Assert.True(d.Observe(currentPercent: 25, stepPercent: 25));
		Assert.Equal(25, d.PendingPercent);

		// It is only released at the next sentence boundary.
		Assert.Equal(25, d.TakeAtBoundary());
		Assert.Equal(0, d.PendingPercent);
	}

	[Fact]
	public void Deferral_NoThresholdReached_NothingPending()
	{
		var d = new ProgressDeferral();
		Assert.False(d.Observe(currentPercent: 24, stepPercent: 25));
		Assert.Equal(0, d.PendingPercent);
		Assert.Equal(0, d.TakeAtBoundary());
	}

	[Fact]
	public void Deferral_MultipleThresholdsBeforeBoundary_FlushesOnlyHighest()
	{
		var d = new ProgressDeferral();

		// Several thresholds cross before the next boundary (25 -> 50 -> 75).
		Assert.True(d.Observe(currentPercent: 25, stepPercent: 25));
		Assert.True(d.Observe(currentPercent: 50, stepPercent: 25));
		Assert.True(d.Observe(currentPercent: 75, stepPercent: 25));

		// Only the highest is pending, so the boundary produces a single announcement.
		Assert.Equal(75, d.TakeAtBoundary());

		// The skipped lower thresholds never re-announce afterwards.
		Assert.False(d.Observe(currentPercent: 76, stepPercent: 25));
		Assert.Equal(0, d.TakeAtBoundary());
	}

	[Fact]
	public void Deferral_FinalSentenceCompletesAtHundred_DropsPending()
	{
		var d = new ProgressDeferral();
		d.Observe(currentPercent: 75, stepPercent: 25);

		// Reaching the end (100%) is never announced as progress, so the pending 75 is dropped.
		Assert.Equal(0, d.TakeOnCompletion(finalPercent: 100));
		Assert.Equal(0, d.PendingPercent);
	}

	[Fact]
	public void Deferral_CompletionBelowHundred_StillFlushesPending()
	{
		var d = new ProgressDeferral();
		d.Observe(currentPercent: 50, stepPercent: 25);

		// Defensive guard: a completion below 100% still speaks the held announcement.
		Assert.Equal(50, d.TakeOnCompletion(finalPercent: 99));
		Assert.Equal(0, d.PendingPercent);
	}

	[Fact]
	public void Deferral_Reset_ClearsThresholdAndPending()
	{
		var d = new ProgressDeferral();
		d.Observe(currentPercent: 50, stepPercent: 25);

		d.Reset();

		Assert.Equal(0, d.LastAnnouncedPercent);
		Assert.Equal(0, d.PendingPercent);
		// After a reset the same threshold can be crossed (and announced) again.
		Assert.True(d.Observe(currentPercent: 25, stepPercent: 25));
	}

	// ---- ReadingAnnouncements: spoken phrasing ----

	[Fact]
	public void Position_NotReading_SpeaksClearMessage()
	{
		Assert.Equal("Not currently reading.", ReadingAnnouncements.Position(ReadingPosition.NotReading));
	}

	[Fact]
	public void Position_SpeaksSentencePercentAndMinutes()
	{
		var p = new ReadingPosition
		{
			IsReading = true,
			SentenceNumber = 4,
			SentenceCount = 22,
			PercentComplete = 40,
			MinutesRemaining = 2,
		};
		Assert.Equal("Sentence 4 of 22, about 40% through, about 2 minutes left.", ReadingAnnouncements.Position(p));
	}

	[Fact]
	public void Position_SingularMinute()
	{
		var p = new ReadingPosition { IsReading = true, SentenceNumber = 1, SentenceCount = 3, PercentComplete = 10, MinutesRemaining = 1 };
		Assert.Equal("Sentence 1 of 3, about 10% through, about 1 minute left.", ReadingAnnouncements.Position(p));
	}

	[Fact]
	public void Position_ZeroMinutes_SaysLessThanAMinute()
	{
		var p = new ReadingPosition { IsReading = true, SentenceNumber = 22, SentenceCount = 22, PercentComplete = 100, MinutesRemaining = 0 };
		Assert.Equal("Sentence 22 of 22, about 100% through, less than a minute left.", ReadingAnnouncements.Position(p));
	}

	[Theory]
	[InlineData(50, 3, "50%, about 3 minutes left.")]
	[InlineData(25, 1, "25%, about 1 minute left.")]
	[InlineData(75, 0, "75%, less than a minute left.")]
	public void Progress_Phrasing(int percent, int minutes, string expected)
	{
		Assert.Equal(expected, ReadingAnnouncements.Progress(percent, minutes));
	}

	[Theory]
	[InlineData(1, "Reading approximately 1 minute of text. ")]
	[InlineData(5, "Reading approximately 5 minutes of text. ")]
	public void StartupReadingTime_Phrasing(int minutes, string expected)
	{
		Assert.Equal(expected, ReadingAnnouncements.StartupReadingTime(minutes));
	}
}
