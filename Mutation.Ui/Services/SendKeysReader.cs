#nullable enable
using System;
using System.Collections.Generic;

namespace Mutation.Ui.Services;

/// <summary>
/// Reads a Windows SendKeys string back into the chords it puts on the keyboard —
/// <c>^{F5}</c> into Ctrl+F5. The inverse of <see cref="SendKeysMapper"/>, which only ever
/// went the other way.
/// <para>
/// The two "Send key after…" boxes accept both spellings of a shortcut, and duplicate
/// detection understood only the plain one. Setting a sent key to <c>^{F5}</c> while a
/// Mutation shortcut is Ctrl+F5 raised no badge, though Windows routes that synthesized
/// keystroke straight back to Mutation and the action sets itself off again — the exact
/// clash the sent-against-registered check exists to catch (issue #326).
/// </para>
/// <para>
/// Everything here errs towards reading nothing. A missed clash leaves the user where they
/// already were; a wrong one puts a "Duplicate hotkey" badge on a shortcut that is perfectly
/// fine, which is a worse place to be. So a run of ordinary characters — text being typed,
/// not a shortcut — yields nothing, an unrecognised key name yields nothing, and a string
/// that does not hold together at all yields nothing whatsoever rather than a partial
/// reading of a value nobody can be sure of.
/// </para>
/// </summary>
public static class SendKeysReader
{
	private const char CtrlMod = '^';
	private const char ShiftMod = '+';
	private const char AltMod = '%';

	/// <summary>
	/// SendKeys' braced key names, spelled the way <see cref="Hotkey.Parse"/> wants them.
	/// Only names this table knows are read back; anything else is a key we cannot name with
	/// confidence, and guessing is what puts a badge on an innocent row.
	/// <para>
	/// The reserved single characters SendKeys escapes in braces — <c>{+}</c>, <c>{^}</c>,
	/// <c>{%}</c>, <c>{~}</c>, <c>{(}</c>, <c>{)}</c>, <c>{{}</c>, <c>{}}</c> — are absent on
	/// purpose. They are literal characters being typed, not named keys, and are treated as
	/// the ordinary literals they are.
	/// </para>
	/// </summary>
	private static readonly Dictionary<string, string> KeyNames = new(StringComparer.OrdinalIgnoreCase)
	{
		["ENTER"] = "Enter",
		["RETURN"] = "Enter",
		["TAB"] = "Tab",
		["ESC"] = "Escape",
		["ESCAPE"] = "Escape",
		["BACKSPACE"] = "Back",
		["BKSP"] = "Back",
		["BS"] = "Back",
		["DEL"] = "Delete",
		["DELETE"] = "Delete",
		["INS"] = "Insert",
		["INSERT"] = "Insert",
		["SPACE"] = "Space",
		["UP"] = "Up",
		["DOWN"] = "Down",
		["LEFT"] = "Left",
		["RIGHT"] = "Right",
		["HOME"] = "Home",
		["END"] = "End",
		["PGUP"] = "PageUp",
		["PGDN"] = "PageDown",
		["APPS"] = "Application",
		["BREAK"] = "Pause",
		["HELP"] = "Help",
		["CAPSLOCK"] = "CapitalLock",
		["NUMLOCK"] = "NumberKeyLock",
		["SCROLLLOCK"] = "Scroll",
		["PRTSC"] = "Snapshot",
		["ADD"] = "Add",
		["SUBTRACT"] = "Subtract",
		["MULTIPLY"] = "Multiply",
		["DIVIDE"] = "Divide",
		["DECIMAL"] = "Decimal",
		["SEPARATOR"] = "Separator",
	};

	/// <summary>
	/// The chords <paramref name="sendKeys"/> presses, in the order it presses them, or an
	/// empty list when it cannot be read back.
	/// </summary>
	public static IReadOnlyList<Hotkey> ChordsIn(string? sendKeys)
	{
		var chords = new List<Hotkey>();
		if (string.IsNullOrWhiteSpace(sendKeys))
			return chords;

		string s = sendKeys.Trim();
		int i = 0;

		while (i < s.Length)
		{
			var mods = ReadModifiers(s, ref i);
			if (i >= s.Length)
				return NothingAtAll(chords); // A modifier with no key after it: not a real value.

			char c = s[i];

			if (c == '(')
			{
				if (!TryReadGroup(s, ref i, mods, chords))
					return NothingAtAll(chords);
				continue;
			}

			if (c == '{')
			{
				if (!TryReadBraced(s, ref i, mods, chords))
					return NothingAtAll(chords);
				continue;
			}

			if (c == ')' || c == '}')
				return NothingAtAll(chords); // A closer with nothing open: the string is broken.

			// '~' is SendKeys' shorthand for Enter, and is a keypress however it is written.
			if (c == '~')
			{
				Add(chords, mods, "Enter");
				i++;
				continue;
			}

			Add(chords, mods, c.ToString(), literal: true);
			i++;
		}

		return chords;
	}

