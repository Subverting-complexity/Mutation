#nullable enable
using System;

namespace Mutation.Ui.Services;

public static class HotkeyValidator
{
	public static string? Validate(string input, bool allowSendKeysSyntax)
	{
		var trimmed = (input ?? string.Empty).Trim();
		if (trimmed.Length == 0)
			return null;

		var parts = trimmed.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
		Exception? firstParseError = null;
		bool allParsed = parts.Length > 0;
		foreach (var p in parts)
		{
			try { _ = Hotkey.Parse(p); }
			catch (Exception ex) { allParsed = false; firstParseError = ex; break; }
		}
		if (allParsed)
			return null;

		if (allowSendKeysSyntax)
		{
			try { _ = SendKeysMapper.Map(trimmed); return null; }
			catch (Exception ex) { return ex.Message; }
		}

		return firstParseError!.Message;
	}
}
