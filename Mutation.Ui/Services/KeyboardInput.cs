#nullable enable
using System;
using System.Runtime.InteropServices;

namespace Mutation.Ui.Services;

/// <summary>
/// The <c>SendInput</c> interop, in one place.
/// <para>
/// The layout matters more than it looks. Windows rejects the whole call with
/// ERROR_INVALID_PARAMETER unless <c>cbSize</c> equals the size of the real Win32
/// <c>INPUT</c> — and the real one carries a union of MOUSEINPUT, KEYBDINPUT and
/// HARDWAREINPUT, sized by the largest of the three. Declaring only the keyboard arm
/// makes the struct 32 bytes on x64 where Windows wants 40, so every call returned
/// zero events sent and nothing was ever typed (PR #328).
/// </para>
/// <para>
/// Nothing said so. The keystrokes silently went nowhere and the app fell through to
/// the WinForms SendKeys fallback, which cannot report delivery — so a dictation
/// pasted into another window was announced as a failure it had not had, and the
/// shortcut configured to run afterwards was skipped along with it.
/// </para>
/// <para>
/// The contents matter for the same reason the size does. A key press describes both
/// what the key means (the virtual key) and where it is (the scan code), and this sent
/// only the first. Ordinary applications read the meaning and were content; screen
/// readers and magnifiers watch the keyboard through a low-level hook and read the
/// position, so a shortcut arriving from nowhere on the keyboard was not the shortcut
/// they were waiting for and they let it pass. Once PR #328 made SendInput work, that
/// became the whole of the "send this shortcut afterwards" feature failing — quietly,
/// because Windows accepted every event (issue #335).
/// </para>
/// </summary>
internal static class KeyboardInput
{
	public const uint InputKeyboard = 1;
	public const uint KeyEventExtendedKey = 0x0001;
	public const uint KeyEventKeyUp = 0x0002;
	public const uint KeyEventUnicode = 0x0004;

	public const ushort VkControl = 0x11;
	public const ushort VkShift = 0x10;
	public const ushort VkAlt = 0x12;
	public const ushort VkLeftWindows = 0x5B;

	[StructLayout(LayoutKind.Sequential)]
	public struct MOUSEINPUT
	{
		public int dx;
		public int dy;
		public uint mouseData;
		public uint dwFlags;
		public uint time;
		public IntPtr dwExtraInfo;
	}

	[StructLayout(LayoutKind.Sequential)]
	public struct KEYBDINPUT
	{
		public ushort wVk;
		public ushort wScan;
		public uint dwFlags;
		public uint time;
		public IntPtr dwExtraInfo;
	}

	[StructLayout(LayoutKind.Sequential)]
	public struct HARDWAREINPUT
	{
		public uint uMsg;
		public ushort wParamL;
		public ushort wParamH;
	}

	/// <summary>
	/// All three arms, because the union's size is what makes <see cref="INPUT"/> the size
	/// Windows checks for. Only <see cref="ki"/> is ever written; the other two are here to
	/// be measured.
	/// </summary>
	[StructLayout(LayoutKind.Explicit)]
	public struct INPUTUNION
	{
		[FieldOffset(0)] public MOUSEINPUT mi;
		[FieldOffset(0)] public KEYBDINPUT ki;
		[FieldOffset(0)] public HARDWAREINPUT hi;
	}

	[StructLayout(LayoutKind.Sequential)]
	public struct INPUT
	{
		public uint type;
		public INPUTUNION U;
	}

	/// <summary>What this build will pass as <c>cbSize</c>.</summary>
	public static int InputSize => Marshal.SizeOf<INPUT>();

	/// <summary>
	/// What Windows requires <c>cbSize</c> to be: 40 bytes in a 64-bit process, 28 in a
	/// 32-bit one. Exposed so the size can be asserted without a keyboard.
	/// </summary>
	public static int RequiredInputSize => IntPtr.Size == 8 ? 40 : 28;

	[DllImport("user32.dll", SetLastError = true)]
	private static extern uint SendInput(uint nInputs, INPUT[] pInputs, int cbSize);

	/// <summary>MAPVK_VK_TO_VSC — a virtual key to the scan code of the key that carries it.</summary>
	private const uint MapVkToScanCode = 0;

	/// <summary>
	/// MAPVK_VK_TO_VSC_EX — the same, with the prefix byte in the high byte: 0xE0 for an
	/// extended key. Consulted only for the flag; the scan code itself still comes from
	/// <see cref="MapVkToScanCode"/>, because the low byte of the two agrees everywhere the
	/// extended one answers at all.
	/// </summary>
	private const uint MapVkToScanCodeEx = 4;

