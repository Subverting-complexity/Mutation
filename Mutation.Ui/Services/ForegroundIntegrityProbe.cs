using Mutation.Ui.Core;
using System;
using System.Runtime.InteropServices;

namespace Mutation.Ui.Services;

/// <summary>
/// Asks Windows whether the window in front belongs to a process we are allowed to touch.
/// Three syscalls and no rules — the rule lives in <see cref="ForegroundInjectionGuard"/>,
/// which can be tested without an elevated process to aim at (issue #294).
/// </summary>
internal static class ForegroundIntegrityProbe
{
	/// <summary>
	/// The weakest access right that still identifies a process. Windows grants it for a
	/// process at our own integrity level or below; a refusal means the target sits above us.
	/// </summary>
	private const uint ProcessQueryLimitedInformation = 0x1000;

	[DllImport("user32.dll")]
	private static extern IntPtr GetForegroundWindow();

	[DllImport("user32.dll")]
	private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

	[DllImport("kernel32.dll", SetLastError = true)]
	private static extern IntPtr OpenProcess(uint dwDesiredAccess, bool bInheritHandle, uint dwProcessId);

	[DllImport("kernel32.dll", SetLastError = true)]
	[return: MarshalAs(UnmanagedType.Bool)]
	private static extern bool CloseHandle(IntPtr hObject);

	/// <summary>
	/// True when anything we inject into the foreground window will be silently discarded.
	/// False whenever that cannot be established, so a probe that fails for any other reason
	/// leaves delivery to proceed exactly as it did before.
	/// </summary>
	internal static bool ForegroundWindowWillDiscardInput() =>
		ForegroundInjectionGuard.InputWillBeDiscarded(Probe());

	/// <summary>
	/// One probe of the current foreground window. Swallows a failure to call Windows at all
	/// — a missing entry point on an unexpected platform — as "cannot tell", for the same
	/// reason the guard is conservative: this check exists to add a failure the app was
	/// missing, not to take away a delivery that would have worked.
	/// </summary>
	internal static ForegroundInjectionGuard.ProbeResult Probe()
	{
		try
		{
			IntPtr foreground = GetForegroundWindow();
			if (foreground == IntPtr.Zero)
				return ForegroundInjectionGuard.Classify(false, 0, false, 0);

			GetWindowThreadProcessId(foreground, out uint processId);
			if (processId == 0)
				return ForegroundInjectionGuard.Classify(true, 0, false, 0);

			IntPtr handle = OpenProcess(ProcessQueryLimitedInformation, false, processId);

			// Read straight after the call. Anything in between — a log line, a comparison
			// that itself sets an error — replaces the code we came for.
			int errorCode = handle == IntPtr.Zero ? Marshal.GetLastWin32Error() : 0;

			if (handle != IntPtr.Zero)
				CloseHandle(handle);

			return ForegroundInjectionGuard.Classify(true, processId, handle != IntPtr.Zero, errorCode);
		}
		catch (Exception ex)
		{
			System.Diagnostics.Debug.WriteLine($"Foreground integrity probe failed: {ex.Message}");
			return ForegroundInjectionGuard.ProbeResult.Unknown;
		}
	}
}
