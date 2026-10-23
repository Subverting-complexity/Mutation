using Mutation.Ui.Core;
using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;

namespace Mutation.Ui.Services;

/// <summary>
/// Watches the foreground for the life of one screen capture: writes every change to the
/// capture log, pulls the overlay back if the main window takes the foreground while the
/// overlay is open, and keeps the main window disabled so it cannot take it in the first place.
///
/// <para>
/// Issue #446. The rules are in <see cref="CaptureForegroundTrace"/> and
/// <see cref="MainWindowActivationLock"/>; this class is the Win32 around them. The watch is a
/// WinEvent hook for <c>EVENT_SYSTEM_FOREGROUND</c>, installed out of context on the UI thread,
/// so Windows delivers each change to this thread's message loop and never calls into another
/// process. That also means a change is seen a message or two after it happened, not before:
/// the hook records and repairs a stolen foreground, while the disabled main window is what
/// prevents it.
/// </para>
///
/// <para>
/// Started on the UI thread and ended there. Every failure is swallowed and written to the
/// capture log: a capture must not fail because the thing watching it could not be set up.
/// </para>
/// </summary>
internal sealed class CaptureFocusGuard : IDisposable
{
	private const uint EVENT_SYSTEM_FOREGROUND = 0x0003;
	private const uint WINEVENT_OUTOFCONTEXT = 0x0000;

	// A ceiling on how often one capture pulls the overlay back. Something that kept taking the
	// foreground for the main window would otherwise turn this into a tug of war the user hears
	// as an endless run of focus changes.
	private const int MaxReclaims = 3;

	private delegate void WinEventProc(IntPtr hWinEventHook, uint eventType, IntPtr hwnd, int idObject, int idChild, uint idEventThread, uint dwmsEventTime);

	[DllImport("user32.dll")]
	private static extern IntPtr SetWinEventHook(uint eventMin, uint eventMax, IntPtr hmodWinEventProc, WinEventProc lpfnWinEventProc, uint idProcess, uint idThread, uint dwFlags);

	[DllImport("user32.dll")]
	[return: MarshalAs(UnmanagedType.Bool)]
	private static extern bool UnhookWinEvent(IntPtr hWinEventHook);

	[DllImport("user32.dll")]
	private static extern IntPtr GetForegroundWindow();

	[DllImport("user32.dll")]
	[return: MarshalAs(UnmanagedType.Bool)]
	private static extern bool IsWindowVisible(IntPtr hWnd);

	[DllImport("user32.dll", CharSet = CharSet.Unicode)]
	private static extern int GetClassName(IntPtr hWnd, StringBuilder lpClassName, int nMaxCount);

	[DllImport("user32.dll")]
	private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

	private readonly CaptureForegroundTrace _trace;
	private readonly Action _reclaim;
	private readonly IDisposable? _hold;
	private readonly IntPtr _overlay;
	private IntPtr _hook;
	private int _reclaims;

	// Held in a field for as long as the hook is installed: the delegate is handed to native
	// code, and a delegate the collector has taken crashes the process on the next event.
	private WinEventProc? _proc;

	private CaptureFocusGuard(CaptureForegroundTrace trace, IntPtr overlay, Action reclaim, IDisposable? hold)
	{
		_trace = trace;
		_overlay = overlay;
		_reclaim = reclaim;
		_hold = hold;
	}

	/// <summary>
	/// Starts watching. Call before the overlay is shown, so the change it causes is recorded
	/// and the main window is already disabled when it happens.
	/// </summary>
	/// <param name="mainWindow">The main window, or zero when there is none to protect.</param>
	/// <param name="overlay">The capture overlay's window.</param>
	/// <param name="activationLock">The lock that disables the main window; null when there is no main window.</param>
	/// <param name="reclaim">Brings the overlay back to the front. Runs on the UI thread.</param>
	public static CaptureFocusGuard Begin(IntPtr mainWindow, IntPtr overlay, MainWindowActivationLock? activationLock, Action reclaim)
	{
		var trace = new CaptureForegroundTrace(mainWindow, overlay);
		IDisposable? hold = null;
		try
		{
			hold = activationLock?.Hold();
		}
		catch (Exception ex)
		{
			CaptureLog.Write($"focus guard: could not disable the main window: {ex.Message}");
		}

		var guard = new CaptureFocusGuard(trace, overlay, reclaim, hold);
		IntPtr foreground = SafeForeground();
		CaptureLog.Write(
			$"capture opening; {guard.DescribeWindow(foreground)}; main window "
			+ (activationLock?.IsHoldingWindowDisabled == true ? "disabled for the capture" : "left as it was"));
		guard.InstallHook();
		return guard;
	}

