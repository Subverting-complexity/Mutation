using Mutation.Ui.Services;

namespace Mutation.Ui.Core;

/// <summary>
/// A configured shortcut together with the name the user would recognise it by.
/// <para>
/// <see cref="HotkeyConflictFinder"/> answers in positions, which is all a page of rows needs
/// — it flags the row and the user reads the header beside it. The prompt editor has no such
/// row: the thing it clashes with is on another screen entirely, so the warning has to say
/// what it is (issue #336). The name travels with the shortcut so the wording is decided in
/// one place instead of at each screen that reports a clash.
/// </para>
/// </summary>
/// <param name="Name">
/// Read out inside a sentence — "Already used by <em>Speak clipboard</em>." — so it is phrased
/// as a thing rather than as a row header, and carries its own article where it needs one.
/// </param>
internal readonly record struct NamedHotkey(string Name, string? Text, bool ClaimsTheChord)
{
	public HotkeyConflictFinder.ConfiguredHotkey Configured => new(Text, ClaimsTheChord);
}
