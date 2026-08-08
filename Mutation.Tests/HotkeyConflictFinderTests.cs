using System;
using System.Collections.Generic;
using Mutation.Ui.Core;
using static Mutation.Ui.Core.HotkeyConflictFinder;

namespace Mutation.Tests;

/// <summary>
/// Covers <see cref="HotkeyConflictFinder"/> — the "Duplicate hotkey" badge on the Settings
/// screen and the duplicate flag on a hotkey route.
/// <para>
/// The screen used to compare the typed text case-insensitively while registration compared
/// chords, so a pair Settings waved through could be refused later by the registration table
/// and reported as a failure dialog after saving (issue #306). For a user who cannot see the
/// badge appear, a conflict found while editing is the difference between fixing it in place
/// and hunting for it afterwards.
/// </para>
/// </summary>
public class HotkeyConflictFinderTests
{
	/// <summary>A shortcut Mutation claims from Windows.</summary>
	private static ConfiguredHotkey Claimed(string? text) => new(text, ClaimsTheChord: true);

	/// <summary>A "Send key after…" entry, typed out to the foreground app instead.</summary>
	private static ConfiguredHotkey Sent(string? text) => new(text, ClaimsTheChord: false);

	private static IReadOnlyList<ConfiguredHotkey> Configured(params ConfiguredHotkey[] entries) => entries;

	[Fact]
	public void Nothing_configured_conflicts_with_nothing()
	{
		Assert.Empty(DuplicateIndexes(Configured()));
	}

	[Fact]
	public void Distinct_shortcuts_do_not_conflict()
	{
		Assert.Empty(DuplicateIndexes(Configured(
			Claimed("CTRL+ALT+G"), Claimed("CTRL+ALT+H"), Claimed("SHIFT+F1"))));
	}

	[Fact]
	public void The_same_shortcut_twice_flags_both_rows()
	{
		// Both, not just the second: the user has to be told which two rows to compare.
		Assert.Equal(new[] { 0, 2 }, Sorted(DuplicateIndexes(Configured(
			Claimed("CTRL+ALT+G"), Claimed("CTRL+ALT+H"), Claimed("CTRL+ALT+G")))));
	}

	[Fact]
	public void Modifier_order_does_not_make_two_shortcuts()
	{
		// The case this issue is about, and the one the old text comparison genuinely missed:
		// the hotkey editor preserves the order the user typed, so both spellings can be sitting
		// in settings at once, and registration would refuse the second.
		Assert.Equal(new[] { 0, 1 }, Sorted(DuplicateIndexes(Configured(
			Claimed("CTRL+SHIFT+A"), Claimed("SHIFT+CTRL+A")))));
	}

	[Fact]
	public void A_modifier_alias_does_not_make_two_shortcuts()
	{
		Assert.Equal(new[] { 0, 1 }, Sorted(DuplicateIndexes(Configured(
			Claimed("CONTROL+WINDOWS+K"), Claimed("CTRL+WIN+K")))));
	}

	[Fact]
	public void A_separator_or_case_difference_does_not_make_two_shortcuts()
	{
		Assert.Equal(new[] { 0, 1, 2 }, Sorted(DuplicateIndexes(Configured(
			Claimed("CTRL+ALT+G"), Claimed("ctrl-alt-g"), Claimed("Ctrl Alt G")))));
	}

	// ----- "Send key after…" entries -----

	[Fact]
	public void Two_sent_keys_holding_the_same_shortcut_do_not_conflict()
	{
		// Sending the same key after OCR and after transcription is an ordinary way to set the
		// app up. Neither is claimed from Windows, so there is nothing to clash over.
		Assert.Empty(DuplicateIndexes(Configured(Sent("CTRL+V"), Sent("CTRL+V"))));
	}

