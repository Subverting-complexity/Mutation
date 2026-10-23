using System;

namespace Mutation.Ui.Core;

/// <summary>
/// Which window took the foreground during a capture, said in words for the capture log, and
/// whether the overlay has to take it back.
///
/// <para>
/// Issue #446 could not be reproduced on demand: on some captures, not all, the main window
/// became active for a moment before the overlay did, and a screen reader read it out. The
/// capture log now records every foreground change from the moment a capture opens until the
/// keyboard has been handed back, so the next report can be checked against what happened
/// rather than guessed at. This class is the part of that which needs no window: naming each
/// change, and deciding what to do about it.
/// </para>
///
/// <para>
/// The overlay takes the foreground back only when the main window took it while the overlay
/// was still open. Anything else is left alone: the overlay taking it is the point, another
/// application taking it is the user's choice (or the hand-back at the end), and once the
/// selection has finished, pulling the overlay back would put a closed capture on screen
/// again.
/// </para>
/// </summary>
public sealed class CaptureForegroundTrace
{
	private readonly IntPtr _mainWindow;
	private readonly IntPtr _overlay;
	private bool _overlayOpen = true;

	public CaptureForegroundTrace(IntPtr mainWindow, IntPtr overlay)
	{
		_mainWindow = mainWindow;
		_overlay = overlay;
	}

	/// <summary>Whether the overlay is still waiting for a selection.</summary>
	public bool OverlayOpen => _overlayOpen;

	/// <summary>Records that the selection has finished, so the overlay must not be pulled back.</summary>
	public void MarkOverlayClosed() => _overlayOpen = false;

	/// <summary>What kind of window <paramref name="window"/> is, from this capture's point of view.</summary>
	public CaptureWindowRole Classify(IntPtr window)
	{
		if (window == IntPtr.Zero)
			return CaptureWindowRole.None;
		if (window == _overlay)
			return CaptureWindowRole.Overlay;
		if (window == _mainWindow)
			return CaptureWindowRole.MainWindow;
		return CaptureWindowRole.Other;
	}

	/// <summary>
	/// True when <paramref name="window"/> taking the foreground means the overlay has to take it
	/// back: it is the main window, and the overlay is still open.
	/// </summary>
	public bool ShouldReclaim(IntPtr window) =>
		_overlayOpen && Classify(window) == CaptureWindowRole.MainWindow;

	/// <summary>
	/// One capture-log line for a foreground change. <paramref name="className"/> and
	/// <paramref name="processName"/> are only for windows that are not Mutation's own, and may
	/// be empty when they could not be read.
	/// </summary>
	public string Describe(IntPtr window, string? className, string? processName)
	{
		var role = Classify(window);
		string who = role switch
		{
			CaptureWindowRole.None => "no window",
			CaptureWindowRole.Overlay => "the capture overlay",
			CaptureWindowRole.MainWindow => "the MAIN WINDOW",
			_ => DescribeOther(className, processName),
		};
		string line = $"foreground: {who} hwnd=0x{window.ToInt64():X}";
		if (ShouldReclaim(window))
			line += " (while the overlay is open; taking it back)";
		return line;
	}

	private static string DescribeOther(string? className, string? processName)
	{
		string process = string.IsNullOrWhiteSpace(processName) ? "unknown process" : processName!;
		string cls = string.IsNullOrWhiteSpace(className) ? "unknown class" : className!;
		return $"another window ({process}, {cls})";
	}
}
