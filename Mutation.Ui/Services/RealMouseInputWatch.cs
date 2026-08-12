using Mutation.Ui.Core;
using System;
using System.Runtime.InteropServices;
using System.Threading;

namespace Mutation.Ui.Services;

/// <summary>
/// Watches the machine's mouse events and counts what a hand has done, so the pointer hold and
/// the wiggle can tell a hand from a program without ever guessing.
///
/// <para>
/// It counts rather than latches (issue #382). The first version set a "user has the mouse" flag
/// on the first real event and kept it for ever — and measurement showed a hand merely resting on
/// a high-polling-rate mouse streams genuine hardware events continuously, so the flag was up
/// within milliseconds on every capture and the hold never corrected anything. Two monotonic
/// counters carry the same information without the verdict: <see cref="HandActs"/> for buttons
/// and wheel turns, <see cref="HandSteps"/> for hand-sized movement. A caller snapshots them,
/// waits, and compares; what the change means — stand down, follow the hand, undo a grab — is the
/// caller's rule, held in Core where it can be tested.
/// </para>
///
/// <para>
/// The classification itself is <see cref="RealMouseInput.Classify"/>. The one input it needs
/// from here besides the raw event is the step size — how far this event moved the pointer from
/// the previous one — because a driver moving the pointer produces events flagged exactly like
/// hardware, and step size is what gives it away.
/// </para>
///
/// <para>
/// A low-level hook is delivered on the thread that installed it, and that thread needs a message
/// loop, so <see cref="Start"/> and <see cref="Dispose"/> both have to be called on the UI
/// thread. The counters are read from elsewhere, hence the interlocked increments.
/// </para>
///
/// <para>
/// The hook sees every mouse event on the machine while it is installed, so it is installed only
/// while a capture has something to defend, and it does nothing but classify, count, and pass
/// the event on.
/// </para>
///
/// <para>
/// Whether it installed at all is the caller's business, not a detail to be swallowed. A watch
/// that never installed would report, for ever, that the hand has done nothing — and a hold
/// acting on that would spend its whole length hauling the pointer back from under the user's
/// hand, which is the exact behaviour it exists to avoid. So <see cref="IsWatching"/> is public,
/// and neither the hold nor the wiggle's reclaim runs without it.
/// </para>
/// </summary>
internal sealed class RealMouseInputWatch : IDisposable
{
	private const int WH_MOUSE_LL = 14;

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

	private long _handActs;
	private long _handSteps;

	// Where the previous move event put the pointer, whoever made it. Touched only on the hook
	// thread. The first move after installation has no previous event to measure a step against,
	// so it only seeds this and is not counted — an unjudgeable event must not count as a hand.
	private bool _hasLastPosition;
	private POINT _lastPosition;

	private RealMouseInputWatch()
	{
		_callback = OnMouseInput;
	}

	/// <summary>
	/// How many deliberate acts — buttons and wheel turns — have been seen. Monotonic; compare
	/// across a wait to learn whether one happened during it.
	/// </summary>
	public long HandActs => Interlocked.Read(ref _handActs);

	/// <summary>
	/// How many hand-sized real movement events have been seen. Monotonic, compared the same
	/// way. A resting hand advances this too — that is the point; what the caller does about
	/// hand movement is the caller's rule.
	/// </summary>
	public long HandSteps => Interlocked.Read(ref _handSteps);

	/// <summary>
	/// Whether the hook is actually installed. False means there is no signal at all, and a
	/// caller that would have acted on the absence of one must do nothing instead.
	/// </summary>
	public bool IsWatching => _hook != IntPtr.Zero;

	/// <summary>
	/// Installs the hook on the calling thread, which must be the UI thread. Never throws; check
	/// <see cref="IsWatching"/> to find out whether it worked. It can fail for reasons that have
	/// nothing to do with this code — endpoint-protection software commonly refuses global hooks
	/// — so the failure is reported rather than assumed away.
	/// </summary>
	public static RealMouseInputWatch Start()
	{
		var watch = new RealMouseInputWatch();
		try
		{
			using var process = System.Diagnostics.Process.GetCurrentProcess();
			using var module = process.MainModule;
			watch._hook = SetWindowsHookEx(WH_MOUSE_LL, watch._callback, GetModuleHandle(module?.ModuleName), 0);
			if (watch._hook == IntPtr.Zero)
			{
				System.Diagnostics.Debug.WriteLine(
					$"RealMouseInputWatch: SetWindowsHookEx failed, GetLastError={Marshal.GetLastWin32Error()}. "
					+ "The pointer hold will stand down rather than move the pointer blind.");
			}
		}
		catch (Exception ex)
		{
			System.Diagnostics.Debug.WriteLine($"RealMouseInputWatch: could not install the hook: {ex.Message}");
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
				var info = Marshal.PtrToStructure<MSLLHOOKSTRUCT>(lParam);
				int message = (int)wParam;

				bool measurable = _hasLastPosition;
				int step = measurable
					? Math.Max(Math.Abs(info.pt.x - _lastPosition.x), Math.Abs(info.pt.y - _lastPosition.y))
					: 0;

				if (message == RealMouseInput.MouseMove)
				{
					// Every move re-seeds, including synthetic ones: the step that matters is
					// from wherever the pointer last was, not from where a hand last left it —
					// otherwise a hand step right after a program's move would measure the
					// program's distance and be misread as a teleport.
					_lastPosition = info.pt;
					_hasLastPosition = true;
				}

				switch (RealMouseInput.Classify(message, info.flags, step))
				{
					case RealMouseEventKind.HandAct:
						Interlocked.Increment(ref _handActs);
						break;
					case RealMouseEventKind.HandStep when measurable:
						Interlocked.Increment(ref _handSteps);
						break;
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
}
