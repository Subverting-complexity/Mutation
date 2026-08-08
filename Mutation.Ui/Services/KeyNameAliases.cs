#nullable enable
using System;
using System.Collections.Generic;

namespace Mutation.Ui.Services;

/// <summary>
/// The short names people write for keys, and the one spelling the app resolves each of them
/// to. <see cref="Hotkey.Parse"/> and <see cref="SendKeysMapper"/> both go through here, so a
/// key has one vocabulary across the shortcut editors and the "send key after…" boxes.
/// <para>
/// It used to have two. <c>Alt+PgDn</c> was accepted and sent by a send-key box and rejected
/// by a shortcut editor with "Unsupported key 'PGDN'", and nothing on screen explained why
/// (issue #329). That is the same second-vocabulary problem #306 removed for whole chords,
/// one level down at key names.
/// </para>
/// </summary>
internal static class KeyNameAliases
{
	/// <summary>
	/// Alias to canonical name. Every value is a <see cref="Windows.System.VirtualKey"/>
	/// member name, which is what makes it a spelling <see cref="Hotkey.Parse"/> can already
	/// resolve; no key is one, which is what stops an alias quietly redefining a name that
	/// means something else today. <c>KeyNameAliasesTests</c> pins both halves of that.
	/// </summary>
	private static readonly Dictionary<string, string> Aliases = new(StringComparer.OrdinalIgnoreCase)
	{
		// Control keys
		["RETURN"] = "Enter",
		["ESC"] = "Escape",
		["BACKSPACE"] = "Back",
		["BKSP"] = "Back",
		["BS"] = "Back",
		["DEL"] = "Delete",
		["INS"] = "Insert",
		["SPACEBAR"] = "Space",
		["BREAK"] = "Pause",

		// Navigation
		["UPARROW"] = "Up",
		["ARROWUP"] = "Up",
		["DOWNARROW"] = "Down",
		["ARROWDOWN"] = "Down",
		["LEFTARROW"] = "Left",
		["ARROWLEFT"] = "Left",
		["RIGHTARROW"] = "Right",
		["ARROWRIGHT"] = "Right",
		["PGUP"] = "PageUp",
		["PGDN"] = "PageDown",
		["PAGEDN"] = "PageDown",

		// Context menu
		["APPS"] = "Application",
		["CONTEXTMENU"] = "Application",

		// Toggles
		["CAPS"] = "CapitalLock",
		["CAPSLOCK"] = "CapitalLock",
		["NUMLOCK"] = "NumberKeyLock",
		["SCROLLLOCK"] = "Scroll",
	};

	/// <summary>
	/// The canonical name for <paramref name="keyName"/>, or <paramref name="keyName"/>
	/// itself when it is not an alias — including when it is already canonical, and when it
	/// is not a key name at all. Callers can therefore apply this to every token without
	/// first asking whether it is one.
	/// </summary>
	internal static string Resolve(string keyName) =>
		Aliases.TryGetValue(keyName, out string? canonical) ? canonical : keyName;

	/// <summary>Every alias, for the tests that check the table's shape.</summary>
	internal static IReadOnlyDictionary<string, string> All => Aliases;
}