	[Fact]
	public void A_sent_key_that_matches_a_claimed_shortcut_flags_both()
	{
		// The real conflict, and the one that would loop: Windows routes the synthesized
		// keystroke back to Mutation, so the action fires itself again.
		Assert.Equal(new[] { 0, 1 }, Sorted(DuplicateIndexes(Configured(
			Claimed("ALT+J"), Sent("ALT+J")))));
	}

	[Fact]
	public void A_sent_key_that_matches_a_claimed_shortcut_is_caught_however_it_is_spelled()
	{
		Assert.Equal(new[] { 0, 1 }, Sorted(DuplicateIndexes(Configured(
			Claimed("CTRL+SHIFT+M"), Sent("shift-ctrl-m")))));
	}

	[Fact]
	public void Two_sent_keys_and_a_claimed_shortcut_all_flag()
	{
		// Each sent key clashes with the claimed one, even though they do not clash with each
		// other.
		Assert.Equal(new[] { 0, 1, 2 }, Sorted(DuplicateIndexes(Configured(
			Sent("ALT+J"), Claimed("ALT+J"), Sent("alt+j")))));
	}

	[Fact]
	public void Any_step_of_a_sent_sequence_that_matches_a_claimed_shortcut_flags_both()
	{
		// A "send key after" value may be a comma-separated sequence. Reading it as one chord
		// misses the collision and invents a chord nobody configured: "ALT+J, CTRL+ENTER" read
		// whole comes out as CTRL+ALT+ENTER, so the ALT+J that actually loops goes unreported.
		Assert.Equal(new[] { 0, 1 }, Sorted(DuplicateIndexes(Configured(
			Claimed("ALT+J"), Sent("ALT+J, CTRL+ENTER")))));
	}

	[Fact]
	public void A_later_step_of_a_sent_sequence_counts_too()
	{
		Assert.Equal(new[] { 0, 1 }, Sorted(DuplicateIndexes(Configured(
			Claimed("CTRL+ENTER"), Sent("ALT+J, CTRL+ENTER")))));
	}

	[Fact]
	public void A_sent_sequence_that_clashes_with_nothing_is_left_alone()
	{
		Assert.Empty(DuplicateIndexes(Configured(
			Claimed("CTRL+ALT+G"), Sent("ALT+J, CTRL+ENTER"))));
	}

	[Fact]
	public void A_sent_sequence_naming_one_chord_twice_is_not_a_duplicate_of_itself()
	{
		// One row cannot clash with itself, however many times it names the chord.
		Assert.Empty(DuplicateIndexes(Configured(Sent("CTRL+V, CTRL+V"))));
	}

	[Fact]
	public void A_comma_inside_a_claimed_shortcut_is_a_separator_not_a_sequence()
	{
		// Registration parses the whole string, where a comma is just another separator —
		// "CTRL,A" binds Ctrl+A. Splitting it would compare two things that are not chords.
		Assert.Equal(new[] { 0, 1 }, Sorted(DuplicateIndexes(Configured(
			Claimed("CTRL,A"), Claimed("CTRL+A")))));
	}

	[Fact]
	public void SendKeys_syntax_in_a_sent_value_is_read_back_and_compared()
	{
		// The send-key boxes accept both spellings of one shortcut. This one used to take no
		// part in the comparison at all, so "^v" against Ctrl+V raised nothing while
		// "Ctrl+V" against Ctrl+V was flagged (issue #326).
		Assert.Equal(new[] { 0, 1 }, Sorted(DuplicateIndexes(Configured(
			Claimed("CTRL+V"), Sent("^v")))));
	}

	[Fact]
	public void The_case_from_the_issue_is_flagged_either_way_it_is_written()
	{
		Assert.Equal(new[] { 0, 1 }, Sorted(DuplicateIndexes(Configured(
			Claimed("CTRL+F5"), Sent("^{F5}")))));
	}

	[Fact]
	public void Every_chord_a_SendKeys_group_presses_is_compared()
	{
		Assert.Equal(new[] { 0, 1 }, Sorted(DuplicateIndexes(Configured(
			Claimed("CTRL+SHIFT+B"), Sent("^+(ab)")))));
	}

