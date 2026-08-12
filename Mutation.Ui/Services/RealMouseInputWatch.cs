using System;
using System.Runtime.InteropServices;

namespace Mutation.Ui.Services;

/// <summary>
/// Watches for a hand on the mouse, as opposed to a program moving the pointer.
///
/// <para>
/// Windows marks mouse input it did not get from a device as injected, and a low-level hook can
/// read that mark. That is the whole point of this class. A magnifier that moves the pointer to
/// the keyboard caret either injects the movement, which arrives flagged, or places the cursor
/// directly, which produces no mouse input at all — and neither looks anything like the user
/// picking up the mouse. Without that distinction, holding the pointer still after a capture
/// would mean fighting whoever moved it, user or not.
/// </para>
///
/// <para>
/// Any button counts, whatever it is flagged as. Injected movement is common enough from remote
/// desktops and some KVM software that a genuine user could be mistaken for a program; a button
/// press is a deliberate act either way, and treating it as the user is the safer mistake.
/// </para>
///
/// <para>
/// A low-level hook is delivered on the thread that installed it, and that thread needs a message
/// loop, so <see cref="Start"/> and <see cref="Dispose"/> both have to be called on the UI
/// thread. The flag it sets is read from elsewhere, which is why it is volatile.
/// </para>
///
/// <para>
/// The hook sees every mouse event on the machine while it is installed, so it is installed for
/// no longer than the hold it serves — well under a second per capture — and it does nothing but
/// set a flag and pass the event on.
/// </para>
/// </summary>
internal sealed class RealMouseInputWatch : IDisposable
{
	private const int WH_MOUSE_LL = 14;

	private const int WM_MOUSEMOVE = 0x0200;
	private const int WM_LBUTTONDOWN = 0x0201;
	private const int WM_RBUTTONDOWN = 0x0204;
	private const int WM_MBUTTONDOWN = 0x0207;
	private const int WM_XBUTTONDOWN = 0x020B;
	private const int WM_MOUSEWHEEL = 0x020A;
	private const int WM_MOUSEHWHEEL = 0x020E;

	/// <summary>LLMHF_INJECTED — the event did not come from a mouse.</summary>
	private const uint LLMHF_INJECTED = 0x00000001;

	/// <summary>
	/// LLMHF_LOWER_IL_INJECTED — injected by something running at a lower integrity level. Also
	/// not a hand on a mouse.
	/// </summary>
	private const uint LLMHF_LOWER_IL_INJECTED = 0x00000002;

	[StructLayout(LayoutKind.Sequential)]
	private struct POINT
	{
		public int x;
		public int y;
	}

	[StructLayout(LayoutKind.Sequential)]
	private struct MSLLHOOKSTRUCT
	{
		public POINT pt;
		public uint mouseData;
		public uint flags;
		public uint time;
		public IntPtr dwExtraInfo;
	}

	private delegate IntPtr LowLevelMouseProc(int nCode, IntPtr wParam, IntPtr lParam);

	[DllImport("user32.dll", SetLastError = true)]
	private static extern IntPtr SetWindowsHookEx(int idHook, LowLevelMouseProc lpfn, IntPtr hMod, uint dwThreadId);

	[DllImport("user32.dll", SetLastError = true)]
	private static extern bool UnhookWindowsHookEx(IntPtr hhk);

	[DllImport("user32.dll")]
	private static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);

	[DllImport("kernel32.dll", CharSet = CharSet.Auto)]
	private static extern IntPtr GetModuleHandle(string? lpModuleName);

	private IntPtr _hook;

	// Held so the delegate is not collected while Windows still holds a pointer to it.
	private readonly LowLevelMouseProc _callback;

	private volatile bool _userHasTheMouse;

	private RealMouseInputWatch()
	{
		_callback = OnMouseInput;
	}

	/// <summary>
	/// Whether a hand has been on the mouse since the watch started. Once true it stays true: the
	/// pointer is the user's for the rest of this capture, and nothing may take it back.
	/// </summary>
	public bool UserHasTheMouse => _userHasTheMouse;

	/// <summary>
	/// Installs the hook on the calling thread, which must be the UI thread. Never throws and
	/// never returns null: a watch that could not install simply reports that the user has not
	/// touched the mouse, which leaves the caller doing what it would have done anyway.
	/// </summary>
	public static RealMouseInputWatch Start()
	{
		var watch = new RealMouseInputWatch();
		try
		{
			using var process = System.Diagnostics.Process.GetCurrentProcess();
			using var module = process.MainModule;
			watch._hook = SetWindowsHookEx(WH_MOUSE_LL, watch._callback, GetModuleHandle(module?.ModuleName), 0);
		}
		catch
		{
			watch._hook = IntPtr.Zero;
		}

		return watch;
	}

	/// <summary>Removes the hook. Must be called on the thread that installed it.</summary>
	public void Dispose()
	{
		if (_hook == IntPtr.Zero)
			return;

		try { UnhookWindowsHookEx(_hook); } catch { }
		_hook = IntPtr.Zero;
	}

	private IntPtr OnMouseInput(int nCode, IntPtr wParam, IntPtr lParam)
	{
		if (nCode >= 0)
		{
			try
			{
				int message = (int)wParam;
				if (IsButtonOrWheel(message))
				{
					// A deliberate act however it arrived.
					_userHasTheMouse = true;
				}
				else if (message == WM_MOUSEMOVE)
				{
					var data = Marshal.PtrToStructure<MSLLHOOKSTRUCT>(lParam);
					bool injected = (data.flags & (LLMHF_INJECTED | LLMHF_LOWER_IL_INJECTED)) != 0;
					if (!injected)
						_userHasTheMouse = true;
				}
			}
			catch
			{
				// This runs inside the system's input path. Whatever goes wrong here, the event
				// still has to be passed on, and a capture must not be brought down by it.
			}
		}

		return CallNextHookEx(_hook, nCode, wParam, lParam);
	}

	private static bool IsButtonOrWheel(int message) =>
		message is WM_LBUTTONDOWN or WM_RBUTTONDOWN or WM_MBUTTONDOWN or WM_XBUTTONDOWN
			or WM_MOUSEWHEEL or WM_MOUSEHWHEEL;
}
