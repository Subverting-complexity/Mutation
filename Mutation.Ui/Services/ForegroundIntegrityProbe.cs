using Mutation.Ui.Core;
using System;
using System.Runtime.InteropServices;

namespace Mutation.Ui.Services;

/// <summary>
/// Finds out how much privilege the window in front is running with, compared to us. All
/// P/Invoke and no rules — the rule lives in <see cref="ForegroundInjectionGuard"/>, which can
/// be tested without an elevated process to aim at (issue #294).
/// </summary>
internal static class ForegroundIntegrityProbe
{
	/// <summary>
	/// The weakest access right that still identifies a process. Windows grants it across an
	/// integrity boundary on purpose, so that an ordinary process can name a more privileged one
	/// — which is why being granted this is not the test. A refusal of it means a DACL said no,
	/// which is a different question entirely.
	/// </summary>
	private const uint ProcessQueryLimitedInformation = 0x1000;

	/// <summary>
	/// The right <c>OpenProcessToken</c> needs, and the one an integrity boundary refuses. Asked
	/// for separately from the limited right so the two answers can be told apart.
	/// </summary>
	private const uint ProcessQueryInformation = 0x0400;

	private const uint TokenQuery = 0x0008;

	/// <summary><c>TOKEN_INFORMATION_CLASS.TokenIntegrityLevel</c>.</summary>
	private const uint TokenIntegrityLevel = 25;

	private const int ErrorInsufficientBuffer = 122;

	[DllImport("user32.dll")]
	private static extern IntPtr GetForegroundWindow();

	[DllImport("user32.dll")]
	private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

	[DllImport("kernel32.dll", SetLastError = true)]
	private static extern IntPtr OpenProcess(uint dwDesiredAccess, bool bInheritHandle, uint dwProcessId);

	[DllImport("kernel32.dll")]
	private static extern IntPtr GetCurrentProcess();

	[DllImport("kernel32.dll", SetLastError = true)]
	[return: MarshalAs(UnmanagedType.Bool)]
	private static extern bool CloseHandle(IntPtr hObject);

	[DllImport("advapi32.dll", SetLastError = true)]
	[return: MarshalAs(UnmanagedType.Bool)]
	private static extern bool OpenProcessToken(IntPtr processHandle, uint desiredAccess, out IntPtr tokenHandle);

	[DllImport("advapi32.dll", SetLastError = true)]
	[return: MarshalAs(UnmanagedType.Bool)]
	private static extern bool GetTokenInformation(
		IntPtr tokenHandle,
		uint tokenInformationClass,
		IntPtr tokenInformation,
		uint tokenInformationLength,
		out uint returnLength);

	[DllImport("advapi32.dll")]
	private static extern IntPtr GetSidSubAuthority(IntPtr sid, uint subAuthorityIndex);

	[DllImport("advapi32.dll")]
	private static extern IntPtr GetSidSubAuthorityCount(IntPtr sid);

	/// <summary>
	/// True when anything we inject into the foreground window will be silently discarded.
	/// False whenever that cannot be established, so a probe that fails for any other reason
	/// leaves delivery to proceed exactly as it did before.
	/// </summary>
	internal static bool ForegroundWindowWillDiscardInput()
	{
		var (probe, foregroundIntegrity, ownIntegrity) = Probe();
		return ForegroundInjectionGuard.InputWillBeDiscarded(probe, foregroundIntegrity, ownIntegrity);
	}

