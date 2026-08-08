using System;
using System.Collections.Generic;
using System.IO;
using Mutation.Ui;
using Mutation.Ui.Core;

namespace Mutation.Tests;

/// <summary>
/// Covers <see cref="SessionSelectionPlanner"/> — which recording is selected after the
/// session list is rebuilt, and which one Previous/Next moves to. Lifted out of
/// <see cref="AudioSessionManager"/>, the second-largest untested unit in the app, which
/// could not be constructed in a test without a recorder, a device enumerator, and a
/// transcription service (issue #255).
/// <para>
/// Picking the wrong recording here is silent and immediate: navigation plays what it
/// selects, so for a user working by ear the first sign is the wrong audio.
/// </para>
/// </summary>
public class SessionSelectionPlannerTests
{
	private static SpeechSession Session(string fileName) =>
		new(Path.Combine(Path.GetTempPath(), fileName), new DateTime(2026, 1, 1));

	private static IReadOnlyList<SpeechSession> List(params SpeechSession[] sessions) => sessions;

	[Fact]
	public void An_empty_list_selects_nothing()
	{
		var chosen = SessionSelectionPlanner.ChooseSelection(List(), Session("gone.ogg"), preferredPath: null);

		Assert.Null(chosen);
	}

	[Fact]
	public void An_empty_list_clears_a_preferred_path_too()
	{
		var wanted = Session("gone.ogg");

		var chosen = SessionSelectionPlanner.ChooseSelection(List(), wanted, wanted.FilePath);

		Assert.Null(chosen);
	}

	[Fact]
	public void With_nothing_selected_the_newest_recording_is_chosen()
	{
		var newest = Session("newest.ogg");

		var chosen = SessionSelectionPlanner.ChooseSelection(List(newest, Session("older.ogg")), null, null);

		Assert.Same(newest, chosen);
	}

	[Fact]
	public void A_preferred_path_wins_over_the_newest_recording()
	{
		var older = Session("older.ogg");

		var chosen = SessionSelectionPlanner.ChooseSelection(List(Session("newest.ogg"), older), null, older.FilePath);

		Assert.Same(older, chosen);
	}

	[Fact]
	public void A_preferred_path_wins_over_what_was_already_selected()
	{
		var wanted = Session("wanted.ogg");
		var previously = Session("previously.ogg");

		var chosen = SessionSelectionPlanner.ChooseSelection(List(previously, wanted), previously, wanted.FilePath);

		Assert.Same(wanted, chosen);
	}

	[Fact]
	public void A_preferred_path_that_is_no_longer_in_the_list_falls_back_to_the_newest()
	{
		var newest = Session("newest.ogg");

		var chosen = SessionSelectionPlanner.ChooseSelection(
			List(newest, Session("older.ogg")),
			currentSelection: null,
			preferredPath: Session("deleted.ogg").FilePath);

		Assert.Same(newest, chosen);
	}

	[Fact]
	public void A_preferred_path_that_has_been_deleted_falls_back_to_the_newest_even_when_something_is_selected()
	{
		var deleted = Session("deleted.ogg");
		var newest = Session("newest.ogg");

		var chosen = SessionSelectionPlanner.ChooseSelection(
			List(newest, Session("older.ogg")),
			currentSelection: deleted,
			preferredPath: deleted.FilePath);

		// The path AudioSessionManager actually takes: NavigateSessionsAsync always passes the
		// current selection as the preferred one, so when retention cleanup has removed that
		// file, the lookup misses and the fallback has to run rather than leaving the deleted
		// recording selected.
		Assert.Same(newest, chosen);
	}

	[Fact]
	public void A_preferred_path_that_has_been_deleted_selects_nothing_when_the_list_is_now_empty()
	{
		var deleted = Session("deleted.ogg");

		var chosen = SessionSelectionPlanner.ChooseSelection(List(), deleted, deleted.FilePath);

		Assert.Null(chosen);
	}

	[Fact]
	public void ChooseSelection_refuses_a_missing_session_list()
	{
		Assert.Throws<ArgumentNullException>(() =>
			SessionSelectionPlanner.ChooseSelection(null!, null, null));
	}

	[Fact]
	public void A_preferred_path_is_matched_however_it_is_spelled()
	{
		var session = Session("Recording.ogg");
		string differentlySpelled = Path.Combine(Path.GetTempPath(), "sub", "..", "recording.ogg");

		var chosen = SessionSelectionPlanner.ChooseSelection(List(session), null, differentlySpelled);

		Assert.Same(session, chosen);
	}

	[Theory]
	[InlineData(null)]
	[InlineData("")]
	[InlineData("   ")]
	public void A_blank_preferred_path_keeps_the_existing_selection(string? preferredPath)
	{
		var selected = Session("selected.ogg");

		var chosen = SessionSelectionPlanner.ChooseSelection(
			List(Session("newest.ogg"), selected),
			selected,
			preferredPath);

		// Refreshing after a transcription must not silently jump the user back to the top of
		// the list.
		Assert.Same(selected, chosen);
	}

