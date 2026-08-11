namespace Mutation.Ui.Core;

/// <summary>
/// What the app knows about one hotkey-router route: whether the running app is listening for
/// it, whether it tried and could not, or whether there is nothing to say yet.
/// <para>
/// The old shape had three states and called the third one <c>Inactive</c>, which the row
/// reported as "Hotkey is not currently bound." That was the lie behind issue #343 — the
/// Settings page never learned any registration outcome at all, so every row, working ones
/// included, sat in that state. The states below separate not knowing from knowing the answer
/// is no, and add the one the Settings page is usually in: a perfectly good mapping the running
/// app has not been handed yet, because it is handed them on save.
/// </para>
/// </summary>
public enum HotkeyBindingState
{
	/// <summary>
	/// Nothing is known. No registration outcome is available, or the row is not a usable
	/// mapping yet, so the row says nothing about being live rather than guessing.
	/// </summary>
	Unknown,

	/// <summary>The running app is listening for this route, and it works.</summary>
	Bound,

	/// <summary>Registration was attempted and refused. The reason travels with it.</summary>
	Failed,

	/// <summary>
	/// A valid mapping that the running app has not been given. Editing an existing row or
	/// adding a new one puts it here until the settings are saved.
	/// </summary>
	NotYetApplied,
}
