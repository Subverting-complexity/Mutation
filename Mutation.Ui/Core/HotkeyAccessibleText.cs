namespace Mutation.Ui.Core;

/// <summary>
/// Builds the accessible name and tooltip for a hotkey-bound button.
///
/// The name is always composed from the label the caller passes for the button's
/// *current* state. An earlier version cached the first name it ever saw and rebuilt
/// from that cache, so a button whose state changed — Mute to Unmute, Record to Stop —
/// kept announcing its startup name while the glyph and tooltip updated correctly
/// (issue #214). Only the screen reader saw the stale value.
/// </summary>
internal static class HotkeyAccessibleText
{
	public static string ComposeName(string label, string? hotkey) =>
		string.IsNullOrWhiteSpace(hotkey) ? label : $"{label}, {hotkey}";

	public static string ComposeTooltip(string tooltip, string? hotkey) =>
		string.IsNullOrWhiteSpace(hotkey) ? tooltip : $"{tooltip} (Hotkey: {hotkey})";

	public static string ComposeHotkeyText(string? hotkey) =>
		string.IsNullOrWhiteSpace(hotkey) ? string.Empty : $"Hotkey: {hotkey}";
}
