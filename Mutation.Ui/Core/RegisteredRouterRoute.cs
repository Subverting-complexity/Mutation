namespace Mutation.Ui.Core;

/// <summary>
/// One hotkey-router route the running app was asked to register, and how that went.
/// <para>
/// This is what the Hotkeys page compares its rows against. Registration happens once, on save,
/// and the page is editing a copy of the settings — so without something like this, a row has no
/// way to tell being live from merely being typed, which is the whole of issue #343.
/// </para>
/// <para>
/// <see cref="ErrorMessage"/> is set only when <see cref="Success"/> is false.
/// </para>
/// </summary>
public readonly record struct RegisteredRouterRoute(
	string? From,
	string? To,
	bool Success,
	string? ErrorMessage);
