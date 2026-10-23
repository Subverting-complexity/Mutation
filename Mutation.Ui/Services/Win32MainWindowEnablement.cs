using Mutation.Ui.Core;
using System;
using System.Runtime.InteropServices;

namespace Mutation.Ui.Services;

/// <summary>
/// <see cref="IMainWindowEnablement"/> for a real window handle. All P/Invoke, no rules; the
/// rules are in <see cref="MainWindowActivationLock"/>.
/// </summary>
internal sealed class Win32MainWindowEnablement : IMainWindowEnablement
{
	[DllImport("user32.dll")]
	private static extern IntPtr GetForegroundWindow();

	[DllImport("user32.dll")]
	[return: MarshalAs(UnmanagedType.Bool)]
	private static extern bool IsWindowEnabled(IntPtr hWnd);

	[DllImport("user32.dll")]
	[return: MarshalAs(UnmanagedType.Bool)]
	private static extern bool EnableWindow(IntPtr hWnd, [MarshalAs(UnmanagedType.Bool)] bool bEnable);

	private readonly IntPtr _window;

	public Win32MainWindowEnablement(IntPtr window) => _window = window;

	public bool IsForeground => GetForegroundWindow() == _window;

	public bool IsEnabled => IsWindowEnabled(_window);

	public void SetEnabled(bool enabled) => EnableWindow(_window, enabled);
}
