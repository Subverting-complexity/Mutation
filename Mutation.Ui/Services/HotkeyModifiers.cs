using System;

namespace Mutation.Ui.Services;

/// <summary>
/// Builds the <c>fsModifiers</c> word that <c>RegisterHotKey</c> takes. Kept apart
/// from <see cref="HotkeyManager"/> so the composition is testable without a window
/// handle or a P/Invoke.
/// </summary>
public static class HotkeyModifiers
{
	public const uint MOD_ALT = 0x1;
	public const uint MOD_CONTROL = 0x2;
	public const uint MOD_SHIFT = 0x4;
	public const uint MOD_WIN = 0x8;

	/// <summary>
	/// Windows repeats WM_HOTKEY at the keyboard auto-repeat rate for as long as the
	/// combination is held unless this flag is set. Every hotkey here starts an
	/// operation — record, mute, read aloud — so a repeat is never what the user meant:
	/// holding the dictation shortcut a moment too long would start a recording, stop
	/// and transcribe it, and start another (issue #226).
	/// </summary>
	public const uint MOD_NOREPEAT = 0x4000;

	/// <summary>
	/// Modifier flags for <paramref name="hotkey"/>, always including
	/// <see cref="MOD_NOREPEAT"/> so a held combination activates exactly once.
	/// </summary>
	public static uint Compose(Hotkey hotkey)
	{
		ArgumentNullException.ThrowIfNull(hotkey);

		uint mods = MOD_NOREPEAT;
		if (hotkey.Alt) mods |= MOD_ALT;
		if (hotkey.Control) mods |= MOD_CONTROL;
		if (hotkey.Shift) mods |= MOD_SHIFT;
		if (hotkey.Win) mods |= MOD_WIN;
		return mods;
	}
}
