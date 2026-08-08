using System;
using CognitiveSupport;

namespace Mutation.Ui.Core;

/// <summary>
/// One of the app's built-in shortcuts: what it is called, where its value lives in
/// <see cref="Settings"/>, and how it behaves.
/// </summary>
/// <param name="Registers">
/// Whether this shortcut is claimed from Windows. The "Send key after…" entries are typed
/// out to whichever app is in front rather than registered, so two of them holding the same
/// key is an ordinary way to set the app up and not a conflict — it used to be reported as
/// one (issue #306). They are still checked against the shortcuts Mutation does claim,
/// because Windows routes a synthesized keystroke back to whoever registered it.
/// </param>
internal sealed record HotkeySpec(
	string Label,
	Func<Settings, string?> Getter,
	Action<Settings, string?> Setter,
	bool AllowEmpty,
	string? Default = null,
	bool Registers = true)
{
	/// <summary>
	/// Whether this row's box also accepts SendKeys syntax, the way the same two values
	/// already do on the OCR and Speech pages. The Hotkeys page used to leave the flag off, so
	/// a working <c>^{F5}</c> was read out there as "Unsupported key" — on the page whose job
	/// is telling the user which shortcuts are wrong (issue #322).
	/// <para>
	/// It follows from <see cref="Registers"/> because the reason is the same one: a value
	/// that is typed out rather than claimed is a keystroke to send, and a keystroke to
	/// send is what SendKeys syntax spells. Derived rather than a flag of its own so the
	/// two cannot be set to disagree — a row that is not registered but must be a plain
	/// chord would need its own flag, and there is no such row.
	/// </para>
	/// </summary>
	public bool AllowsSendKeysSyntax => !Registers;

	/// <summary>
	/// The label with any parenthesised aside dropped, for sentences that name this shortcut
	/// rather than heading a row. "Send key after OCR (optional)" is the right header for a box
	/// the user may leave blank, and the wrong thing to read out inside "Already used by…".
	/// </summary>
	public string SpokenName
	{
		get
		{
			int aside = Label.IndexOf(" (", StringComparison.Ordinal);
			return aside < 0 ? Label : Label[..aside];
		}
	}
}
