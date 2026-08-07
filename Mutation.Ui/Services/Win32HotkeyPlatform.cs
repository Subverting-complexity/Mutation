using System;
using System.Runtime.InteropServices;

namespace Mutation.Ui.Services;

/// <summary>
/// The real Windows implementation of <see cref="IHotkeyPlatform"/>, bound to one window
/// handle. Holds no state of its own: what is registered is tracked by
/// <see cref="HotkeyRegistrationTable"/>, which is where the rules worth testing live.
/// </summary>
internal sealed class Win32HotkeyPlatform : IHotkeyPlatform
{
	[DllImport("user32.dll", SetLastError = true)]
	private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

	[DllImport("user32.dll")]
	private static extern bool UnregisterHotKey(IntPtr hWnd, int id);

	private readonly IntPtr _hwnd;

	public Win32HotkeyPlatform(IntPtr hwnd) => _hwnd = hwnd;

	public bool Register(int id, uint modifiers, uint virtualKey, out int errorCode)
	{
		if (RegisterHotKey(_hwnd, id, modifiers, virtualKey))
		{
			errorCode = 0;
			return true;
		}

		errorCode = Marshal.GetLastWin32Error();
		return false;
	}

	public void Unregister(int id) => UnregisterHotKey(_hwnd, id);
}
