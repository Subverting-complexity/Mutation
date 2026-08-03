#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;

namespace Mutation.Ui.Services;

public static class SendKeysMapper
{
	// Modifiers in canonical order for stable output
	private const char CtrlMod = '^';
	private const char ShiftMod = '+';
	private const char AltMod = '%';

	// Separators accepted inside a parenthesised group: "(Enter Tab)". Commas are not
	// included — SplitByComma has already divided the input into chords by then.
	private static readonly char[] GroupSeparators = { ' ', '\t' };

	// Unsupported keys in WinForms SendKeys
	private static readonly HashSet<string> UnsupportedKeys = new(StringComparer.OrdinalIgnoreCase)
	{
		"WIN", "WINDOWS", "CMD", "COMMAND", "META", "SUPER", "PRINTSCREEN", "PRTSC", "PRTSCR", "SYSRQ"
	};

	// Map of normalized tokens (letters/digits only, no spaces/dashes/underscores) to SendKeys pieces.
	// Use braces for action keys; single-char literals returned as-is; reserved chars are escaped later.
	private static readonly Dictionary<string, string> KeyMap = new(StringComparer.OrdinalIgnoreCase)
	{
		// Control keys
		["ENTER"] = "{ENTER}",
		["RETURN"] = "{ENTER}",
		["TAB"] = "{TAB}",
		["ESC"] = "{ESC}",
		["ESCAPE"] = "{ESC}",
		["BACKSPACE"] = "{BACKSPACE}",
		["BKSP"] = "{BACKSPACE}",
		["BS"] = "{BACKSPACE}",
		["DELETE"] = "{DEL}",
		["DEL"] = "{DEL}",
		["INSERT"] = "{INS}",
		["INS"] = "{INS}",
		["SPACE"] = "{SPACE}",
		["SPACEBAR"] = "{SPACE}",

		// Navigation
		["UP"] = "{UP}",
		["UPARROW"] = "{UP}",
		["ARROWUP"] = "{UP}",
		["DOWN"] = "{DOWN}",
		["DOWNARROW"] = "{DOWN}",
		["ARROWDOWN"] = "{DOWN}",
		["LEFT"] = "{LEFT}",
		["LEFTARROW"] = "{LEFT}",
		["ARROWLEFT"] = "{LEFT}",
		["RIGHT"] = "{RIGHT}",
		["RIGHTARROW"] = "{RIGHT}",
		["ARROWRIGHT"] = "{RIGHT}",
		["HOME"] = "{HOME}",
		["END"] = "{END}",
		["PGUP"] = "{PGUP}",
		["PAGEUP"] = "{PGUP}",
		["PAGEDOWN"] = "{PGDN}",
		["PAGEDN"] = "{PGDN}",
		["PGDN"] = "{PGDN}",

		// Editing/context
		["APPS"] = "{APPS}",
		["CONTEXTMENU"] = "{APPS}",
		["MENU"] = "{APPS}",
		["BREAK"] = "{BREAK}",
		["HELP"] = "{HELP}",

		// Toggles
		["CAPSLOCK"] = "{CAPSLOCK}",
		["CAPS"] = "{CAPSLOCK}",
		["NUMLOCK"] = "{NUMLOCK}",
		["SCROLLLOCK"] = "{SCROLLLOCK}",
		["SCROLL"] = "{SCROLLLOCK}",

		// Numeric keypad (explicit names)
		["ADD"] = "{ADD}",
		["SUBTRACT"] = "{SUBTRACT}",
		["MULTIPLY"] = "{MULTIPLY}",
		["DIVIDE"] = "{DIVIDE}",
		["DECIMAL"] = "{DECIMAL}",
		["SEPARATOR"] = "{SEPARATOR}",

		// Common symbol names → literals (escaped later if reserved)
		["PLUS"] = "+",
		["MINUS"] = "-",
		["DASH"] = "-",
		["HYPHEN"] = "-",
		["EQUAL"] = "=",
		["EQUALS"] = "=",
		["TILDE"] = "~",
		["CARET"] = "^",
		["PERCENT"] = "%",
		["LBRACKET"] = "[",
		["LEFTBRACKET"] = "[",
		["RBRACKET"] = "]",
		["RIGHTBRACKET"] = "]",
		["SEMICOLON"] = ";",
		["APOSTROPHE"] = "'",
		["QUOTE"] = "\"",
		["DQUOTE"] = "\"",
		["BACKQUOTE"] = "`",
		["GRAVE"] = "`",
		["BACKTICK"] = "`",
		["BACKSLASH"] = "\\",
		["SLASH"] = "/",
		["FORWARDSLASH"] = "/",
		["COMMA"] = ",",
		["PERIOD"] = ".",
		["DOT"] = ".",
		["PIPE"] = "|",
		["LESSTHAN"] = "<",
		["GREATERTHAN"] = ">"
	};

