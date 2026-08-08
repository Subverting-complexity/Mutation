using System;
using System.Collections.Generic;
using Mutation.Ui.Services;

namespace Mutation.Ui.Core;

/// <summary>
/// Which entries in a list of configured shortcuts clash with each other.
/// <para>
/// The Settings screen used to answer this by comparing the raw text case-insensitively,
/// while registration compares chords. <c>CTRL+ALT+G</c> and <c>Ctrl-Alt-G</c> are the same
/// shortcut and the same string to nobody, so Settings would accept a pair that registration
/// then refused, and the user found out from a failure dialog after saving (issue #306).
/// </para>
/// <para>
/// Kept as a pure list-in, indexes-out unit so it can be exercised without a settings page:
/// a duplicate reported here is a badge appearing beside a control, which nothing else in
/// the app would notice was wrong.
/// </para>
/// </summary>
internal static class HotkeyConflictFinder
{
	/// <summary>
	/// The positions in <paramref name="hotkeyTexts"/> holding a chord that also appears
	/// somewhere else in the list. Every member of a clashing group is reported, not just the
	/// later ones, because the user needs both rows flagged to know which two to compare.
	/// <para>
	/// Text that is blank or does not parse as a chord is skipped rather than matched on. It
	/// cannot be registered at all, so it cannot collide with anything — reporting two
	/// half-typed rows as duplicates of each other would be a second, wrong complaint on top
	/// of the validation message they already carry.
	/// </para>
	/// </summary>
	public static IReadOnlySet<int> DuplicateIndexes(IReadOnlyList<string?> hotkeyTexts)
	{
		if (hotkeyTexts is null) throw new ArgumentNullException(nameof(hotkeyTexts));

		var firstSeenAt = new Dictionary<Hotkey, int>();
		var duplicates = new HashSet<int>();

		for (int i = 0; i < hotkeyTexts.Count; i++)
		{
			if (!Hotkey.TryParse(hotkeyTexts[i], out var chord))
				continue;

			if (firstSeenAt.TryGetValue(chord, out int first))
			{
				duplicates.Add(first);
				duplicates.Add(i);
			}
			else
			{
				firstSeenAt[chord] = i;
			}
		}

		return duplicates;
	}
}
