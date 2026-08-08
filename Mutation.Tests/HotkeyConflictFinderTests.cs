using System;
using System.Collections.Generic;
using Mutation.Ui.Core;

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
	private static IReadOnlyList<string?> Configured(params string?[] hotkeys) => hotkeys;

	[Fact]
	public void Nothing_configured_conflicts_with_nothing()
	{
		Assert.Empty(HotkeyConflictFinder.DuplicateIndexes(Configured()));
	}

	[Fact]
	public void Distinct_shortcuts_do_not_conflict()
	{
		Assert.Empty(HotkeyConflictFinder.DuplicateIndexes(
			Configured("CTRL+ALT+G", "CTRL+ALT+H", "SHIFT+F1")));
	}

	[Fact]
	public void The_same_shortcut_twice_flags_both_rows()
	{
		// Both, not just the second: the user has to be told which two rows to compare.
		Assert.Equal(new[] { 0, 2 }, Sorted(HotkeyConflictFinder.DuplicateIndexes(
			Configured("CTRL+ALT+G", "CTRL+ALT+H", "CTRL+ALT+G"))));
	}

	[Fact]
	public void Two_spellings_of_one_shortcut_conflict()
	{
		// The case this issue is about. "Ctrl-Alt-G" and "CTRL+ALT+G" are one shortcut to
		// Windows and two different strings to the old text comparison.
		Assert.Equal(new[] { 0, 1 }, Sorted(HotkeyConflictFinder.DuplicateIndexes(
			Configured("CTRL+ALT+G", "Ctrl-Alt-G"))));
	}

	[Fact]
	public void Modifier_order_does_not_make_two_shortcuts()
	{
		Assert.Equal(new[] { 0, 1 }, Sorted(HotkeyConflictFinder.DuplicateIndexes(
			Configured("CTRL+SHIFT+A", "SHIFT+CTRL+A"))));
	}

	[Fact]
	public void A_modifier_alias_does_not_make_two_shortcuts()
	{
		Assert.Equal(new[] { 0, 1 }, Sorted(HotkeyConflictFinder.DuplicateIndexes(
			Configured("CONTROL+WINDOWS+K", "CTRL+WIN+K"))));
	}

	[Fact]
	public void Three_rows_on_one_shortcut_are_all_flagged()
	{
		Assert.Equal(new[] { 0, 1, 3 }, Sorted(HotkeyConflictFinder.DuplicateIndexes(
			Configured("CTRL+ALT+G", "ctrl+alt+g", "CTRL+ALT+H", "Ctrl Alt G"))));
	}

	[Theory]
	[InlineData(null)]
	[InlineData("")]
	[InlineData("   ")]
	public void Unset_shortcuts_never_conflict(string? blank)
	{
		// Several hotkeys are optional and left empty. Two empties are not a clash.
		Assert.Empty(HotkeyConflictFinder.DuplicateIndexes(Configured(blank, blank, blank)));
	}

	[Fact]
	public void Half_typed_text_never_conflicts()
	{
		// Text that does not parse cannot be registered, so it cannot collide with anything.
		// Flagging it as a duplicate would be a second, wrong complaint on top of the
		// validation message the row already shows.
		Assert.Empty(HotkeyConflictFinder.DuplicateIndexes(Configured("CTRL+", "CTRL+", "Ctrl+Shift")));
	}

	[Fact]
	public void A_real_shortcut_still_conflicts_alongside_unparseable_rows()
	{
		Assert.Equal(new[] { 1, 3 }, Sorted(HotkeyConflictFinder.DuplicateIndexes(
			Configured("CTRL+", "CTRL+ALT+G", null, "ctrl-alt-g"))));
	}

	[Fact]
	public void A_missing_list_is_refused_rather_than_treated_as_empty()
	{
		Assert.Throws<ArgumentNullException>(() => HotkeyConflictFinder.DuplicateIndexes(null!));
	}

	private static int[] Sorted(IReadOnlySet<int> indexes)
	{
		var sorted = new List<int>(indexes);
		sorted.Sort();
		return sorted.ToArray();
	}
}
