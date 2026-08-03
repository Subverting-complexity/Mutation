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

			if (TryMapFunctionKey(norm, out var fKey))
			{
				keys.Add(fKey);
				continue;
			}

			if (KeyMap.TryGetValue(norm, out var mapped))
			{
				keys.Add(EscapeIfReserved(mapped));
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

			if (TrySingleCharLiteral(token, out var literal))
			{
				keys.Add(EscapeIfReserved(literal));
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
		// '^', '%', '~', braces, or grouping parentheses opened by a modifier.
		for (int i = 0; i < s.Length; i++)
		{
			var c = s[i];
			if (c == '^' || c == '%' || c == '~' || c == '{' || c == '}')
				return true;

			// '+(' opens a SendKeys group only when that '+' is the Shift modifier,
			// which can only sit at the start of the string or directly after another
			// modifier character — "+(ab)", "^+(ab)". In a human-written chord like
			// "Ctrl+(AB)" the '+' is the separator after a key name, so treating it as
			// SendKeys would pass the string through untranslated and type the literal
			// text "Ctrl" into the user's target application.
			if (IsModifierChar(c) && i + 1 < s.Length && s[i + 1] == '('
				&& (i == 0 || IsModifierChar(s[i - 1])))
				return true;
		}
		return false;
	}

	private static bool IsModifierChar(char c) =>
		c == CtrlMod || c == ShiftMod || c == AltMod;

	// Expands "(AB)" into the individual keys 'a' and 'b'. Only a fully parenthesised
	// token qualifies, and every character inside must be a plain single-key literal —
	// a nested parenthesis is malformed and is rejected so the caller reports it as an
	// unknown key rather than emitting garbage keystrokes.
	private static bool TryExpandKeyGroup(string token, out List<string> keys)
	{
		keys = new List<string>();

		var t = token.Trim();
		if (t.Length < 3 || t[0] != '(' || t[^1] != ')')
			return false;

		foreach (char c in t[1..^1])
		{
			if (char.IsWhiteSpace(c))
				continue;

			if (c == '(' || c == ')' || !TrySingleCharLiteral(c.ToString(), out var literal))
			{
				keys.Clear();
				return false;
			}

			keys.Add(EscapeIfReserved(literal));
		}

		if (keys.Count == 0)
			return false;

		return true;
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