	public static string Map(string input)
	{
		if (input is null)
			throw new ArgumentNullException(nameof(input), "Input cannot be null.");
		input = input.Trim();
		if (input.Length == 0)
			throw new ArgumentException("Input cannot be empty.", nameof(input));

		if (LooksLikeSendKeys(input))
			return input;

		var parts = SplitByComma(input);
		var result = new System.Text.StringBuilder(input.Length * 2);

		foreach (var rawPart in parts)
		{
			var chord = rawPart.Trim();
			if (chord.Length == 0)
				continue;

			result.Append(MapSingleChord(chord));
		}

		return result.ToString();
	}

	private static string MapSingleChord(string chord)
	{
		var (tokens, plusKeyPositions) = TokenizeByPlus(chord);

		var hasCtrl = false;
		var hasShift = false;
		var hasAlt = false;
		var keys = new List<string>(capacity: Math.Max(1, tokens.Count));

		var unknowns = new List<string>();

		foreach (var (token, isPlusLiteral) in EnumerateTokens(tokens, plusKeyPositions))
		{
			var norm = Normalize(token);

			if (IsCtrl(norm))
			{
				hasCtrl = true;
				continue;
			}
			if (IsShift(norm))
			{
				hasShift = true;
				continue;
			}
			if (IsAlt(norm))
			{
				hasAlt = true;
				continue;
			}
			if (IsAltGr(norm))
			{
				hasCtrl = true;
				hasAlt = true;
				continue;
			}

			// Unsupported (Windows/Cmd/PrintScreen)
			if (UnsupportedKeys.Contains(norm))
			{
				throw new NotSupportedException($"'{token}' is not supported by Windows Forms SendKeys.");
			}

			// Explicit plus literal (Ctrl++ cases)
			if (isPlusLiteral)
			{
				keys.Add(EscapeIfReserved("+"));
				continue;
			}

			// A parenthesised run like "(AB)" is how a human writes the group form of
			// "A+B" — expand it into its individual keys so the chord's modifiers apply
			// to the whole group, exactly as they do for "Ctrl+A+B".
			if (TryExpandKeyGroup(token, out var groupedKeys))
			{
				keys.AddRange(groupedKeys);
				continue;
			}

			if (TryResolveKey(token, out var key))
			{
				keys.Add(key);
				continue;
			}

			unknowns.Add(token);
		}

		if (unknowns.Count > 0)
		{
			throw new FormatException($"Unknown key name(s): {string.Join(", ", unknowns)} in '{chord}'. " +
				"Try common names like Delete, Enter, PgDn, ArrowUp, F5, etc.");
		}

		if (keys.Count == 0)
			throw new FormatException($"No primary key specified in '{chord}'. Add a key after the modifier(s), e.g. 'Ctrl+C'.");

		// Build modifiers prefix (stable order Ctrl, Shift, Alt)
		var prefix = new System.Text.StringBuilder(3);
		if (hasCtrl) prefix.Append(CtrlMod);
		if (hasShift) prefix.Append(ShiftMod);
		if (hasAlt) prefix.Append(AltMod);

		// If multiple keys were provided in one chord, apply modifiers to the group
		if (keys.Count == 1)
			return prefix + keys[0];

		// Group apply: ^+(ab) where a/b are already escaped/braced as needed
		var body = string.Concat(keys);
		return prefix + "(" + body + ")";
	}

	private static bool LooksLikeSendKeys(string s)
	{
		// Heuristic: any of these strongly implies SendKeys syntax
		// '^', '%', '~', braces, or a group opened by SendKeys' Shift modifier.
		for (int i = 0; i < s.Length; i++)
		{
			var c = s[i];
			if (c == '^' || c == '%' || c == '~' || c == '{' || c == '}')
				return true;

			// '+(' is ambiguous: in SendKeys the '+' is the Shift modifier opening a
			// group, but in a human-written chord like "Ctrl+(AB)" it is the separator
			// between the modifier name and the group. Reading the latter as SendKeys
			// passed the string through untranslated and typed the literal text "Ctrl"
			// into the user's target application (issue #225). What tells them apart is
			// what precedes the '+': a modifier *name* means the user wrote a chord.
			if (c == ShiftMod && i + 1 < s.Length && s[i + 1] == '('
				&& !FollowsModifierName(s, i))
				return true;
		}
		return false;
	}

