using Mutation.Ui.Core;
using System;
using System.Runtime.InteropServices;

namespace Mutation.Ui.Services;

/// <summary>
/// Reads and writes the mouse pointer position through <c>user32</c>. All P/Invoke and no
/// rules — the rules about when the pointer may be moved live in <see cref="CursorAnchor"/>,
/// which can be tested without a real mouse.
/// <para>
/// Both calls work in virtual-screen pixels across every monitor, so a position read here can be
/// written back here unchanged. Neither is tied to a particular thread, which is what lets the
/// capture restore the pointer from the continuation that runs after the foreground has been
/// handed back rather than having to get back onto the UI thread first.
/// </para>
/// </summary>
internal sealed class Win32CursorPosition : ICursorPosition
{
	[StructLayout(LayoutKind.Sequential)]
	private struct POINT
	{
		public int X;
		public int Y;
	}

	[DllImport("user32.dll", SetLastError = false)]
	private static extern bool GetCursorPos(out POINT lpPoint);

	[DllImport("user32.dll", SetLastError = false)]
	private static extern bool SetCursorPos(int X, int Y);

	public bool TryGet(out CursorPoint position)
	{
		try
		{
			if (GetCursorPos(out POINT p))
			{
				position = new CursorPoint(p.X, p.Y);
				return true;
			}
		}
		catch
		{
			// Falls through to the failure below. A pointer position that cannot be read must
			// never be the thing that breaks a capture.
		}

		position = default;
		return false;
	}

	public bool TrySet(CursorPoint position)
	{
		try
		{
			return SetCursorPos(position.X, position.Y);
		}
		catch
		{
			return false;
		}
	}
}
