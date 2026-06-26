using CognitiveSupport;
using Xunit;

namespace Mutation.Tests;

// Pure-logic tests for the gapless progress weave: boundary snapping, threshold
// selection (which markers to bake in and where), and the spoken<->real offset
// map. None of these need a synthesizer.
public class GaplessProgressWeaveTests
{
	// ---- SentenceBoundaries: snap forward to the next sentence start ----

	[Theory]
	[InlineData(0, 0)]     // already on a boundary
	[InlineData(1, 20)]    // just past one -> next
	[InlineData(20, 20)]   // exactly on one
	[InlineData(61, 80)]
	[InlineData(80, 80)]   // last start
	[InlineData(81, -1)]   // past the last start -> none (in the final sentence)
	public void FirstStartAtOrAfter_SnapsForward(int offset, int expected)
	{
		var starts = new[] { 0, 20, 40, 60, 80 };
		Assert.Equal(expected, SentenceBoundaries.FirstStartAtOrAfter(starts, offset));
	}

	[Fact]
	public void FirstStartAtOrAfter_EmptyList_IsMinusOne()
	{
		Assert.Equal(-1, SentenceBoundaries.FirstStartAtOrAfter(Array.Empty<int>(), 5));
	}

	// ---- ProgressWeavePlanner: which thresholds, at which boundary ----

	private static readonly int[] Starts = { 0, 20, 40, 60, 80 };

	[Fact]
	public void Plan_FromBeginning_WeavesEachThresholdAtItsBoundary()
	{
		var points = ProgressWeavePlanner.Plan(
			length: 100, Starts, fromReal: 0, lastAnnouncedPercent: 0, stepPercent: 25);

		Assert.Equal(new[]
		{
			new ProgressWeavePlanner.WeavePoint(40, 25),
			new ProgressWeavePlanner.WeavePoint(60, 50),
			new ProgressWeavePlanner.WeavePoint(80, 75),
		}, points);
	}

	[Fact]
	public void Plan_ForwardSkipPastThresholds_CollapsesToHighestAtNextBoundary()
	{
		// Skipped to 50%; the 25% and 50% thresholds both snap to the boundary just
		// past the new position and collapse to a single "50%" announcement there.
		var points = ProgressWeavePlanner.Plan(
			length: 100, Starts, fromReal: 50, lastAnnouncedPercent: 0, stepPercent: 25);

		Assert.Equal(new[]
		{
			new ProgressWeavePlanner.WeavePoint(60, 50),
			new ProgressWeavePlanner.WeavePoint(80, 75),
		}, points);
	}

	[Fact]
	public void Plan_BackwardWithThresholdsAlreadyAnnounced_DoesNotReannounce()
	{
		// Already announced through 50%; skipping back to 10% re-plans only 75% ahead.
		var points = ProgressWeavePlanner.Plan(
			length: 100, Starts, fromReal: 10, lastAnnouncedPercent: 50, stepPercent: 25);

		Assert.Equal(new[] { new ProgressWeavePlanner.WeavePoint(80, 75) }, points);
	}

	[Fact]
	public void Plan_ThresholdInFinalSentence_IsDropped()
	{
		// With the last boundary at 40, the 50% and 75% thresholds have no following
		// boundary to weave at, so only 25% is woven (the rest is handled by end-of-text).
		var starts = new[] { 0, 20, 40 };
		var points = ProgressWeavePlanner.Plan(
			length: 100, starts, fromReal: 0, lastAnnouncedPercent: 0, stepPercent: 25);

		Assert.Equal(new[] { new ProgressWeavePlanner.WeavePoint(40, 25) }, points);
	}

	[Fact]
	public void Plan_AnnouncementsDisabled_WeavesNothing()
	{
		var points = ProgressWeavePlanner.Plan(
			length: 100, Starts, fromReal: 0, lastAnnouncedPercent: 0, stepPercent: 0);
		Assert.Empty(points);
	}

	[Fact]
	public void Plan_NeverWeavesHundredPercent()
	{
		// One long sentence spanning the whole text: 25/50/75 thresholds all fall in the
		// final (only) sentence and are dropped; 100% is never a threshold anyway.
		var points = ProgressWeavePlanner.Plan(
			length: 100, new[] { 0 }, fromReal: 0, lastAnnouncedPercent: 0, stepPercent: 25);
		Assert.Empty(points);
		Assert.DoesNotContain(points, p => p.Percent >= 100);
	}