	// True when the run of characters immediately before <paramref name="plusIndex"/>
	// spells a modifier ("Ctrl", "Shift", "Alt", "AltGr", …) — i.e. that '+' is a
	// human chord separator. Anything else (a key name, a literal, the start of the
	// string) leaves the '+' as SendKeys' Shift modifier.
	private static bool FollowsModifierName(string s, int plusIndex)
	{
		int start = plusIndex;
		while (start > 0 && s[start - 1] != '+' && s[start - 1] != ',')
			start--;

		var norm = Normalize(s[start..plusIndex]);
		return IsCtrl(norm) || IsShift(norm) || IsAlt(norm) || IsAltGr(norm);
	}

	// Expands a fully parenthesised token into the keys the group applies to:
	//
	//   "(Enter)"     → {ENTER}       a single named key spelled inside the group
	//   "(Enter Tab)" → {ENTER}{TAB}  names written out, space separated
	//   "(AB)"        → a, b          the compact form: one key per character
	//
	// Anything that does not fit — a nested parenthesis, an unrecognised name, a
	// symbol run in the compact form — is rejected so the caller reports it as an
	// unknown key rather than typing garbage into the user's target application.
	private static bool TryExpandKeyGroup(string token, out List<string> keys)
	{
		keys = new List<string>();

		var t = token.Trim();
		if (t.Length < 3 || t[0] != '(' || t[^1] != ')')
			return false;

		var body = t[1..^1];
		if (body.Contains('(') || body.Contains(')'))
			return false;

		var pieces = body.Split(GroupSeparators,
			StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
		if (pieces.Length == 0)
			return false;

		// A lone piece that is not itself a key name is the compact form, where each
		// character is its own key. Checked first so "(AB)" expands rather than being
		// rejected, while "(Enter)" and "(F5)" resolve as the keys they name.
		if (pieces.Length == 1 && !TryResolveKey(pieces[0], out _))
			return TryExpandCompactGroup(pieces[0], keys);

		foreach (var piece in pieces)
		{
			ThrowIfUnsupported(piece);

			if (!TryResolveKey(piece, out var key))
			{
				keys.Clear();
				return false;
			}

			keys.Add(key);
		}

		return true;
	}

	// The compact "(AB)" form. Restricted to letters and digits: a symbol run has no
	// unambiguous per-character reading, and guessing one would emit keystrokes the
	// user did not ask for.
	private static bool TryExpandCompactGroup(string body, List<string> keys)
	{
		ThrowIfUnsupported(body);

		foreach (char c in body)
		{
			if (!char.IsLetterOrDigit(c) || !TrySingleCharLiteral(c.ToString(), out var literal))
			{
				keys.Clear();
				return false;
			}

			keys.Add(EscapeIfReserved(literal));
		}

		return keys.Count > 0;
	}

	// Resolves one token to its SendKeys form: a function key, a named key from
	// KeyMap, or a single-character literal. Shared by the plain chord path and the
	// group expansion so both spell keys identically.
	private static bool TryResolveKey(string token, out string key)
	{
		key = "";
		var norm = Normalize(token);

		if (TryMapFunctionKey(norm, out var fKey))
		{
			key = fKey;
			return true;
		}

		if (KeyMap.TryGetValue(norm, out var mapped))
		{
			key = EscapeIfReserved(mapped);
			return true;
		}

		if (TrySingleCharLiteral(token, out var literal))
		{
			key = EscapeIfReserved(literal);
			return true;
		}

		return false;
	}

	private static void ThrowIfUnsupported(string token)
	{
		if (UnsupportedKeys.Contains(Normalize(token)))
			throw new NotSupportedException($"'{token}' is not supported by Windows Forms SendKeys.");
	}

	private static List<string> SplitByComma(string input)
	{
		var parts = new List<string>();
		int start = 0;
		for (int i = 0; i < input.Length; i++)
		{
			if (input[i] == ',')
			{
				parts.Add(input.Substring(start, i - start));
				start = i + 1;
			}
		}
		parts.Add(input.Substring(start));
		return parts;
	}

	// Tokenize on '+' but detect when '+' itself is intended as a key (e.g., "Ctrl++")
	private static (List<string> tokens, HashSet<int> plusLiteralPositions) TokenizeByPlus(string chord)
	{
		var tokens = new List<string>();
		var plusLiteralPositions = new HashSet<int>(); // indexes in tokens that are '+' literal

		int i = 0;
		int tokenIndex = -1;
		var current = new System.Text.StringBuilder();

		bool lastWasSeparator = true; // treat leading '+' as literal

		while (i < chord.Length)
		{
			char c = chord[i++];

			if (c == '+')
			{
				if (lastWasSeparator)
				{
					// '+' right after a separator (or at start) → '+' key literal
					tokens.Add("+");
					tokenIndex++;
					plusLiteralPositions.Add(tokenIndex);
					lastWasSeparator = false; // we just added a token
					continue;
				}

				// end current token (if any), mark separator
				if (current.Length > 0)
				{
					tokens.Add(current.ToString().Trim());
					current.Clear();
					tokenIndex++;
				}
				lastWasSeparator = true;
				continue;
			}

			current.Append(c);
			lastWasSeparator = false;
		}

		if (current.Length > 0)
		{
			tokens.Add(current.ToString().Trim());
			tokenIndex++;
		}

		for (int t = tokens.Count - 1; t >= 0; t--)
		{
			if (string.IsNullOrWhiteSpace(tokens[t]))
			{
				tokens.RemoveAt(t);
				// keep plusLiteralPositions consistent: shift not necessary since we only remove empties
			}
		}

		return (tokens, plusLiteralPositions);
	}

	private static IEnumerable<(string token, bool isPlusLiteral)> EnumerateTokens(
		List<string> tokens, HashSet<int> plusLiteralPositions)
	{
		for (int idx = 0; idx < tokens.Count; idx++)
		{
			var tok = tokens[idx];
			var isPlus = plusLiteralPositions.Contains(idx);
			yield return (tok, isPlus);
		}
	}

	private static string Normalize(string token)
	{
		// Uppercase; remove spaces, dashes, underscores; keep alnum only for matching dictionary keys.
		// Preserve original for single-char literal path.
		Span<char> buffer = stackalloc char[token.Length];
		int j = 0;
		for (int i = 0; i < token.Length; i++)
		{
			char c = token[i];
			if (c == ' ' || c == '-' || c == '_')
				continue;

			if (char.IsLetterOrDigit(c))
				buffer[j++] = char.ToUpperInvariant(c);
			else
				buffer[j++] = char.ToUpperInvariant(c); // allow symbols like '+' in norm when needed
		}
		return new string(buffer[..j]);
	}

	private static bool IsCtrl(string norm) =>
		norm is "CTRL" or "CONTROL" or "CTL";

	private static bool IsShift(string norm) =>
		norm is "SHIFT" or "SHFT";

	private static bool IsAlt(string norm) =>
		norm is "ALT" or "OPTION" or "OPT";

	private static bool IsAltGr(string norm) =>
		norm is "ALTGR" or "ALTGRAPH";

	private static bool TryMapFunctionKey(string norm, out string mapped)
	{
		mapped = "";
		if (norm.Length is < 2 or > 3) // F1..F24
			return false;

		if (norm[0] != 'F')
			return false;

		if (!int.TryParse(norm.AsSpan(1), NumberStyles.None, CultureInfo.InvariantCulture, out var n))
			return false;

		if (n < 1 || n > 24)
			return false;

		mapped = "{F" + n.ToString(CultureInfo.InvariantCulture) + "}";
		return true;
	}

	private static bool TrySingleCharLiteral(string token, out string literal)
	{
		literal = "";
		var t = token.Trim();

		static char LowerIfLetter(char ch)
		{
			return char.IsLetter(ch) ? char.ToLowerInvariant(ch) : ch;
		}

		if (t.Length == 1)
		{
			literal = LowerIfLetter(t[0]).ToString();
			return true;
		}

		if (t.Length == 3 && t[0] == '"' && t[2] == '"')
		{
			literal = LowerIfLetter(t[1]).ToString();
			return true;
		}

		return false;
	}

	private static string EscapeIfReserved(string s)
	{
		// If it's a braced action like {DEL}, leave it.
		if (s.Length >= 2 && s[0] == '{' && s[^1] == '}')
			return s;

		// Escape single reserved characters with braces: + ^ % ~ ( ) { }
		if (s.Length == 1)
		{
			return s switch
			{
				"+" => "{+}",
				"^" => "{^}",
				"%" => "{%}",
				"~" => "{~}",
				"(" => "{(}",
				")" => "{)}",
				"{" => "{{}",
				"}" => "{}}",
				_ => s
			};
		}

		// For longer literals, escape any braces they might contain (rare).
		return s.Replace("{", "{{}").Replace("}", "{}}");
	}
}