	[Fact]
	public void A_selection_that_has_left_the_list_falls_back_to_the_newest()
	{
		var deleted = Session("deleted.ogg");
		var newest = Session("newest.ogg");

		var chosen = SessionSelectionPlanner.ChooseSelection(List(newest), deleted, preferredPath: null);

		// Retention cleanup removes the selected recording underneath the user. Leaving it
		// selected made Play announce "Audio file not found", which for someone working by ear
		// reads as a broken button rather than as a recording that is gone (issue #303).
		Assert.Same(newest, chosen);
	}

	[Fact]
	public void A_selection_that_has_left_an_emptied_list_selects_nothing()
	{
		var deleted = Session("deleted.ogg");

		Assert.Null(SessionSelectionPlanner.ChooseSelection(List(), deleted, preferredPath: null));
	}

	[Fact]
	public void A_kept_selection_is_the_instance_the_rebuilt_list_holds()
	{
		// The list is rebuilt from disk on every refresh, so the entry naming the selected
		// recording is a new object each time. SpeechSession is a record, so returning the
		// caller's stale instance would still have compared equal — this pins the selection and
		// the list on one object rather than two that merely agree.
		var stale = Session("selected.ogg");
		var rebuilt = Session("selected.ogg");

		var chosen = SessionSelectionPlanner.ChooseSelection(
			List(Session("newest.ogg"), rebuilt),
			stale,
			preferredPath: null);

		Assert.Same(rebuilt, chosen);
	}

	[Fact]
	public void A_selection_spelled_differently_by_the_rebuilt_list_still_counts_as_present()
	{
		var selected = new SpeechSession(
			Path.Combine(Path.GetTempPath(), "sub", "..", "Selected.ogg"),
			new DateTime(2026, 1, 1));
		var rebuilt = Session("selected.ogg");

		var chosen = SessionSelectionPlanner.ChooseSelection(
			List(Session("newest.ogg"), rebuilt),
			selected,
			preferredPath: null);

		Assert.Same(rebuilt, chosen);
	}

	[Theory]
	[InlineData(-1)]
	[InlineData(1)]
	public void Navigation_goes_nowhere_when_there_are_no_recordings(int direction)
	{
		Assert.Null(SessionSelectionPlanner.NextIndex(count: 0, currentIndex: -1, direction));
	}

	[Fact]
	public void Next_moves_away_from_the_newest_recording()
	{
		Assert.Equal(2, SessionSelectionPlanner.NextIndex(count: 5, currentIndex: 1, direction: 1));
	}

	[Fact]
	public void Previous_moves_towards_the_newest_recording()
	{
		Assert.Equal(0, SessionSelectionPlanner.NextIndex(count: 5, currentIndex: 1, direction: -1));
	}

	[Fact]
	public void Previous_at_the_newest_recording_stops_rather_than_wrapping()
	{
		Assert.Null(SessionSelectionPlanner.NextIndex(count: 5, currentIndex: 0, direction: -1));
	}

	[Fact]
	public void Next_at_the_oldest_recording_stops_rather_than_wrapping()
	{
		Assert.Null(SessionSelectionPlanner.NextIndex(count: 5, currentIndex: 4, direction: 1));
	}

	[Fact]
	public void Next_with_nothing_selected_starts_from_the_newest_recording()
	{
		Assert.Equal(1, SessionSelectionPlanner.NextIndex(count: 5, currentIndex: -1, direction: 1));
	}

	[Fact]
	public void Previous_with_nothing_selected_goes_nowhere()
	{
		// Nothing selected is treated as sitting on the newest recording, and there is nothing
		// newer than that.
		Assert.Null(SessionSelectionPlanner.NextIndex(count: 5, currentIndex: -1, direction: -1));
	}

	[Fact]
	public void Next_in_a_single_recording_list_goes_nowhere()
	{
		Assert.Null(SessionSelectionPlanner.NextIndex(count: 1, currentIndex: 0, direction: 1));
		Assert.Null(SessionSelectionPlanner.NextIndex(count: 1, currentIndex: 0, direction: -1));
	}

	[Theory]
	[InlineData(-1)]
	[InlineData(1)]
	public void An_index_past_the_end_of_the_list_stops_rather_than_selecting_out_of_range(int direction)
	{
		// A selection index that outran the list would otherwise index past the end. Refusing
		// the move is the safe answer, even though it means navigation goes quiet until the
		// selection is re-established.
		Assert.Null(SessionSelectionPlanner.NextIndex(count: 3, currentIndex: 7, direction));
	}

	[Fact]
	public void Zero_is_treated_as_moving_away_from_the_newest_recording()
	{
		// Only a negative direction means "previous"; the caller passes +1 or -1, and anything
		// else has to land somewhere defined rather than off the end of the list.
		Assert.Equal(1, SessionSelectionPlanner.NextIndex(count: 5, currentIndex: 0, direction: 0));
	}
}
