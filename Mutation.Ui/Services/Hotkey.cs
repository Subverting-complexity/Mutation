using System;
using Windows.System;

namespace Mutation.Ui.Services;

public class Hotkey
{
        internal static readonly char[] TokenSeparators = new[] { '+', '-', ' ', ',', '/', '\\', '|', ';', ':' };

        public bool Alt { get; set; }
        public bool Control { get; set; }
        public bool Shift { get; set; }
        public bool Win { get; set; }
        public VirtualKey Key { get; set; }

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

	public override string ToString()
	{
		if (Key == VirtualKey.None)
			return "(none)";
		string modifiers = "";
		if (Shift) modifiers += "Shift+";
		if (Control) modifiers += "Control+";
		if (Alt) modifiers += "Alt+";
		if (Win) modifiers += "Windows+";
		string keyName = Key.ToString();
		// Optionally strip 'Number' prefix for number keys
		if (keyName.StartsWith("Number") && keyName.Length == 7)
			keyName = keyName.Substring(6);
		return modifiers + keyName;
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
