using System;
using System.Diagnostics.CodeAnalysis;
using System.Text;
using Windows.System;

namespace Mutation.Ui.Services;

/// <summary>
/// One chord: the modifiers held down and the key pressed with them.
/// <para>
/// Two instances describing the same chord are equal, whatever text they were parsed from.
/// That is what lets duplicate detection work on chords rather than on strings — before
/// this, every place that needed set membership normalized to text of its own, and the
/// spellings did not agree (issue #306).
/// </para>
/// </summary>
public class Hotkey : IEquatable<Hotkey>
{
        internal static readonly char[] TokenSeparators = new[] { '+', '-', ' ', ',', '/', '\\', '|', ';', ':' };

        public bool Alt { get; set; }
        public bool Control { get; set; }
        public bool Shift { get; set; }
        public bool Win { get; set; }
        public VirtualKey Key { get; set; }

	/// <summary>
	/// Whether <paramref name="other"/> is the same chord. Mutable properties would normally
	/// make a class a poor equality candidate, but a <see cref="Hotkey"/> is treated as a
	/// value everywhere it is used: parsed, registered, compared, discarded — never edited
	/// after it has gone into a set.
	/// </summary>
	public bool Equals(Hotkey? other)
	{
		if (other is null) return false;
		if (ReferenceEquals(this, other)) return true;

		return Alt == other.Alt
			&& Control == other.Control
			&& Shift == other.Shift
			&& Win == other.Win
			&& Key == other.Key;
	}

	public override bool Equals(object? obj) => Equals(obj as Hotkey);

	public override int GetHashCode() => HashCode.Combine(Alt, Control, Shift, Win, Key);

	public Hotkey Clone()
	{
		return new Hotkey
		{
			Alt = this.Alt,
			Control = this.Control,
			Shift = this.Shift,
			Win = this.Win,
			Key = this.Key
		};
	}

	/// <summary>
	/// The one canonical spelling of a chord: modifiers in a fixed order, upper-cased key,
	/// and the <c>Number</c> prefix stripped off the digit keys so a chord reads the way the
	/// user typed it. <see cref="Parse"/> accepts what this produces, so the form round-trips.
	/// <para>
	/// This used to be a third spelling — <c>Shift+Control+Alt+Windows+A</c> against the
	/// <c>CTRL+SHIFT+ALT+WIN+A</c> that both the registration table and the hotkey editor
	/// emitted. It now agrees with them, so there is one canonical form in the app rather
	/// than two (issue #306).
	/// </para>
	/// </summary>
	public override string ToString()
	{
		if (Key == VirtualKey.None)
			return "(none)";

		var sb = new StringBuilder(32);
		if (Control) sb.Append("CTRL+");
		if (Shift) sb.Append("SHIFT+");
		if (Alt) sb.Append("ALT+");
		if (Win) sb.Append("WIN+");

		string keyName = Key.ToString();
		if (keyName.StartsWith("Number", StringComparison.Ordinal) && keyName.Length == 7)
			keyName = keyName.Substring(6);

		sb.Append(keyName.ToUpperInvariant());
		return sb.ToString();
	}

	/// <summary>
	/// Parses <paramref name="text"/> as a chord, answering false rather than throwing when
	/// it is blank or not a chord at all. Callers that check for duplicates run over whatever
	/// the user has typed so far, where half-finished text is routine and not an error.
	/// </summary>
	public static bool TryParse(string? text, [NotNullWhen(true)] out Hotkey? hotkey)
	{
		try
		{
			hotkey = Parse(text!);
			return true;
		}
		catch (Exception ex) when (ex is ArgumentException or NotSupportedException)
		{
			hotkey = null;
			return false;
		}
	}

	public static Hotkey Parse(string text)
	{
		if (string.IsNullOrWhiteSpace(text))
			throw new ArgumentException("Invalid hotkey", nameof(text));

                var parts = text.Split(TokenSeparators, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                var hk = new Hotkey();
                foreach (var p in parts)
                {
                        var token = p.Trim().ToUpperInvariant();
			switch (token)
			{
				case "CTRL":
				case "CONTROL":
					hk.Control = true; break;
				case "ALT":
					hk.Alt = true; break;
				case "SHIFT":
				case "SHFT":
					hk.Shift = true; break;
				case "WIN":
				case "WINDOWS":
				case "START":
					hk.Win = true; break;
                                default:
                                        // Try the "NumberN" alias first for purely-numeric tokens.
                                        // Otherwise Enum.TryParse would parse "5" as the integer value 5
                                        // and silently bind to VirtualKey.XButton1 (the int-5 enum member).
                                        bool isAllDigits = token.Length > 0;
                                        for (int i = 0; i < token.Length && isAllDigits; i++)
                                                isAllDigits = char.IsDigit(token[i]);

                                        if (isAllDigits && Enum.TryParse<VirtualKey>("Number" + token, true, out var vk))
                                                hk.Key = vk;
                                        else if (Enum.TryParse<VirtualKey>(token, true, out vk))
                                                hk.Key = vk;
                                        else if (Enum.TryParse<VirtualKey>("Number" + token, true, out vk))
                                                hk.Key = vk;
                                        else
                                                throw new NotSupportedException($"Unsupported key '{token}'");
                                        break;
                        }
                }

                if (hk.Key == VirtualKey.None)
                        throw new ArgumentException("Hotkey must include a non-modifier key.", nameof(text));

                return hk;
        }
}
