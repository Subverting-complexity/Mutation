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

	private static int[] Sorted(IReadOnlySet<int> indexes)
	{
		var sorted = new List<int>(indexes);
		sorted.Sort();
		return sorted.ToArray();
	}
}