	// ---- SpokenWeave: build the spoken string and map back to real offsets ----

	[Fact]
	public void Build_NoMarkers_IsAScalarShift()
	{
		// No weave: spoken is the remainder from fromReal, and the map is the same
		// scalar shift the old single-delta used (real = spoken + fromReal).
		var (spoken, map) = SpokenWeave.Build("0123456789", fromReal: 2, prefix: "", markers: Array.Empty<SpokenWeave.Marker>());

		Assert.Equal("23456789", spoken);
		Assert.Equal(2, map.ToReal(0));
		Assert.Equal(9, map.ToReal(7));
	}

	[Fact]
	public void Build_Prefix_MapsLikeTheLengthWarning()
	{
		// A spoken-only prefix maps every prefix position back to the start of the real
		// text (clamped), exactly like the old delta of -prefix.Length.
		var (spoken, map) = SpokenWeave.Build("abcdef", fromReal: 0, prefix: "XX", markers: Array.Empty<SpokenWeave.Marker>());

		Assert.Equal("XXabcdef", spoken);
		Assert.Equal(0, map.ToReal(0)); // inside the prefix -> clamps to 0
		Assert.Equal(0, map.ToReal(2)); // first real char 'a'
		Assert.Equal(4, map.ToReal(6)); // 'e' at real index 4
	}

	[Fact]
	public void Build_OneMarker_MapsBeforeInsideAndAfterIt()
	{
		var markers = new[] { new SpokenWeave.Marker(RealOffset: 4, Percent: 50, Text: "[M]") };
		var (spoken, map) = SpokenWeave.Build("0123456789", fromReal: 0, prefix: "", markers: markers);

		Assert.Equal("0123[M]456789", spoken);

		// Before the marker: spoken == real.
		Assert.Equal(0, map.ToReal(0));
		Assert.Equal(3, map.ToReal(3));
		// Inside the marker ("[M]" at spoken 4..6): maps to the boundary it sits at.
		Assert.Equal(4, map.ToReal(4));
		Assert.Equal(4, map.ToReal(6));
		// After the marker: spoken is shifted right by the marker length.
		Assert.Equal(4, map.ToReal(7)); // real '4'
		Assert.Equal(9, map.ToReal(12)); // real '9'
	}

	[Fact]
	public void Build_MapIsMonotonic_SoSentenceIndexNeverGoesBackwards()
	{
		// Real offsets must be non-decreasing across the spoken string, so a derived
		// sentence index (and the reported percentage) never regresses while reading.
		var markers = new[]
		{
			new SpokenWeave.Marker(3, 30, "[A]"),
			new SpokenWeave.Marker(7, 60, "[B]"),
		};
		var (spoken, map) = SpokenWeave.Build("0123456789", fromReal: 0, prefix: "", markers: markers);

		int prev = map.ToReal(0);
		for (int p = 1; p < spoken.Length; p++)
		{
			int real = map.ToReal(p);
			Assert.True(real >= prev, $"real regressed at spoken {p}: {real} < {prev}");
			prev = real;
		}
	}

	[Fact]
	public void HighestMarkerPercentAtOrBefore_TracksReachedAnnouncements()
	{
		var markers = new[]
		{
			new SpokenWeave.Marker(40, 25, "[A]"),
			new SpokenWeave.Marker(60, 50, "[B]"),
		};
		var (_, map) = SpokenWeave.Build(new string('x', 100), fromReal: 0, prefix: "", markers: markers);

		Assert.Equal(0, map.HighestMarkerPercentAtOrBefore(39));  // none reached yet
		Assert.Equal(25, map.HighestMarkerPercentAtOrBefore(40)); // first marker's boundary
		Assert.Equal(25, map.HighestMarkerPercentAtOrBefore(59));
		Assert.Equal(50, map.HighestMarkerPercentAtOrBefore(60)); // second reached
		Assert.Equal(50, map.HighestMarkerPercentAtOrBefore(100));
	}
}
