#nullable enable
using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using Windows.System;

namespace Mutation.Ui.Services;

/// <summary>
/// Reads a chord written in WinForms SendKeys notation — <c>^{DEL}</c>, <c>%{F4}</c>,
/// <c>^+{TAB}</c>, <c>^c</c> — back into a <see cref="Hotkey"/>.
/// <para>
/// This is the inverse of <see cref="SendKeysMapper"/>, and it exists so that the two
/// "send this shortcut afterwards" settings can be delivered the way every other chord
/// is: through SendInput, which reports whether the keystrokes were accepted. Without it
/// a setting saved in SendKeys notation could only ever reach
/// <c>System.Windows.Forms.SendKeys.SendWait</c>, which answers nothing about whether the
/// target window took them — so a shortcut that quietly stopped arriving looked exactly
/// like one that worked (PR #328).
/// </para>
/// <para>
/// Deliberately narrow. Only modifier prefixes and a single key are recognised; anything
/// with grouping, repeat counts, <c>~</c>, or literal text to type is left to the WinForms
/// fallback rather than guessed at, because guessing wrong types characters into whatever
/// the user is working in.
/// </para>
/// </summary>
internal static class SendKeysChord
{
	private const char CtrlModifier = '^';
	private const char ShiftModifier = '+';
	private const char AltModifier = '%';

	/// <summary>
	/// The braced key names <see cref="SendKeysMapper"/> emits, read back. Kept in step with
	/// that map: a name it can produce and this cannot would silently drop back to the
	/// fallback path rather than fail, which is the failure mode this type exists to remove.
	/// </summary>
	private static readonly Dictionary<string, VirtualKey> BracedKeys = new(StringComparer.OrdinalIgnoreCase)
	{
		["ENTER"] = VirtualKey.Enter,
		["TAB"] = VirtualKey.Tab,
		["ESC"] = VirtualKey.Escape,
		["ESCAPE"] = VirtualKey.Escape,
		["BACKSPACE"] = VirtualKey.Back,
		["BKSP"] = VirtualKey.Back,
		["BS"] = VirtualKey.Back,
		["DEL"] = VirtualKey.Delete,
		["DELETE"] = VirtualKey.Delete,
		["INS"] = VirtualKey.Insert,
		["INSERT"] = VirtualKey.Insert,
		["SPACE"] = VirtualKey.Space,
		["UP"] = VirtualKey.Up,
		["DOWN"] = VirtualKey.Down,
		["LEFT"] = VirtualKey.Left,
		["RIGHT"] = VirtualKey.Right,
		["HOME"] = VirtualKey.Home,
		["END"] = VirtualKey.End,
		["PGUP"] = VirtualKey.PageUp,
		["PGDN"] = VirtualKey.PageDown,
		["APPS"] = VirtualKey.Application,
		["BREAK"] = VirtualKey.Pause,
		["HELP"] = VirtualKey.Help,
		["CAPSLOCK"] = VirtualKey.CapitalLock,
		["NUMLOCK"] = VirtualKey.NumberKeyLock,
		["SCROLLLOCK"] = VirtualKey.Scroll,
		["ADD"] = VirtualKey.Add,
		["SUBTRACT"] = VirtualKey.Subtract,
		["MULTIPLY"] = VirtualKey.Multiply,
		["DIVIDE"] = VirtualKey.Divide,
		["DECIMAL"] = VirtualKey.Decimal,
		["SEPARATOR"] = VirtualKey.Separator,
	};

	/// <summary>
	/// Whether <paramref name="text"/> is a SendKeys chord this can send, and the chord it
	/// spells. False leaves the caller to its own fallback; it never throws.
	/// </summary>
	public static bool TryParse(string? text, [NotNullWhen(true)] out Hotkey? hotkey)
	{
		hotkey = null;

		var chord = (text ?? string.Empty).Trim();
		if (chord.Length == 0)
			return false;

		bool control = false, shift = false, alt = false;
		int i = 0;
		for (; i < chord.Length; i++)
		{
			switch (chord[i])
			{
				case CtrlModifier: control = true; continue;
				case ShiftModifier: shift = true; continue;
				case AltModifier: alt = true; continue;
			}
			break;
		}

		// A bare key with no modifier at all is not SendKeys notation unless it is braced.
		// "c" on its own is a chord Hotkey.Parse already handles, and reading it here would
		// take the same text down two different paths depending on which ran first.
		bool hasModifier = control || shift || alt;
		var remainder = chord[i..];
		if (remainder.Length == 0)
			return false;

		if (!TryReadKey(remainder, out var key, out bool wasBraced))
			return false;

		if (!hasModifier && !wasBraced)
			return false;

		hotkey = new Hotkey { Control = control, Shift = shift, Alt = alt, Key = key };
		return true;
	}

	private static bool TryReadKey(string remainder, out VirtualKey key, out bool wasBraced)
	{
		key = VirtualKey.None;
		wasBraced = false;

		if (remainder[0] == '{')
		{
			// Exactly one braced group and nothing after it. "{DEL}{DEL}" is a repeat, and
			// "{DEL 2}" is a count — both are sequences rather than chords, and neither is
			// something a single SendInput chord can express.
			if (remainder[^1] != '}' || remainder.IndexOf('}') != remainder.Length - 1)
				return false;

			var name = remainder[1..^1].Trim();
			if (name.Length == 0)
				return false;

			wasBraced = true;

			if (BracedKeys.TryGetValue(name, out key))
				return true;

			if (TryReadFunctionKey(name, out key))
				return true;

			// A braced escape of a reserved character — {+}, {^}, {%}, {(} — is a literal
			// keystroke whose virtual key depends on the keyboard layout. Left to the
			// fallback rather than mapped to whatever a US layout would use.
			return false;
		}

		if (remainder.Length != 1)
			return false;

		return TryReadCharacter(remainder[0], out key);
	}

	private static bool TryReadFunctionKey(string name, out VirtualKey key)
	{
		key = VirtualKey.None;
		if (name.Length is < 2 or > 3 || (name[0] != 'F' && name[0] != 'f'))
			return false;

		if (!int.TryParse(name.AsSpan(1), out int number) || number is < 1 or > 24)
			return false;

		key = VirtualKey.F1 + (number - 1);
		return true;
	}

	private static bool TryReadCharacter(char character, out VirtualKey key)
	{
		key = VirtualKey.None;

		if (char.IsAsciiLetter(character))
		{
			key = VirtualKey.A + (char.ToUpperInvariant(character) - 'A');
			return true;
		}

		if (char.IsAsciiDigit(character))
		{
			key = VirtualKey.Number0 + (character - '0');
			return true;
		}

		// Punctuation is layout-dependent — ';' is not the same physical key everywhere —
		// so it goes to the fallback rather than to a guess.
		return false;
	}
}
