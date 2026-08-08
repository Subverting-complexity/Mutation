using System.Runtime.InteropServices;
using Mutation.Ui.Services;

namespace Mutation.Tests;

/// <summary>
/// The size of the INPUT struct, which is the one thing Windows validates before it will
/// deliver a single keystroke.
/// <para>
/// Issue #325: the struct declared only the keyboard arm of the union, so it measured 32
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
}
