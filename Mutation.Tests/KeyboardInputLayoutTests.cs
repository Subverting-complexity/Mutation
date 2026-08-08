using System.Runtime.InteropServices;
using Mutation.Ui.Services;

namespace Mutation.Tests;

/// <summary>
/// The size of the INPUT struct, which is the one thing Windows validates before it will
/// deliver a single keystroke.
/// <para>
/// PR #328: the struct declared only the keyboard arm of the union, so it measured 32
/// bytes in a 64-bit process where Windows requires 40. Every SendInput call in the app
/// answered "0 events sent, ERROR_INVALID_PARAMETER" — pastes, typed dictation, and every
/// configured send-a-shortcut-afterwards. Nothing about the code looked wrong, which is
/// exactly why the size is asserted here rather than left to be noticed again.
/// </para>
/// </summary>
public class KeyboardInputLayoutTests
{
	[Fact]
	public void InputSize_MatchesWhatWindowsRequires()
	{
		Assert.Equal(KeyboardInput.RequiredInputSize, KeyboardInput.InputSize);
	}

	[Fact]
	public void InputSize_Is40BytesInA64BitProcess()
	{
		// Stated as the literal so a change to RequiredInputSize cannot quietly agree with
		// a change to the struct and let both drift away from Windows.
		if (IntPtr.Size != 8)
			return;

		Assert.Equal(40, Marshal.SizeOf<KeyboardInput.INPUT>());
	}

	[Fact]
	public void Union_IsSizedByItsLargestArm()
	{
		// The mouse arm is the largest of the three; if it is ever dropped from the union
		// again the INPUT struct shrinks and SendInput stops working, silently.
		Assert.True(Marshal.SizeOf<KeyboardInput.MOUSEINPUT>() >= Marshal.SizeOf<KeyboardInput.KEYBDINPUT>());
		Assert.Equal(Marshal.SizeOf<KeyboardInput.MOUSEINPUT>(), Marshal.SizeOf<KeyboardInput.INPUTUNION>());
	}

	[Fact]
	public void KeyDown_CarriesTheVirtualKeyAndNoKeyUpFlag()
	{
		var input = KeyboardInput.KeyDown(KeyboardInput.VkControl);

		Assert.Equal(KeyboardInput.InputKeyboard, input.type);
		Assert.Equal(KeyboardInput.VkControl, input.U.ki.wVk);
		Assert.Equal(0u, input.U.ki.dwFlags & KeyboardInput.KeyEventKeyUp);
	}

	[Fact]
	public void KeyUp_SetsTheKeyUpFlag()
	{
		var input = KeyboardInput.KeyUp(KeyboardInput.VkControl);

		Assert.Equal(KeyboardInput.KeyEventKeyUp, input.U.ki.dwFlags & KeyboardInput.KeyEventKeyUp);
	}

	[Theory]
	[InlineData((ushort)0x2E)] // Delete
	[InlineData((ushort)0x24)] // Home
	[InlineData((ushort)0x21)] // Page Up
	public void NavigationKeys_AreMarkedExtended(ushort virtualKey)
	{
		// Without the flag these arrive as their numeric-keypad twins — Delete becomes the
		// keypad period, which types a full stop into the user's document.
		var input = KeyboardInput.KeyDown(virtualKey);

		Assert.Equal(KeyboardInput.KeyEventExtendedKey, input.U.ki.dwFlags & KeyboardInput.KeyEventExtendedKey);
	}

	[Fact]
	public void LetterKeys_AreNotMarkedExtended()
	{
		var input = KeyboardInput.KeyDown(0x56); // V

		Assert.Equal(0u, input.U.ki.dwFlags & KeyboardInput.KeyEventExtendedKey);
	}

	[Fact]
	public void Unicode_CarriesTheCharacterAsAScanCodeWithNoVirtualKey()
	{
		var down = KeyboardInput.UnicodeDown('é');

		Assert.Equal(0, down.U.ki.wVk);
		Assert.Equal('é', (char)down.U.ki.wScan);
		Assert.Equal(KeyboardInput.KeyEventUnicode, down.U.ki.dwFlags & KeyboardInput.KeyEventUnicode);
	}

	[Fact]
	public void Send_WithNothingToSend_DoesNotCallWindows()
	{
		Assert.Equal(0u, KeyboardInput.Send(Array.Empty<KeyboardInput.INPUT>()));
	}