	/// <summary>
	/// Records that the selection has finished. The watch carries on until <see cref="Dispose"/>,
	/// so the hand-back is in the log too, but it no longer pulls the overlay back.
	/// </summary>
	public void OverlayClosed() => _trace.MarkOverlayClosed();

	/// <summary>Stops watching and enables the main window again. Safe to call more than once.</summary>
	public void Dispose()
	{
		_trace.MarkOverlayClosed();
		if (_hook != IntPtr.Zero)
		{
			try { UnhookWinEvent(_hook); } catch { }
			_hook = IntPtr.Zero;
			_proc = null;
			CaptureLog.Write($"capture finished; {DescribeWindow(SafeForeground())}");
		}
		try { _hold?.Dispose(); }
		catch (Exception ex) { CaptureLog.Write($"focus guard: could not enable the main window: {ex.Message}"); }
	}

	private void InstallHook()
	{
		try
		{
			_proc = OnForegroundChanged;
			// Out of context and for every process: the change that matters may be made by
			// another process, and only this thread's message loop has to run the callback.
			_hook = SetWinEventHook(EVENT_SYSTEM_FOREGROUND, EVENT_SYSTEM_FOREGROUND, IntPtr.Zero, _proc, 0, 0, WINEVENT_OUTOFCONTEXT);
			if (_hook == IntPtr.Zero)
			{
				_proc = null;
				CaptureLog.Write("focus guard: could not watch the foreground; changes during this capture are not logged");
			}
		}
		catch (Exception ex)
		{
			_hook = IntPtr.Zero;
			_proc = null;
			CaptureLog.Write($"focus guard: could not watch the foreground: {ex.Message}");
		}
	}

	private void OnForegroundChanged(IntPtr hWinEventHook, uint eventType, IntPtr hwnd, int idObject, int idChild, uint idEventThread, uint dwmsEventTime)
	{
		try
		{
			if (_hook == IntPtr.Zero)
				return;
			CaptureLog.Write(DescribeWindow(hwnd));
			// The overlay is hidden before the selection's result reaches the capture, so a
			// hand-back to the main window can arrive here while the trace still calls the
			// overlay open. Pulling a hidden overlay back would put a finished capture on
			// screen again, so a hidden overlay is never reclaimed.
			if (_trace.ShouldReclaim(hwnd) && IsWindowVisible(_overlay))
			{
				if (_reclaims >= MaxReclaims)
				{
					CaptureLog.Write("focus guard: not taking the foreground back again this capture");
					return;
				}
				_reclaims++;
				_reclaim();
			}
		}
		catch (Exception ex)
		{
			Debug.WriteLine($"CaptureFocusGuard callback failed: {ex.Message}");
		}
	}

	private string DescribeWindow(IntPtr hwnd)
	{
		if (_trace.Classify(hwnd) != CaptureWindowRole.Other)
			return _trace.Describe(hwnd, null, null);
		return _trace.Describe(hwnd, ReadClassName(hwnd), ReadProcessName(hwnd));
	}

	private static IntPtr SafeForeground()
	{
		try { return GetForegroundWindow(); } catch { return IntPtr.Zero; }
	}

	private static string? ReadClassName(IntPtr hwnd)
	{
		try
		{
			var buffer = new StringBuilder(256);
			return GetClassName(hwnd, buffer, buffer.Capacity) > 0 ? buffer.ToString() : null;
		}
		catch { return null; }
	}

	private static string? ReadProcessName(IntPtr hwnd)
	{
		try
		{
			GetWindowThreadProcessId(hwnd, out uint processId);
			if (processId == 0)
				return null;
			using var process = Process.GetProcessById((int)processId);
			return process.ProcessName;
		}
		catch { return null; }
	}
}