	/// <summary>
	/// One probe of the current foreground window: what could be established, and the two
	/// integrity levels when both were read.
	/// <para>
	/// Swallows a failure to call Windows at all — a missing entry point on an unexpected
	/// platform — as "cannot tell", for the same reason the guard is conservative: this check
	/// exists to add a failure the app was missing, not to take away a delivery that would have
	/// worked.
	/// </para>
	/// </summary>
	internal static (ForegroundInjectionGuard.ForegroundProbe Probe, uint ForegroundIntegrity, uint OwnIntegrity) Probe()
	{
		var unknown = (ForegroundInjectionGuard.ForegroundProbe.Unknown, 0u, 0u);
		var failed = ForegroundInjectionGuard.ProbeStep.Failed;

		try
		{
			IntPtr foreground = GetForegroundWindow();
			if (foreground == IntPtr.Zero)
				return unknown;

			GetWindowThreadProcessId(foreground, out uint processId);
			if (processId == 0)
				return unknown;

			var limitedOpen = TryOpen(processId, ProcessQueryLimitedInformation, out IntPtr limited);
			if (limited != IntPtr.Zero)
				CloseHandle(limited);

			if (limitedOpen != ForegroundInjectionGuard.ProbeStep.Succeeded)
				return (ForegroundInjectionGuard.Classify(true, processId, limitedOpen, failed, failed), 0u, 0u);

			var fullOpen = TryOpen(processId, ProcessQueryInformation, out IntPtr process);
			if (process == IntPtr.Zero)
				return (ForegroundInjectionGuard.Classify(true, processId, limitedOpen, fullOpen, failed), 0u, 0u);

			try
			{
				bool tokenOpened = OpenProcessToken(process, TokenQuery, out IntPtr token);
				int tokenError = tokenOpened ? 0 : Marshal.GetLastWin32Error();
				var tokenRead = ForegroundInjectionGuard.StepFrom(tokenOpened, tokenError);

				if (!tokenOpened)
					return (ForegroundInjectionGuard.Classify(true, processId, limitedOpen, fullOpen, tokenRead), 0u, 0u);

				try
				{
					uint? theirs = IntegrityLevelOf(token);
					uint? ours = OwnIntegrityLevel();

					if (theirs is null || ours is null)
						return unknown;

					return (
						ForegroundInjectionGuard.Classify(true, processId, limitedOpen, fullOpen, tokenRead),
						theirs.Value,
						ours.Value);
				}
				finally
				{
					CloseHandle(token);
				}
			}
			finally
			{
				CloseHandle(process);
			}
		}
		catch (Exception ex)
		{
			System.Diagnostics.Debug.WriteLine($"Foreground integrity probe failed: {ex.Message}");
			return unknown;
		}
	}

	/// <summary>
	/// Opens <paramref name="processId"/> for <paramref name="access"/> and reports how it went.
	/// The error code is read on the line after the call: anything in between — a log line, a
	/// comparison that itself sets an error — replaces the code we came for.
	/// </summary>
	private static ForegroundInjectionGuard.ProbeStep TryOpen(uint processId, uint access, out IntPtr handle)
	{
		handle = OpenProcess(access, false, processId);
		int errorCode = handle == IntPtr.Zero ? Marshal.GetLastWin32Error() : 0;
		return ForegroundInjectionGuard.StepFrom(handle != IntPtr.Zero, errorCode);
	}

	/// <summary>
	/// Our own integrity level. The pseudo-handle from <c>GetCurrentProcess</c> needs no
	/// closing and can always be opened, so this only fails if the token cannot be read at all.
	/// </summary>
	private static uint? OwnIntegrityLevel()
	{
		if (!OpenProcessToken(GetCurrentProcess(), TokenQuery, out IntPtr token))
			return null;

		try
		{
			return IntegrityLevelOf(token);
		}
		finally
		{
			CloseHandle(token);
		}
	}

	/// <summary>
	/// The integrity level in <paramref name="token"/>, or null when it could not be read.
	/// <para>
	/// Asked for twice, as Windows requires: once with no buffer, to be told how big one has to
	/// be, and once with a buffer that size. What comes back is a <c>TOKEN_MANDATORY_LABEL</c>,
	/// whose only field is a pointer to the S-1-16-x SID; the level is that SID's last
	/// sub-authority.
	/// </para>
	/// </summary>
	private static uint? IntegrityLevelOf(IntPtr token)
	{
		if (GetTokenInformation(token, TokenIntegrityLevel, IntPtr.Zero, 0, out uint size))
			return null; // Cannot happen with a zero-length buffer, and a success here tells us nothing.

		if (Marshal.GetLastWin32Error() != ErrorInsufficientBuffer || size == 0)
			return null;

		IntPtr buffer = Marshal.AllocHGlobal((int)size);
		try
		{
			if (!GetTokenInformation(token, TokenIntegrityLevel, buffer, size, out _))
				return null;

			// TOKEN_MANDATORY_LABEL is a single SID_AND_ATTRIBUTES, so the SID pointer is the
			// first field.
			IntPtr sid = Marshal.ReadIntPtr(buffer);
			if (sid == IntPtr.Zero)
				return null;

			IntPtr countPtr = GetSidSubAuthorityCount(sid);
			if (countPtr == IntPtr.Zero)
				return null;

			int count = Marshal.ReadByte(countPtr);
			if (count <= 0)
				return null;

			IntPtr levelPtr = GetSidSubAuthority(sid, (uint)(count - 1));
			if (levelPtr == IntPtr.Zero)
				return null;

			return unchecked((uint)Marshal.ReadInt32(levelPtr));
		}
		finally
		{
			Marshal.FreeHGlobal(buffer);
		}
	}
}