	[Theory]
	[InlineData((ushort)0x11)] // Ctrl
	[InlineData((ushort)0x10)] // Shift
	[InlineData((ushort)0x12)] // Alt
	[InlineData((ushort)0x56)] // V
	[InlineData((ushort)0x2E)] // Delete
	[InlineData((ushort)0x24)] // Home
	[InlineData((ushort)0x70)] // F1
	public void KeyDown_CarriesTheScanCodeTheKeyboardWouldReport(ushort virtualKey)
	{
		// Issue #335. A chord injected with a scan code of zero carries no key position, and
		// a screen reader or magnifier watching the keyboard through a low-level hook reads
		// the position. It sees a key that is at no place on the keyboard, decides this is
		// not its shortcut, and does nothing — while SendInput reports every event accepted,
		// so nothing anywhere says the shortcut was ignored.
		var input = KeyboardInput.KeyDown(virtualKey);

		Assert.NotEqual(0, input.U.ki.wScan);
		Assert.Equal(KeyboardInput.ScanCode(virtualKey), input.U.ki.wScan);
	}

	[Fact]
	public void KeyUp_CarriesTheSameScanCodeAsItsKeyDown()
	{
		// A press and its release have to name the same physical key, or a watcher that
		// pairs them by position never sees the release and holds the chord down.
		const ushort delete = 0x2E;

		Assert.Equal(KeyboardInput.KeyDown(delete).U.ki.wScan, KeyboardInput.KeyUp(delete).U.ki.wScan);
	}

	[Fact]
	public void ExtendedKeys_KeepBothTheScanCodeAndTheExtendedFlag()
	{
		// Delete's scan code is the keypad period's; the extended flag is the only thing
		// telling the two apart. Adding the scan code must not cost the flag.
		var input = KeyboardInput.KeyDown(0x2E);

		Assert.NotEqual(0, input.U.ki.wScan);
		Assert.Equal(KeyboardInput.KeyEventExtendedKey, input.U.ki.dwFlags & KeyboardInput.KeyEventExtendedKey);
	}

	[Theory]
	[InlineData((ushort)0x5B)] // Left Windows
	[InlineData((ushort)0x5C)] // Right Windows
	[InlineData((ushort)0xA3)] // Right Ctrl
	[InlineData((ushort)0xA5)] // Right Alt
	[InlineData((ushort)0x5D)] // Context menu
	[InlineData((ushort)0x6F)] // Keypad divide
	public void KeysWindowsCallsExtended_AreMarkedExtended(ushort virtualKey)
	{
		// The last two were missing from the hand-written list, and both are reachable by
		// name from the "send key after…" boxes. Windows is asked as well as the list now.
		Assert.True(KeyboardInput.IsExtended(virtualKey));
	}

	[Theory]
	[InlineData((ushort)0xA1)] // Right Shift — scan 0x36, and genuinely not extended
	[InlineData((ushort)0xA0)] // Left Shift
	[InlineData((ushort)0xA2)] // Left Ctrl
	[InlineData((ushort)0x90)] // Num Lock
	[InlineData((ushort)0x56)] // V
	public void KeysWindowsDoesNotCallExtended_AreNotMarkedExtended(ushort virtualKey)
	{
		// Right Shift was on the list and should never have been. Prefixing it produces the
		// filler keystroke Windows emits around keypad sequences, which hooks discard.
		Assert.False(KeyboardInput.IsExtended(virtualKey));
	}

	[Theory]
	[InlineData((ushort)0x2E)] // Delete
	[InlineData((ushort)0x24)] // Home
	[InlineData((ushort)0x21)] // Page Up
	[InlineData((ushort)0x25)] // Left
	[InlineData((ushort)0x2D)] // Insert
	public void TheNavigationCluster_StaysExtendedThoughWindowsWillNotSaySo(ushort virtualKey)
	{
		// MapVirtualKey answers these with the keypad's scan code and no prefix, because a
		// virtual key alone does not say which of the twinned keys was pressed. Deferring to
		// it here would turn Delete back into the keypad's full stop.
		Assert.True(KeyboardInput.IsExtended(virtualKey));
	}

	[Fact]
	public void ScanCode_ForAKeyTheLayoutHasNoPositionFor_IsZeroRatherThanAThrow()
	{
		// There is no key for VK_PROCESSKEY. Zero is what the struct held before this was
		// filled in at all, so an unmappable key is no worse off than it used to be.
		Assert.Equal(0, KeyboardInput.ScanCode(0xE5));
	}
}
