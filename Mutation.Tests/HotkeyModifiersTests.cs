using Mutation.Ui.Services;
using Windows.System;

namespace Mutation.Tests;

// Covers issue #226: MOD_NOREPEAT was declared but never OR'd into the modifier word,
// so Windows repeated WM_HOTKEY at the keyboard auto-repeat rate while a hotkey was
// held. Holding the dictation shortcut a moment too long started a recording, stopped
// and transcribed it, and started another — with a RecorderBusy failure beep per round.
public class HotkeyModifiersTests
{
	[Fact]
	public void Compose_AlwaysSetsNoRepeat()
	{
		uint mods = HotkeyModifiers.Compose(new Hotkey { Key = VirtualKey.U });

		Assert.Equal(HotkeyModifiers.MOD_NOREPEAT, mods & HotkeyModifiers.MOD_NOREPEAT);
	}

	[Fact]
	public void Compose_SetsNoRepeat_ForEveryModifierCombination()
	{
		for (int bits = 0; bits < 16; bits++)
		{
			var hotkey = new Hotkey
			{
				Alt = (bits & 1) != 0,
				Control = (bits & 2) != 0,
				Shift = (bits & 4) != 0,
				Win = (bits & 8) != 0,
				Key = VirtualKey.U,
			};

			uint mods = HotkeyModifiers.Compose(hotkey);

			Assert.Equal(HotkeyModifiers.MOD_NOREPEAT, mods & HotkeyModifiers.MOD_NOREPEAT);
		}
	}

	[Fact]
	public void Compose_KeepsTheRequestedModifiers()
	{
		uint mods = HotkeyModifiers.Compose(Hotkey.Parse("Shift+Alt+U"));

		Assert.Equal(
			HotkeyModifiers.MOD_SHIFT | HotkeyModifiers.MOD_ALT | HotkeyModifiers.MOD_NOREPEAT,
			mods);
	}

	[Fact]
	public void Compose_DoesNotSetModifiersThatWereNotAsked_For()
	{
		uint mods = HotkeyModifiers.Compose(Hotkey.Parse("Ctrl+M"));

		Assert.Equal(HotkeyModifiers.MOD_CONTROL | HotkeyModifiers.MOD_NOREPEAT, mods);
		Assert.Equal(0u, mods & HotkeyModifiers.MOD_ALT);
		Assert.Equal(0u, mods & HotkeyModifiers.MOD_SHIFT);
		Assert.Equal(0u, mods & HotkeyModifiers.MOD_WIN);
	}

	[Fact]
	public void Compose_MapsWin()
	{
		uint mods = HotkeyModifiers.Compose(Hotkey.Parse("Win+Ctrl+Shift+Alt+F1"));

		Assert.Equal(
			HotkeyModifiers.MOD_WIN | HotkeyModifiers.MOD_CONTROL | HotkeyModifiers.MOD_SHIFT
				| HotkeyModifiers.MOD_ALT | HotkeyModifiers.MOD_NOREPEAT,
			mods);
	}
}