	[Fact]
	public void A_SendKeys_value_that_clashes_with_nothing_is_left_alone()
	{
		Assert.Empty(DuplicateIndexes(Configured(Claimed("CTRL+ALT+G"), Sent("^{F5}"))));
	}

	[Fact]
	public void Two_send_boxes_holding_the_same_SendKeys_value_are_not_a_clash()
	{
		// Sending the same key after OCR and after transcription is an ordinary way to set
		// the app up. Reading the shorthand must not turn that into a complaint.
		Assert.Empty(DuplicateIndexes(Configured(Sent("^{F5}"), Sent("^{F5}"))));
	}

	[Fact]
	public void Text_typed_out_by_a_send_box_is_not_mistaken_for_shortcuts()
	{
		// "hello" types five letters. Reading five one-key shortcuts out of it would flag a
		// row that clashes with nothing at all.
		Assert.Empty(DuplicateIndexes(Configured(Claimed("H"), Claimed("O"), Sent("hello"))));
	}

	[Fact]
	public void A_plain_chord_still_wins_over_the_SendKeys_reading_of_the_same_text()
	{
		// The '+' in "Ctrl+F5" separates a chord; the one in "+{F5}" is SendKeys' Shift.
		// Reading a chord first is what keeps the two apart.
		Assert.Equal(new[] { 0, 1 }, Sorted(DuplicateIndexes(Configured(
			Claimed("CTRL+F5"), Sent("Ctrl+F5")))));
	}

	// ----- Nothing to compare -----

	[Theory]
	[InlineData(null)]
	[InlineData("")]
	[InlineData("   ")]
	public void Unset_shortcuts_never_conflict(string? blank)
	{
		// Several hotkeys are optional and left empty. Two empties are not a clash.
		Assert.Empty(DuplicateIndexes(Configured(Claimed(blank), Claimed(blank), Sent(blank))));
	}

	[Fact]
	public void Half_typed_text_never_conflicts()
	{
		// Text that does not parse cannot be registered, so it cannot collide with anything.
		// Flagging it as a duplicate would be a second, wrong complaint on top of the
		// validation message the row already shows.
		Assert.Empty(DuplicateIndexes(Configured(
			Claimed("CTRL+"), Claimed("CTRL+"), Claimed("Ctrl+Shift"))));
	}

	[Fact]
	public void A_real_shortcut_still_conflicts_alongside_unparseable_rows()
	{
		Assert.Equal(new[] { 1, 3 }, Sorted(DuplicateIndexes(Configured(
			Claimed("CTRL+"), Claimed("CTRL+ALT+G"), Claimed(null), Claimed("ctrl-alt-g")))));
	}

	[Fact]
	public void A_missing_list_is_refused_rather_than_treated_as_empty()
	{
		Assert.Throws<ArgumentNullException>(() => DuplicateIndexes(null!));
	}

	// ----- Across every list on the page (issue #321) -----
	//
	// HotkeyRegistrationTable is one table for the whole app: the Core, Router and Prompt
	// groups exist only to decide what a refresh releases, and share one set of live chords.
	// Each Settings list used to check only itself, so a core hotkey and a router mapping
	// could claim the same chord with no badge on either row — and the user found out from
	// "The shortcut is already registered." after saving.

	[Fact]
	public void A_shortcut_on_one_list_conflicts_with_the_same_one_on_another()
	{
		var across = DuplicateIndexesAcross(Lists(
			Configured(Claimed("CTRL+ALT+G"), Claimed("CTRL+ALT+H")),
			Configured(Claimed("CTRL+ALT+G"))));

		// Both ends flagged, on whichever list they sit, so the user can find the pair.
		Assert.Equal(new[] { 0 }, Sorted(across[0]));
		Assert.Equal(new[] { 0 }, Sorted(across[1]));
	}