	/// <summary>
	/// Consumes the run of modifier characters at <paramref name="i"/>. In SendKeys they bind
	/// to whatever comes next and to nothing after that, so each pass round the loop starts
	/// its own run.
	/// </summary>
	private static (bool Ctrl, bool Shift, bool Alt) ReadModifiers(string s, ref int i)
	{
		bool ctrl = false, shift = false, alt = false;
		while (i < s.Length)
		{
			switch (s[i])
			{
				case CtrlMod: ctrl = true; break;
				case ShiftMod: shift = true; break;
				case AltMod: alt = true; break;
				default: return (ctrl, shift, alt);
			}
			i++;
		}
		return (ctrl, shift, alt);
	}

	/// <summary>
	/// The group form: <c>^+(ab)</c> holds Ctrl+Shift down across every key inside it, so it
	/// is Ctrl+Shift+A and then Ctrl+Shift+B rather than one chord naming both.
	/// </summary>
	private static bool TryReadGroup(string s, ref int i, (bool Ctrl, bool Shift, bool Alt) mods, List<Hotkey> chords)
	{
		int close = s.IndexOf(')', i + 1);
		if (close < 0)
			return false;

		string body = s[(i + 1)..close];
		i = close + 1;

		if (body.Length == 0)
			return false;

		// SendKeys neither nests groups nor re-applies modifiers inside one. Something in
		// there doing either is a string we do not understand, and half-understanding it is
		// how an innocent row gets flagged.
		if (body.IndexOfAny([')', '(', CtrlMod, ShiftMod, AltMod]) >= 0)
			return false;

		int j = 0;
		while (j < body.Length)
		{
			char c = body[j];

			if (c == '{')
			{
				if (!TryReadBraced(body, ref j, mods, chords))
					return false;
				continue;
			}

			if (c == '}')
				return false;

			if (c == '~')
			{
				Add(chords, mods, "Enter");
				j++;
				continue;
			}

			Add(chords, mods, c.ToString(), literal: true);
			j++;
		}

		return true;
	}

	/// <summary>
	/// One braced piece: a named key such as <c>{F5}</c>, optionally with the repeat count
	/// SendKeys allows (<c>{LEFT 5}</c>), or one of the reserved characters escaped into
	/// braces so it can be typed literally.
	/// </summary>
	private static bool TryReadBraced(string s, ref int i, (bool Ctrl, bool Shift, bool Alt) mods, List<Hotkey> chords)
	{
		// "{}}" is the closing brace as a literal, and its first '}' is content rather than
		// the closer. Spotted before the search below, which would otherwise stop on it and
		// read an empty pair.
		if (i + 2 < s.Length && s[i + 1] == '}' && s[i + 2] == '}')
		{
			Add(chords, mods, "}", literal: true);
			i += 3;
			return true;
		}

		int close = s.IndexOf('}', i + 1);
		if (close < 0)
			return false;

		string body = s[(i + 1)..close].Trim();
		i = close + 1;

		if (body.Length == 0)
			return false;

		// A repeat count presses the same key again, which adds nothing to a comparison
		// against other shortcuts, so only the key itself is kept.
		int space = body.IndexOf(' ');
		if (space > 0)
			body = body[..space];

		if (body.Length == 1)
		{
			// A single character in braces is an escaped literal — the key it types is the
			// character, not a named key.
			Add(chords, mods, body, literal: true);
			return true;
		}

		if (KeyNames.TryGetValue(body, out string? named))
		{
			Add(chords, mods, named);
			return true;
		}

		if (IsFunctionKey(body))
		{
			Add(chords, mods, body);
			return true;
		}

		// A name we do not know. The piece yields nothing; the rest of the string still
		// reads, the same way the finder skips one half-typed row without discarding the
		// others.
		return true;
	}

	private static bool IsFunctionKey(string body)
	{
		if (body.Length is < 2 or > 3) return false;
		if (body[0] is not ('F' or 'f')) return false;
		if (!int.TryParse(body.AsSpan(1), out int n)) return false;
		return n is >= 1 and <= 24;
	}

	/// <summary>
	/// Adds the chord for one keypress, or nothing when there is no chord in it.
	/// <para>
	/// An unmodified literal character is text being typed rather than a shortcut — a "send
	/// key after" value of <c>hello</c> types a word, and reading five one-key shortcuts out
	/// of it would flag rows that clash with nothing. With a modifier held it is a shortcut
	/// again, so <c>^v</c> counts and the <c>v</c> in <c>hello</c> does not.
	/// </para>
	/// </summary>
	private static void Add(List<Hotkey> chords, (bool Ctrl, bool Shift, bool Alt) mods, string keyToken, bool literal = false)
	{
		bool anyModifier = mods.Ctrl || mods.Shift || mods.Alt;
		if (literal && !anyModifier)
			return;

		string text = string.Concat(
			mods.Ctrl ? "CTRL+" : "",
			mods.Shift ? "SHIFT+" : "",
			mods.Alt ? "ALT+" : "",
			keyToken);

		// Parsed rather than assembled by hand so the key names agree with everywhere else
		// in the app, and so a key this reader has no business naming simply drops out.
		if (Hotkey.TryParse(text, out Hotkey? chord))
			chords.Add(chord);
	}

	private static IReadOnlyList<Hotkey> NothingAtAll(List<Hotkey> chords)
	{
		chords.Clear();
		return chords;
	}
}
