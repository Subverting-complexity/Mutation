using System;
using System.Collections.Generic;
using System.Linq;

namespace Mutation.Ui.Core;

/// <summary>
/// What to tell someone whose shortcut is already taken, and by what.
/// <para>
/// The Hotkeys page can say "Duplicate hotkey" and leave it there, because both ends of the
/// clash are rows on the same screen and the user only has to look up. Neither the prompt
/// editor nor a prompt's clash partner has that: a prompt is edited in its own window, so a
/// bare "duplicate" sends the user hunting for something that is not in front of them
/// (issues #336, #340).
/// </para>
/// </summary>
internal static class HotkeyClashDescription
{
	/// <summary>
	/// A sentence naming everything already holding <paramref name="hotkey"/>, or null when it
	/// is free. Blank and half-typed text is free by definition — it cannot be registered, so
	/// it cannot be taken.
	/// </summary>
	public static string? For(
		string? hotkey,
		IReadOnlyList<NamedHotkey> claimedElsewhere,
		bool claimsTheChord = true)
	{
		var names = NamesHolding(hotkey, claimedElsewhere, claimsTheChord);
		return names.Count == 0 ? null : $"Already used by {JoinNames(names)}.";
	}

	/// <summary>
	/// The names in <paramref name="claimedElsewhere"/> whose shortcut is the same chord as
	/// <paramref name="hotkey"/>, in the order they were given, without repeats.
	/// <para>
	/// Asked one at a time rather than all at once, and that is the point.
	/// <see cref="HotkeyConflictFinder.DuplicateIndexesAcross"/> answers "which entries clash
	/// with <em>something</em>", which is the right question for a page that flags every row —
	/// but it would also hand back two other prompts clashing with each other, and neither of
	/// them is an answer to "what is holding mine". A list of one against a list of one asks
	/// only about this pair, and the exclusions the finder settled in #306 and #320 — a sent
	/// key is not claimed, unparseable text is not a chord — still decide each answer.
	/// </para>
	/// </summary>
	/// <param name="claimsTheChord">
	/// Whether <paramref name="hotkey"/> is claimed from Windows. True for a prompt's shortcut,
	/// which is what the prompt editor asks about. False for a "Send key after…" value, which
	/// is read differently — it may be a comma-separated sequence, and it may be spelled in
	/// SendKeys shorthand. Left at true, <c>^{F5}</c> in such a box parses as nothing at all
	/// and the answer comes back empty rather than naming what holds it.
	/// </param>
	public static IReadOnlyList<string> NamesHolding(
		string? hotkey,
		IReadOnlyList<NamedHotkey> claimedElsewhere,
		bool claimsTheChord = true)
	{
		if (claimedElsewhere is null) throw new ArgumentNullException(nameof(claimedElsewhere));

		var names = new List<string>();
		if (string.IsNullOrWhiteSpace(hotkey))
			return names;

		var mine = new[] { new HotkeyConflictFinder.ConfiguredHotkey(hotkey, claimsTheChord) };

		foreach (var other in claimedElsewhere)
		{
			var across = HotkeyConflictFinder.DuplicateIndexesAcross(
				new IReadOnlyList<HotkeyConflictFinder.ConfiguredHotkey>[] { mine, new[] { other.Configured } });

			// One name per thing that holds it. Several routes can share a name, and reading
			// "a hotkey route and a hotkey route" says nothing the first half did not.
			if (across[0].Contains(0) && !names.Contains(other.Name, StringComparer.Ordinal))
				names.Add(other.Name);
		}

		return names;
	}

	/// <summary>
	/// "A", "A and B", "A, B and C" — read aloud, so the last pair is joined with a word
	/// rather than a comma the reader would run straight through.
	/// </summary>
	private static string JoinNames(IReadOnlyList<string> names)
	{
		if (names.Count == 1)
			return names[0];

		string last = names[names.Count - 1];
		string rest = string.Join(", ", names.Take(names.Count - 1));
		return $"{rest} and {last}";
	}
}