	[Fact]
	public void Distinct_shortcuts_across_lists_do_not_conflict()
	{
		var across = DuplicateIndexesAcross(Lists(
			Configured(Claimed("CTRL+ALT+G")),
			Configured(Claimed("CTRL+ALT+H"), Claimed("SHIFT+F1"))));

		Assert.Empty(across[0]);
		Assert.Empty(across[1]);
	}

	[Fact]
	public void Modifier_order_does_not_hide_a_clash_across_lists()
	{
		// The spellings differ and the chord does not, which is the whole reason this
		// compares parsed chords rather than text (issue #306).
		var across = DuplicateIndexesAcross(Lists(
			Configured(Claimed("SHIFT+CTRL+A")),
			Configured(Claimed("CTRL+SHIFT+A"))));

		Assert.Equal(new[] { 0 }, Sorted(across[0]));
		Assert.Equal(new[] { 0 }, Sorted(across[1]));
	}

	[Fact]
	public void A_sent_key_still_clashes_with_a_registered_one_on_another_list()
	{
		// Windows routes the synthesized keystroke back to whoever registered the chord, so
		// the action fires itself again — the exclusion #320 established survives the widening.
		var across = DuplicateIndexesAcross(Lists(
			Configured(Sent("CTRL+ALT+G")),
			Configured(Claimed("CTRL+ALT+G"))));

		Assert.Equal(new[] { 0 }, Sorted(across[0]));
		Assert.Equal(new[] { 0 }, Sorted(across[1]));
	}

	[Fact]
	public void Two_sent_keys_on_different_lists_are_still_not_a_conflict()
	{
		var across = DuplicateIndexesAcross(Lists(
			Configured(Sent("^{F5}")),
			Configured(Sent("^{F5}"))));

		Assert.Empty(across[0]);
		Assert.Empty(across[1]);
	}

	[Fact]
	public void Half_typed_text_never_conflicts_across_lists_either()
	{
		// It cannot be registered at all, so it cannot collide — and the row already carries a
		// validation message without a wrong duplicate badge on top of it.
		var across = DuplicateIndexesAcross(Lists(
			Configured(Claimed("CTRL+")),
			Configured(Claimed("CTRL+"), Claimed(null))));

		Assert.Empty(across[0]);
		Assert.Empty(across[1]);
	}

	[Fact]
	public void A_clash_inside_one_list_is_still_found_when_checked_alongside_others()
	{
		var across = DuplicateIndexesAcross(Lists(
			Configured(Claimed("CTRL+ALT+G"), Claimed("CTRL+ALT+H"), Claimed("ctrl+alt+g")),
			Configured(Claimed("SHIFT+F1"))));

		Assert.Equal(new[] { 0, 2 }, Sorted(across[0]));
		Assert.Empty(across[1]);
	}

	[Fact]
	public void An_empty_list_alongside_a_populated_one_answers_for_both()
	{
		var across = DuplicateIndexesAcross(Lists(
			Configured(),
			Configured(Claimed("CTRL+ALT+G"), Claimed("CTRL+ALT+G"))));

		Assert.Empty(across[0]);
		Assert.Equal(new[] { 0, 1 }, Sorted(across[1]));
	}

	[Fact]
	public void A_missing_set_of_lists_is_refused_rather_than_treated_as_empty()
	{
		Assert.Throws<ArgumentNullException>(() => DuplicateIndexesAcross(null!));
		Assert.Throws<ArgumentNullException>(() => DuplicateIndexesAcross(Lists(Configured(), null!)));
	}

	private static IReadOnlyList<IReadOnlyList<ConfiguredHotkey>> Lists(
		params IReadOnlyList<ConfiguredHotkey>[] lists) => lists;

	private static int[] Sorted(IReadOnlySet<int> indexes)
	{
		var sorted = new List<int>(indexes);
		sorted.Sort();
		return sorted.ToArray();
	}
}