	[DllImport("user32.dll")]
	private static extern uint MapVirtualKey(uint code, uint mapType);

	/// <summary>
	/// Submits <paramref name="inputs"/> and reports how many Windows accepted. Fewer than
	/// were submitted means the foreground application refused them — usually because it
	/// runs at a higher integrity level than Mutation, which Windows enforces in silence.
	/// </summary>
	public static uint Send(INPUT[] inputs)
	{
		if (inputs is null || inputs.Length == 0)
			return 0;

		return SendInput((uint)inputs.Length, inputs, InputSize);
	}

	public static INPUT KeyDown(ushort virtualKey) =>
		Key(virtualKey, IsExtended(virtualKey) ? KeyEventExtendedKey : 0);

	public static INPUT KeyUp(ushort virtualKey) =>
		Key(virtualKey, KeyEventKeyUp | (IsExtended(virtualKey) ? KeyEventExtendedKey : 0));

	public static INPUT UnicodeDown(char character) => Unicode(character, KeyEventUnicode);

	public static INPUT UnicodeUp(char character) => Unicode(character, KeyEventUnicode | KeyEventKeyUp);

	/// <summary>
	/// The scan code the keyboard itself would report for <paramref name="virtualKey"/> —
	/// what the key's physical position is, as opposed to what it means. Zero when the
	/// current layout has no position for it.
	/// </summary>
	public static ushort ScanCode(ushort virtualKey) => (ushort)MapVirtualKey(virtualKey, MapVkToScanCode);

	private static INPUT Key(ushort virtualKey, uint flags) => new()
	{
		type = InputKeyboard,
		U = new INPUTUNION
		{
			ki = new KEYBDINPUT
			{
				wVk = virtualKey,
				// Filled, not left at zero. A real key press carries both the virtual key
				// and the position it came from, and anything reading the keyboard through
				// a low-level hook — screen readers and magnifiers above all — is entitled
				// to look at the position. A chord injected with a scan code of zero is
				// simply not the shortcut they are watching for, so it is discarded without
				// a word, while SendInput reports every event accepted (issue #335).
				wScan = ScanCode(virtualKey),
				dwFlags = flags,
				time = 0,
				dwExtraInfo = IntPtr.Zero,
			}
		}
	};

	private static INPUT Unicode(char character, uint flags) => new()
	{
		type = InputKeyboard,
		U = new INPUTUNION
		{
			ki = new KEYBDINPUT
			{
				wVk = 0,
				wScan = character,
				dwFlags = flags,
				time = 0,
				dwExtraInfo = IntPtr.Zero,
			}
		}
	};

	/// <summary>
	/// Whether the key needs the 0xE0 prefix that tells it apart from the numeric-keypad key
	/// sharing its scan code. Delete without it arrives as the keypad's full stop.
	/// </summary>
	/// <remarks>
	/// Asked of Windows and of the list below, because neither knows all of it.
	/// <para>
	/// The list cannot be dropped: <c>MapVirtualKey</c> answers the navigation cluster with
	/// the keypad's own scan code and no prefix — Delete is 0x0053, Home 0x0047 — because a
	/// virtual key alone does not say which of the two keys was pressed. Believing it there
	/// would put the full stop back.
	/// </para>
	/// <para>
	/// And the list is not enough on its own: it was written by hand and had never been
	/// checked against Windows. It was missing the context-menu key and the keypad's divide,
	/// both of which the "send key after…" boxes accept by name, and it claimed right Shift
	/// is extended when it is not — that one is scan 0x36 with no prefix, and prefixing it
	/// produces the filler keystroke Windows emits around keypad sequences, which hooks throw
	/// away (PR #339).
	/// </para>
	/// </remarks>
	public static bool IsExtended(ushort virtualKey) =>
		NamedExtendedKeys(virtualKey) || (MapVirtualKey(virtualKey, MapVkToScanCodeEx) & 0xFF00) == 0xE000;

	/// <summary>
	/// The keys Windows will not admit are extended, because their virtual key is shared with
	/// a keypad key and it cannot tell which was meant.
	/// </summary>
	private static bool NamedExtendedKeys(ushort virtualKey) =>
		virtualKey is 0x21 /*PGUP*/ or 0x22 /*PGDN*/ or 0x23 /*END*/ or 0x24 /*HOME*/
			or 0x25 /*LEFT*/ or 0x26 /*UP*/ or 0x27 /*RIGHT*/ or 0x28 /*DOWN*/
			or 0x2D /*INSERT*/ or 0x2E /*DELETE*/;
}
