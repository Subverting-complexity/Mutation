using System;
using System.Collections.Generic;
using Mutation.Ui.Services;

namespace Mutation.Ui.Core;

/// <summary>
/// Which entries in a list of configured shortcuts clash with each other.
/// <para>
/// The Settings screen used to answer this by comparing the raw text case-insensitively,
/// while registration compares chords. Two spellings of one shortcut are the same shortcut
/// and different strings, so Settings would accept a pair that registration then refused,
/// and the user found out from a failure dialog after saving (issue #306).
/// </para>
/// <para>
/// Kept as a pure list-in, indexes-out unit so it can be exercised without a settings page:
/// a duplicate reported here is a badge appearing beside a control, which nothing else in
/// the app would notice was wrong.
/// </para>
/// </summary>
internal static class HotkeyConflictFinder
{
	/// <summary>One configured shortcut, and whether it is claimed from Windows.</summary>
	/// <param name="Text">Whatever the user has typed, in any spelling.</param>
	/// <param name="ClaimsTheChord">
	/// True for a shortcut Mutation registers with Windows, false for one it types out to
	/// whichever app is in front — the "Send key after…" entries. Two sent keys cannot clash
	/// with each other, but a sent key that matches a registered one does: Windows routes the
	/// synthesized keystroke back to Mutation, so the action fires itself again.
	/// <para>
	/// It also decides how the text is read. A registered shortcut is one chord, and a comma
	/// inside it is just another separator — <c>CTRL,A</c> registers as Ctrl+A. A sent value
	/// may be a comma-separated <em>sequence</em> of chords, so it is split first and every
	/// chord in it counts.
	/// </para>
	/// </param>
	public readonly record struct ConfiguredHotkey(string? Text, bool ClaimsTheChord);

	/// <summary>
	/// The positions in <paramref name="configured"/> holding a shortcut that clashes with
	/// another one in the list. Every member of a clashing group is reported, not just the
	/// later ones, because the user needs both rows flagged to know which two to compare.
	/// <para>
	/// A group clashes when it holds more than one entry and at least one of them is claimed
	/// from Windows. That covers registered-against-registered and sent-against-registered,
	/// and leaves out sent-against-sent — sending the same key after OCR and after
	/// transcription is an ordinary way to set the app up, and used to be reported as a
	/// conflict it never was.
	/// </para>
	/// <para>
	/// Text that is blank or does not parse as a chord is skipped rather than matched on. It
	/// cannot be registered or sent as a chord at all, so it cannot collide with anything —
	/// reporting two half-typed rows as duplicates of each other would be a second, wrong
	/// complaint on top of the validation message they already carry.
	/// </para>
	/// </summary>
	public static IReadOnlySet<int> DuplicateIndexes(IReadOnlyList<ConfiguredHotkey> configured)
	{
		if (configured is null) throw new ArgumentNullException(nameof(configured));

		var groups = new Dictionary<Hotkey, HashSet<int>>();
		var claimed = new HashSet<Hotkey>();

		for (int i = 0; i < configured.Count; i++)
		{
			var entry = configured[i];
			foreach (var chord in ChordsIn(entry))
			{
				if (!groups.TryGetValue(chord, out var members))
					groups[chord] = members = new HashSet<int>();

				// A set, so a sequence naming one chord twice does not look like two rows.
				members.Add(i);

				if (entry.ClaimsTheChord)
					claimed.Add(chord);
			}
		}

		var duplicates = new HashSet<int>();
		foreach (var (chord, members) in groups)
		{
			if (members.Count > 1 && claimed.Contains(chord))
				duplicates.UnionWith(members);
		}

		return duplicates;
	}

	/// <summary>
	/// Every chord one entry puts on the keyboard: one for a registered shortcut, and one per
	/// step for a sent sequence. Half-typed text yields nothing, because there is no chord to
	/// compare.
	/// <para>
	/// A sent step is read as a chord first and as Windows' SendKeys shorthand only if that
	/// fails, which is the order the ambiguity needs: the <c>+</c> in <c>Ctrl+F5</c> separates
	/// a chord, while the one in <c>+{F5}</c> is SendKeys' Shift. Reading the shorthand at all
	/// is new — it used to yield nothing, so <c>^{F5}</c> silently took no part in the
	/// comparison while <c>Ctrl+F5</c> was flagged correctly (issue #326).
	/// </para>
	/// </summary>
	private static IEnumerable<Hotkey> ChordsIn(ConfiguredHotkey entry)
	{
		if (entry.ClaimsTheChord)
		{
			if (Hotkey.TryParse(entry.Text, out var chord))
				yield return chord;
			yield break;
		}

		if (string.IsNullOrWhiteSpace(entry.Text))
			yield break;

		foreach (string step in entry.Text.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
		{
			if (Hotkey.TryParse(step, out var chord))
			{
				yield return chord;
				continue;
			}

			foreach (var sent in SendKeysReader.ChordsIn(step))
				yield return sent;
		}
	}
}
