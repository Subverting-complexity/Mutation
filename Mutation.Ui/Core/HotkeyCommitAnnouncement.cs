using System;

namespace Mutation.Ui.Core;

/// <summary>
/// What to say when a hotkey box rewrites itself, and when to say nothing at all.
/// <para>
/// Leaving a hotkey box canonicalises what is in it: <c>shift+ctrl+a</c> becomes
/// <c>CTRL+SHIFT+A</c>. A sighted user watches that happen. Nothing announced it, so a
/// screen-reader user heard only silence, and the next reading of the field disagreed with
/// what they had typed (issue #327).
/// </para>
/// <para>
/// Kept as a pure text-in, text-out unit because the alternative is a WinUI control that no
/// test can reach: the whole defect was an announcement that never happened, which is exactly
/// the kind of absence a running app does not complain about.
/// </para>
/// </summary>
internal static class HotkeyCommitAnnouncement
{
	/// <summary>
	/// The message for a commit, or null when there is nothing worth interrupting for.
	/// </summary>
	/// <param name="header">
	/// The box's on-screen label, used to name which row this is about. Every hotkey row
	/// looks the same to someone tabbing down seventeen of them, and by the time the
	/// announcement lands the focus has already moved on. Blank for a box with no header,
	/// which drops the name rather than inventing one.
	/// </param>
	/// <param name="typed">What was in the box when focus left it.</param>
	/// <param name="committed">What the box holds now.</param>
	/// <param name="validationMessage">
	/// Whatever the validator has to say about <paramref name="committed"/>, or null when it
	/// is happy. An error outranks this: being told the shortcut is unusable matters more
	/// than being told how it is now spelled, and the two would otherwise talk over each
	/// other in the same breath.
	/// </param>
	public static string? For(string? header, string? typed, string? committed, string? validationMessage)
	{
		if (!string.IsNullOrWhiteSpace(validationMessage))
			return null;

		// Nothing changed, so there is nothing to report. This is the common case by far —
		// tabbing through rows that are already canonical has to stay silent, or the page
		// talks over the user on every single row.
		//
		// Space either side of the text does not count as a change. It is invisible to a
		// screen reader, so the field does not disagree with what the user typed, and there
		// is no shortcut in it either way. It matters most for the two "send key after"
		// boxes, whose contents are kept exactly as typed: trimming is the only rewrite they
		// ever get, and announcing it would read SendKeys punctuation aloud — "^{F5}" is
		// spoken as bare "F5" at most screen readers' default punctuation level, naming a
		// different shortcut from the one in the box.
		if (string.Equals((typed ?? string.Empty).Trim(), committed ?? string.Empty, StringComparison.Ordinal))
			return null;

		// An emptied box is the Clear button doing exactly what it was asked to, and it
		// carries its own "Enter a hotkey." message already.
		if (string.IsNullOrEmpty(committed))
			return null;

		string name = SpokenName(header);
		return name.Length == 0
			? $"Now reads {committed}."
			: $"{name} now reads {committed}.";
	}

	/// <summary>
	/// The row's label with any trailing aside dropped. Several rows are labelled
	/// "Send key after OCR (optional)", and "Send key after OCR (optional) now reads ^{F5}"
	/// is a mouthful to listen to for a word that says nothing about what changed.
	/// </summary>
	private static string SpokenName(string? header)
	{
		string name = header?.Trim() ?? string.Empty;
		if (name.EndsWith(')'))
		{
			int open = name.LastIndexOf('(');
			if (open > 0)
				name = name[..open].TrimEnd();
		}

		return name;
	}
}
