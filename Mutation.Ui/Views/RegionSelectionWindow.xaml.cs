using Microsoft.UI.Dispatching;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media.Imaging;
using Mutation.Ui.Core;
using Mutation.Ui.Services;
using System;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using Windows.Foundation;
using Windows.Graphics;
using Windows.Graphics.Imaging;
using Windows.Storage.Streams;

namespace Mutation.Ui.Views;

public sealed partial class RegionSelectionWindow : Window
{
	private Point _start;
	private bool _dragging;
	private TaskCompletionSource<Rect?>? _tcs;
	private readonly IntPtr _hwnd;
	private int _bmpW;
	private int _bmpH;
	private Point? _lastPointerPos; // track last known position in overlay coords
	private bool _initialLayoutDone;
	private IntPtr _previousForeground;
	private readonly DispatcherQueue? _dispatcherQueue;

	/// <summary>
	/// Completed when <see cref="HideAndRestore"/> has finished with the foreground — either the
	/// window that was in front before the overlay opened has the keyboard back, or the retry
	/// ladder has given up on it.
	/// </summary>
	private TaskCompletionSource? _foregroundHandback;

	// Keyboard path for the overlay, so the four screenshot/OCR hotkeys are completable
	// without sight of the crosshair (issue #215).
	private readonly KeyboardRegionSelector _keyboard = new();

	/// <summary>The one way this window reads or writes the mouse pointer position.</summary>
	private readonly ICursorPosition _cursor = new Win32CursorPosition();

	/// <summary>
	/// How the wiggle moves the pointer, as opposed to how everything else does. It reports each
	/// move as real mouse input as well as placing the pointer, because a magnifier watches the
	/// mouse through the input stream and a placed cursor is invisible to it (issue #377). Only
	/// the wiggle needs that: putting the pointer back where the user left it has to be exact,
	/// and has no need to be noticed.
	/// </summary>
	private readonly ICursorPosition _nudgeCursor;

	/// <summary>
	/// Holds the mouse pointer still across the focus changes a capture causes. Opening this
	/// overlay and closing it again both move the foreground, and a magnifier or reader that
	/// follows focus answers that by moving the pointer — so the pointer is read before each
	/// change and put back after it (issue #371).
	/// </summary>
	private readonly CursorAnchor _cursorAnchor;

	/// <summary>
	/// Whether to nudge the pointer at each end of this capture, and how. Set by the caller
	/// before each capture rather than read from settings here, so the overlay stays free of
	/// settings and a change made in the dialog takes effect on the next capture.
	/// </summary>
	public PointerNudgeOptions PointerNudge { get; set; } = PointerNudgeOptions.Off;

	/// <summary>
	/// How long to keep the pointer on the spot this capture left it once the capture is over.
	/// Zero switches the watch off. Set by the caller before each capture, like
	/// <see cref="PointerNudge"/>.
	/// </summary>
	public TimeSpan PointerHoldFor { get; set; } = TimeSpan.Zero;

	/// <summary>
	/// How often the hold looks at the pointer. Short enough that a jump is corrected before the
	/// user has taken it in, long enough not to spend the whole hold reading the cursor position.
	/// </summary>
	private static readonly TimeSpan PointerHoldPollInterval = TimeSpan.FromMilliseconds(40);

	/// <summary>
	/// True while a wiggle is moving the pointer, so the rest of the overlay can tell the app's
	/// own movement from the user's.
	/// <para>
	/// It matters because the wiggle at the opening end runs while the overlay is live and on
	/// screen. Every move it makes arrives back as a genuine pointer event, and without this the
	/// overlay would answer its own wiggle as though a hand had moved the mouse.
	/// </para>
	/// <para>
	/// Written by the wiggle off the UI thread and read on it, so it is volatile. Nothing here
	/// needs the two to agree to the instant: read a moment late and the overlay treats one of
	/// its own moves as the user's, which costs a pixel of crosshair, not a wrong capture.
	/// </para>
	/// </summary>
	private volatile bool _nudgeOwnsPointer;

	/// <summary>
	/// Counts what the user's hand does with the mouse for the length of one capture, so the
	/// wiggle and the hold can tell the hand from a program (issue #382). Started in
	/// <see cref="SelectRegionAsync"/> when there is a wiggle or a hold to serve, and removed
	/// when the capture's pointer work is over. Null, or not watching, means no signal — and
	/// everything that would have leaned on the signal stands down instead of guessing.
	/// </summary>
	private RealMouseInputWatch? _mouseWatch;
	private const string AnnouncementActivityId = "RegionSelection";
	private const string CancelledAnnouncement = "Region selection cancelled.";

	/// <summary>
	/// Said only when the overlay could not take focus. Normally the Canvas's
	/// <c>AutomationProperties.HelpText</c> carries the instructions and a reader reads them
	/// out as focus lands — see <see cref="SelectRegionAsync"/>.
	/// </summary>
	private const string InstructionsAnnouncement =
		"Select screen region. Move with the arrow keys, press Enter to set the first corner, move, then Enter again to capture. Control plus A selects the whole screen. Escape cancels.";

	// Cache XAML elements to avoid reliance on generated fields
	private Microsoft.UI.Xaml.Controls.Image? _img;
	private Microsoft.UI.Xaml.Controls.Canvas? _overlay;
	private Microsoft.UI.Xaml.Shapes.Rectangle? _selection;
	private Microsoft.UI.Xaml.Shapes.Rectangle? _crosshairV;
	private Microsoft.UI.Xaml.Shapes.Rectangle? _crosshairH;

	[DllImport("user32.dll", SetLastError = false)]
	private static extern IntPtr LoadCursor(IntPtr hInstance, int lpCursorName);
	[DllImport("user32.dll", SetLastError = false)]
	private static extern IntPtr SetCursor(IntPtr hCursor);
	private const int IDC_CROSS = 32515;
	private const int IDC_ARROW = 32512;
	private static readonly IntPtr CURSOR_CROSS = LoadCursor(IntPtr.Zero, IDC_CROSS);
	private static readonly IntPtr CURSOR_ARROW = LoadCursor(IntPtr.Zero, IDC_ARROW);

	[DllImport("user32.dll", SetLastError = false)]
	private static extern uint GetDpiForWindow(IntPtr hWnd);

	[DllImport("user32.dll")]
	private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int X, int Y, int cx, int cy, uint uFlags);

	[DllImport("user32.dll")]
	private static extern bool SetForegroundWindow(IntPtr hWnd);

	[DllImport("user32.dll")]
	private static extern IntPtr GetForegroundWindow();

	[DllImport("user32.dll")]
	private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

	[DllImport("user32.dll")]
	private static extern bool AttachThreadInput(uint idAttach, uint idAttachTo, bool fAttach);

	[DllImport("user32.dll")]
	private static extern bool BringWindowToTop(IntPtr hWnd);

	[DllImport("user32.dll")]
	private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

	[DllImport("user32.dll")]
	private static extern bool IsIconic(IntPtr hWnd);

	[DllImport("user32.dll")]
	private static extern bool IsWindow(IntPtr hWnd);

	[DllImport("user32.dll")]
	private static extern bool IsWindowVisible(IntPtr hWnd);

	[DllImport("user32.dll")]
	private static extern IntPtr SetActiveWindow(IntPtr hWnd);

	[DllImport("user32.dll")]
	private static extern IntPtr SetFocus(IntPtr hWnd);

	[DllImport("user32.dll")]
	private static extern bool AllowSetForegroundWindow(uint dwProcessId);

	[DllImport("kernel32.dll")]
	private static extern uint GetCurrentThreadId();

	// Low-level keyboard hook for global Escape key capture
	private delegate IntPtr LowLevelKeyboardProc(int nCode, IntPtr wParam, IntPtr lParam);
	[DllImport("user32.dll", SetLastError = true)]
	private static extern IntPtr SetWindowsHookEx(int idHook, LowLevelKeyboardProc lpfn, IntPtr hMod, uint dwThreadId);
	[DllImport("user32.dll", SetLastError = true)]
	private static extern bool UnhookWindowsHookEx(IntPtr hhk);
	[DllImport("user32.dll")]
	private static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);
	[DllImport("kernel32.dll", CharSet = CharSet.Auto)]
	private static extern IntPtr GetModuleHandle(string? lpModuleName);
	private const int WH_KEYBOARD_LL = 13;
	private const int WM_KEYDOWN = 0x0100;
	private const int VK_ESCAPE = 0x1B;
	private IntPtr _keyboardHookHandle = IntPtr.Zero;
	private LowLevelKeyboardProc? _keyboardHookProc;

	private static readonly IntPtr HWND_TOPMOST = new(-1);
	private const uint SWP_SHOWWINDOW = 0x0040; // keep for reference, but avoid using to prevent flicker
	private const uint SWP_NOMOVE = 0x0002;
	private const uint SWP_NOSIZE = 0x0001;
	private const uint SWP_NOACTIVATE = 0x0010;
	private const int SW_RESTORE = 9;
	private const int FocusRetryDelayMilliseconds = 50; // Allow Windows message pump to settle before retrying focus restoration

	[DllImport("user32.dll")]
	private static extern int GetSystemMetrics(int nIndex);

	private const int SM_XVIRTUALSCREEN = 76;
	private const int SM_YVIRTUALSCREEN = 77;
	private const int SM_CXVIRTUALSCREEN = 78;
	private const int SM_CYVIRTUALSCREEN = 79;
	public RegionSelectionWindow()
	{
		this.InitializeComponent();
		_cursorAnchor = new CursorAnchor(_cursor);
		_nudgeCursor = new ReportedCursorPosition(_cursor);
		_hwnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
		_dispatcherQueue = DispatcherQueue.GetForCurrentThread();
		EnsureElementRefs();
	}

	public void BringToFront()
	{
		try
		{
			// A second hotkey press on a capture that is already waiting drags the overlay back
			// to the front, which is one more focus change and one more chance for the pointer
			// to be moved out from under the user. Skipped entirely mid-drag: the pointer is
			// then the user's drawing hand, and the position to defend is wherever they have
			// got to, not wherever they started.
			// Same reason as in CompleteSelection: re-anchor on the pointer, not on a pixel a
			// wiggle from this capture's opening is still parked on (issue #375).
			SettleAnyNudgeInProgress();
			bool anchored = !_dragging && _cursorAnchor.Capture();
			this.Activate();
			SetForegroundWindow(_hwnd);
			int left = GetSystemMetrics(SM_XVIRTUALSCREEN);
			int top = GetSystemMetrics(SM_YVIRTUALSCREEN);
			int width = GetSystemMetrics(SM_CXVIRTUALSCREEN);
			int height = GetSystemMetrics(SM_CYVIRTUALSCREEN);
			SetWindowPos(_hwnd, HWND_TOPMOST, left, top, width, height, SWP_NOMOVE | SWP_NOSIZE);
			TryFocusOverlay();
			if (anchored)
				RestoreCursorNowAndAfterFocusSettles();
		}
		catch { }
	}

	public Task InitializeAsync(SoftwareBitmap bitmap)
	{
		PrepareWindow();
		UpdateBitmap(bitmap);
		return Task.CompletedTask;
	}

	private void PrepareWindow()
	{
		// Ensure refs in case constructor timing varies
		EnsureElementRefs();
		// Prepare window for full-bleed content and no chrome; size to full virtual screen
		int left = GetSystemMetrics(SM_XVIRTUALSCREEN);
		int top = GetSystemMetrics(SM_YVIRTUALSCREEN);
		int width = GetSystemMetrics(SM_CXVIRTUALSCREEN);
		int height = GetSystemMetrics(SM_CYVIRTUALSCREEN);

		var appWindow = this.AppWindow;
		if (appWindow != null)
		{
			try
			{
				appWindow.MoveAndResize(new RectInt32(left, top, width, height));
				if (appWindow.Presenter is OverlappedPresenter presenter)
				{
					presenter.IsResizable = false;
					presenter.IsMaximizable = false;
					presenter.IsMinimizable = false;
					presenter.SetBorderAndTitleBar(false, false);
				}
			}
			catch { }
		}
		try
		{
			SetWindowPos(_hwnd, HWND_TOPMOST, left, top, width, height, SWP_NOACTIVATE);
		}
		catch { }
		// Expand content into title bar area (hide chrome).
		if (this.AppWindow?.TitleBar is AppWindowTitleBar tb)
		{
			tb.ExtendsContentIntoTitleBar = true;
			tb.ButtonBackgroundColor = Windows.UI.Color.FromArgb(1, 0, 0, 0);
			tb.ButtonInactiveBackgroundColor = Windows.UI.Color.FromArgb(1, 0, 0, 0);
		}
	}

	public void UpdateBitmap(SoftwareBitmap bitmap)
	{
		// Convert SoftwareBitmap directly into a WriteableBitmap to avoid encode/decode latency.
		// Convert always allocates a fresh bitmap, and the pixels are copied out into the
		// WriteableBitmap below, so the conversion is dead the moment the copy is done
		// (issue #229).
		using var converted = SoftwareBitmap.Convert(bitmap, BitmapPixelFormat.Bgra8, BitmapAlphaMode.Premultiplied);
		WriteableBitmap wb = new(converted.PixelWidth, converted.PixelHeight);
		converted.CopyToBuffer(wb.PixelBuffer);
		wb.Invalidate();
		EnsureElementRefs();
		if (_img is not null)
		{
			_img.Source = wb;
		}
		_bmpW = wb.PixelWidth;
		_bmpH = wb.PixelHeight;
	}

	public Task<Rect?> SelectRegionAsync()
	{
		_tcs = new TaskCompletionSource<Rect?>();
		_dragging = false;
		_lastPointerPos = null;
		// Read the pointer before anything below can move it. Showing the overlay, taking the
		// foreground and moving focus onto the canvas are three focus changes in a row, and a
		// tool that follows focus answers each of them by moving the pointer (issue #371).
		_cursorAnchor.Capture();
		// One watch serves the whole capture — the opening wiggle, the closing wiggle, and the
		// hold. Installed here, on the UI thread, because a low-level hook is delivered on the
		// thread that installed it and that thread needs a message loop. It is a system-wide
		// hook, so it is not installed when nothing would use it. Disposing the previous
		// capture's watch is a no-op in the normal course; it matters only when a capture died
		// without its own tidy-up.
		_mouseWatch?.Dispose();
		_mouseWatch = PointerNudge.Enabled || PointerHoldFor > TimeSpan.Zero
			? RealMouseInputWatch.Start()
			: null;
		ResetSelection();
		RememberForegroundWindow();
		InstallKeyboardHook();
		// Show and activate for input (in case window was hidden for reuse)
		try { this.AppWindow?.Show(); } catch { }
		this.Activate();
		bool focused = TryFocusOverlay();
		// Ensure TopMost without using SWP_SHOWWINDOW to avoid flicker
		int left = GetSystemMetrics(SM_XVIRTUALSCREEN);
		int top = GetSystemMetrics(SM_YVIRTUALSCREEN);
		int width = GetSystemMetrics(SM_CXVIRTUALSCREEN);
		int height = GetSystemMetrics(SM_CYVIRTUALSCREEN);
		SetWindowPos(_hwnd, HWND_TOPMOST, left, top, width, height, SWP_NOMOVE | SWP_NOSIZE);
		// Before ResetKeyboardSelection, which seeds the keyboard caret from wherever the
		// pointer is: put the pointer back first and the caret is seeded from the truth.
		RestoreCursorNowAndAfterFocusSettles();
		ResetKeyboardSelection();
		// The overlay is full-screen and topmost, so it has to say what it wants (issue
		// #215) — but the Canvas's AutomationProperties.HelpText already says it, and a
		// reader reads that out as focus lands here. Announcing the same thing as well raced
		// that: the focus-change announcement interrupted the notification part-way through
		// the sentence, so the instructions were routinely heard cut off (issue #265).
		//
		// The notification is kept for the one case the HelpText cannot cover: focus that
		// did not land on the overlay at all, where nothing would read it out.
		if (!focused)
			Announce(InstructionsAnnouncement, AutomationNotificationKind.Other);
		return _tcs.Task;
	}

	private void Overlay_PointerPressed(object sender, PointerRoutedEventArgs e)
	{
		if (_overlay is null) return;
		EnsureElementRefs();
		// Right click cancels immediately
		var pp = e.GetCurrentPoint(_overlay);
		if (pp.Properties.IsRightButtonPressed)
		{
			CompleteSelection(null, CancelledAnnouncement, AutomationNotificationKind.ActionAborted);
			return;
		}
		// Only start selection on left button
		if (!pp.Properties.IsLeftButtonPressed)
		{
			return;
		}
		_start = pp.Position;
		_dragging = true;
		_lastPointerPos = _start;
		// A mouse drag supersedes any half-finished keyboard selection.
		_keyboard.Reset(_overlay.ActualWidth, _overlay.ActualHeight, _start.X, _start.Y);
		// Capture the pointer so we still get the release even if cursor leaves the window/screen edge
		try { _overlay.CapturePointer(e.Pointer); } catch { }
		if (_selection is not null)
		{
			_selection.Visibility = Visibility.Visible;
			Canvas.SetLeft(_selection, _start.X);
			Canvas.SetTop(_selection, _start.Y);
			_selection.Width = 0;
			_selection.Height = 0;
		}
		UpdateCrosshair(_start);
		EnsureCrossCursor();
	}

	private void Overlay_PointerMoved(object sender, PointerRoutedEventArgs e)
	{
		if (_overlay is null) return;
		var pos = e.GetCurrentPoint(_overlay).Position;
		_lastPointerPos = pos;
		UpdateCrosshair(pos);
		// Keep the keyboard caret under the pointer so switching to the keys mid-drag
		// picks up where the mouse left off.
		//
		// Not while the overlay is wiggling the pointer itself, though (issue #375). A wiggle
		// arrives here as an ordinary pointer move, and syncing to it would drag the caret away
		// from wherever the keys had put it — so pressing Control plus A to take the whole
		// screen, and then Enter, would capture from the pinned top-left corner to wherever the
		// mouse happened to be sitting, which is not the region the overlay just announced. The
		// crosshair still follows, because the wiggle is meant to be visible and ends where it
		// started anyway.
		if (!_nudgeOwnsPointer)
			_keyboard.SyncCaret(pos.X, pos.Y);
		if (!_dragging) { return; }
		double x = Math.Min(pos.X, _start.X);
		double y = Math.Min(pos.Y, _start.Y);
		double w = Math.Abs(pos.X - _start.X);
		double h = Math.Abs(pos.Y - _start.Y);
		if (_selection is not null)
		{
			Canvas.SetLeft(_selection, x);
			Canvas.SetTop(_selection, y);
			_selection.Width = w;
			_selection.Height = h;
		}
	}

	private void Overlay_PointerReleased(object sender, PointerRoutedEventArgs e)
	{
		if (_overlay is null) return;
		// release pointer capture if any
		try { _overlay.ReleasePointerCaptures(); } catch { }
		// Right click cancels
		if (e.GetCurrentPoint(_overlay).Properties.IsRightButtonPressed)
		{
			CompleteSelection(null, CancelledAnnouncement, AutomationNotificationKind.ActionAborted);
			return;
		}
		if (!_dragging) return;
		FinishSelection(e.GetCurrentPoint(_overlay).Position);
	}
    
	// Handle cases where the system cancels the pointer (e.g., edge conditions) or capture is lost
	private void Overlay_PointerCanceled(object sender, PointerRoutedEventArgs e)
	{
		if (_overlay is null) return;
		try { _overlay.ReleasePointerCaptures(); } catch { }
		if (!_dragging) return;
		// Fall back to last known pointer position if current point unavailable
		var pos = _lastPointerPos ?? (_overlay is not null ? new Point(_overlay.ActualWidth / 2.0, _overlay.ActualHeight / 2.0) : new Point(0, 0));
		FinishSelection(pos);
	}

	private void Overlay_PointerCaptureLost(object sender, PointerRoutedEventArgs e)
	{
		if (_overlay is null) return;
		try { _overlay.ReleasePointerCaptures(); } catch { }
		if (!_dragging) return;
		var pos = _lastPointerPos ?? (_overlay is not null ? new Point(_overlay.ActualWidth / 2.0, _overlay.ActualHeight / 2.0) : new Point(0, 0));
		FinishSelection(pos);
	}

	private void FinishSelection(Point pos)
	{
		_dragging = false;
		if (_overlay is null) return;
		double x = Math.Min(pos.X, _start.X);
		double y = Math.Min(pos.Y, _start.Y);
		double w = Math.Abs(pos.X - _start.X);
		double h = Math.Abs(pos.Y - _start.Y);
		// Map selection (Overlay coords in DIP) to bitmap pixel coords for accurate crop
		double scaleX = _bmpW / Math.Max(1.0, _overlay.ActualWidth);
		double scaleY = _bmpH / Math.Max(1.0, _overlay.ActualHeight);

		int ix = (int)Math.Round(x * scaleX);
		int iy = (int)Math.Round(y * scaleY);
		int iw = (int)Math.Round(w * scaleX);
		int ih = (int)Math.Round(h * scaleY);

		// Clamp to bitmap bounds
		int x1 = Math.Max(0, Math.Min(ix, _bmpW));
		int y1 = Math.Max(0, Math.Min(iy, _bmpH));
		int x2 = Math.Max(0, Math.Min(ix + iw, _bmpW));
		int y2 = Math.Max(0, Math.Min(iy + ih, _bmpH));

		Rect rectPx = new(
			x1,
			y1,
			Math.Max(0, x2 - x1),
			Math.Max(0, y2 - y1)
		);
		CompleteSelection(
			rectPx,
			$"Region captured, {Math.Round(rectPx.Width)} by {Math.Round(rectPx.Height)} pixels.",
			AutomationNotificationKind.ActionCompleted);
	}

	/// <summary>
	/// Announces the outcome, then hides the overlay and reports the result on a later
	/// dispatcher turn.
	/// <para>
	/// The order is the point. These two announcements — the capture and the cancel — are
	/// the ones issue #215 asked for, and they were the least likely of any to be heard:
	/// raised, and then in the same turn the window was hidden and another application put
	/// back in front. A UIA notification reaches a screen reader asynchronously, and readers
	/// commonly discard events whose source window is gone by the time they arrive
	/// (issue #265). Hiding a turn later leaves the overlay visible and foreground while the
	/// event is delivered.
	/// </para>
	/// <para>
	/// The result is set last, after the restore, and that matters on its own: a
	/// <see cref="TaskCompletionSource{T}"/> may run its continuation inline, so setting it
	/// first let the caller's next step — clipboard, OCR, a paste — begin while this
	/// full-screen topmost window was still in front.
	/// </para>
	/// </summary>
	private void CompleteSelection(Rect? result, string announcement, AutomationNotificationKind kind)
	{
		_dragging = false;
		// Before the read below, so it reads the pointer and not a pixel the opening wiggle
		// happens to be parked on (issue #375).
		SettleAnyNudgeInProgress();
		// Where the pointer is at the instant the selection ends — for a mouse drag, exactly
		// where the button came up. Read here, while the overlay is still up, because hiding it
		// and handing the foreground back are the next two focus changes that can move the
		// pointer (issue #371).
		_cursorAnchor.Capture();
		Announce(announcement, kind);
		ResetSelection();

		void Finish()
		{
			HideAndRestore();
			// The overlay is gone; put the pointer back on the screen the user is looking at
			// again. HideAndRestore has already installed the handback this waits on.
			_cursorAnchor.Restore();
			RestoreCursorAfterForegroundHandback();
			_tcs?.TrySetResult(result);
		}

		// Falling straight through when there is no queue, or it refuses the work, is what
		// keeps a capture from hanging for ever on a lost announcement.
		if (_dispatcherQueue is null || !_dispatcherQueue.TryEnqueue(DispatcherQueuePriority.Low, Finish))
			Finish();
	}

	private void InitializeCrosshairAtCursor(System.Drawing.Rectangle bounds)
	{
		EnsureElementRefs();
		if (_overlay is null || _crosshairV is null || _crosshairH is null) return;
		// Determine DPI scale for mapping screen pixels to DIPs
		double scale = 1.0;
		try { var dpi = GetDpiForWindow(_hwnd); if (dpi > 0) scale = dpi / 96.0; } catch { }
		// Compute overlay size in DIPs
		double overlayW = _overlay.ActualWidth > 0 ? _overlay.ActualWidth : bounds.Width / scale;
		double overlayH = _overlay.ActualHeight > 0 ? _overlay.ActualHeight : bounds.Height / scale;
		_crosshairV.Height = overlayH;
		_crosshairH.Width = overlayW;
		// Map current cursor (screen px) to overlay DIPs
		double cx, cy;
		if (_cursor.TryGet(out var p))
		{
			cx = (p.X - bounds.Left) / scale;
			cy = (p.Y - bounds.Top) / scale;
		}
		else
		{
			cx = overlayW / 2.0;
			cy = overlayH / 2.0;
		}
		Canvas.SetLeft(_crosshairV, Math.Round(cx));
		Canvas.SetTop(_crosshairV, 0);
		Canvas.SetTop(_crosshairH, Math.Round(cy));
		Canvas.SetLeft(_crosshairH, 0);
	}

	private void Window_KeyDown(object sender, KeyRoutedEventArgs e)
	{
		if (e.Key == Windows.System.VirtualKey.Escape)
		{
			CompleteSelection(null, CancelledAnnouncement, AutomationNotificationKind.ActionAborted);
			e.Handled = true;
			return;
		}

		var key = MapKey(e.Key);
		if (key == RegionSelectionKey.None)
			return;

		bool shift = IsModifierDown(Windows.System.VirtualKey.Shift);
		bool control = IsModifierDown(Windows.System.VirtualKey.Control);

		var result = _keyboard.HandleKey(key, shift, control);
		if (result.Outcome == RegionKeyOutcome.Ignored)
			return;

		e.Handled = true;

		if (result.Outcome == RegionKeyOutcome.Committed)
		{
			// FinishSelection announces the commit, for the mouse path too.
			CommitKeyboardSelection();
			return;
		}

		RenderKeyboardSelection();
		Announce(result.Announcement, AutomationNotificationKind.Other);
	}

	private static RegionSelectionKey MapKey(Windows.System.VirtualKey key) => key switch
	{
		Windows.System.VirtualKey.Left => RegionSelectionKey.Left,
		Windows.System.VirtualKey.Right => RegionSelectionKey.Right,
		Windows.System.VirtualKey.Up => RegionSelectionKey.Up,
		Windows.System.VirtualKey.Down => RegionSelectionKey.Down,
		Windows.System.VirtualKey.Home => RegionSelectionKey.Home,
		Windows.System.VirtualKey.End => RegionSelectionKey.End,
		Windows.System.VirtualKey.PageUp => RegionSelectionKey.PageUp,
		Windows.System.VirtualKey.PageDown => RegionSelectionKey.PageDown,
		Windows.System.VirtualKey.Enter => RegionSelectionKey.Commit,
		Windows.System.VirtualKey.Space => RegionSelectionKey.Commit,
		Windows.System.VirtualKey.Back => RegionSelectionKey.Back,
		Windows.System.VirtualKey.A => RegionSelectionKey.SelectAll,
		_ => RegionSelectionKey.None
	};

	private static bool IsModifierDown(Windows.System.VirtualKey key)
	{
		try
		{
			var state = Microsoft.UI.Input.InputKeyboardSource.GetKeyStateForCurrentThread(key);
			return (state & Windows.UI.Core.CoreVirtualKeyStates.Down) == Windows.UI.Core.CoreVirtualKeyStates.Down;
		}
		catch
		{
			return false;
		}
	}

	// Mirrors the keyboard model onto the same rectangle and crosshair the mouse path
	// draws, so the overlay stays usable with either input and readable over someone's
	// shoulder.
	private void RenderKeyboardSelection()
	{
		EnsureElementRefs();
		UpdateCrosshair(new Point(_keyboard.CaretX, _keyboard.CaretY));

		if (_selection is null)
			return;

		if (!_keyboard.HasAnchor)
		{
			_selection.Visibility = Visibility.Collapsed;
			return;
		}

		_selection.Visibility = Visibility.Visible;
		Canvas.SetLeft(_selection, _keyboard.SelectionLeft);
		Canvas.SetTop(_selection, _keyboard.SelectionTop);
		_selection.Width = _keyboard.SelectionWidth;
		_selection.Height = _keyboard.SelectionHeight;
	}

	private void CommitKeyboardSelection()
	{
		// FinishSelection maps overlay DIPs to bitmap pixels and clamps; feed it the
		// anchor as the drag origin so both input paths crop identically.
		_start = new Point(_keyboard.AnchorX, _keyboard.AnchorY);
		FinishSelection(new Point(_keyboard.CaretX, _keyboard.CaretY));
	}

	private void ResetKeyboardSelection()
	{
		EnsureElementRefs();
		double width = _overlay?.ActualWidth ?? 0;
		double height = _overlay?.ActualHeight ?? 0;
		// Start where the pointer already is so mouse and keyboard agree, and so the
		// caret matches the crosshair the mouse path draws on open.
		var caret = _lastPointerPos ?? CursorInOverlayCoordinates(width, height);
		// Everything the selector says is scaled to these, so it reads out the same unit
		// FinishSelection announces the capture in (issue #265).
		_keyboard.SetBitmapSize(_bmpW, _bmpH);
		_keyboard.Reset(width, height, caret.X, caret.Y);
	}

	private Point CursorInOverlayCoordinates(double overlayWidth, double overlayHeight)
	{
		var fallback = new Point(overlayWidth / 2.0, overlayHeight / 2.0);
		try
		{
			double scale = 1.0;
			var dpi = GetDpiForWindow(_hwnd);
			if (dpi > 0)
				scale = dpi / 96.0;

			if (!_cursor.TryGet(out var p))
				return fallback;

			int left = GetSystemMetrics(SM_XVIRTUALSCREEN);
			int top = GetSystemMetrics(SM_YVIRTUALSCREEN);
			return new Point((p.X - left) / scale, (p.Y - top) / scale);
		}
		catch
		{
			return fallback;
		}
	}

	/// <summary>
	/// Puts the pointer back where the anchor says it was, twice: once now, and once on the next
	/// low-priority dispatcher turn.
	/// <para>
	/// The second attempt is the one that usually matters. Activating a window and moving focus
	/// return long before anyone else has reacted to them, and a magnifier or reader that follows
	/// focus moves the pointer from its own message loop a moment later. Restoring only inline
	/// would put the pointer back before the thing that moves it had even run.
	/// </para>
	/// <para>
	/// Two direct restores and no more. Anything later than these is the wiggle's business: it
	/// runs for seconds, and with the watch to tell a hand from a grab it reclaims the pointer
	/// from a magnifier that takes it mid-run (issue #382) — so the late grab this pair of
	/// restores cannot reach is covered without a third one.
	/// </para>
	/// <para>
	/// The optional wiggle goes on the same dispatcher turn, and for the same reason the second
	/// restore is there: holding the pointer perfectly still through the overlay's focus change
	/// keeps the pointer right, but gives a magnifier no reason to look at it, and ZoomText
	/// answers the focus change by swinging its view to the top-left corner (issue #375).
	/// </para>
	/// </summary>
	private void RestoreCursorNowAndAfterFocusSettles()
	{
		_cursorAnchor.Restore();

		if (_dispatcherQueue is null)
			return;

		// Read now, not inside the callback: this identifies the anchor being defended, so a
		// turn that runs late cannot act on behalf of a capture that has already finished.
		int generation = _cursorAnchor.Generation;
		var nudge = PointerNudge;

		_dispatcherQueue.TryEnqueue(DispatcherQueuePriority.Low, () =>
		{
			var allowed = CursorRestorePolicy.Decide(_dragging, _keyboard.HasAnchor);
			if (allowed == DeferredCursorRestore.StandDown)
				return;

			if (_cursorAnchor.RestoreIfCurrent(generation) && allowed == DeferredCursorRestore.MoveAndReseed)
			{
				// The pointer really had been moved, so the crosshair and the keyboard caret
				// were seeded from a position it is no longer at. Seed them again, from the live
				// pointer rather than from _lastPointerPos, which may itself be the jumped
				// position that arrived as a pointer-move event.
				_lastPointerPos = null;
				ResetKeyboardSelection();
				RenderKeyboardSelection();
			}

			// Whether or not the pointer had drifted. The magnified view can be pulled away by
			// the focus change on its own, with the pointer never moving at all — which is the
			// case that has nothing else to fix it.
			StartPointerNudge(generation, nudge);
		});
	}

	/// <summary>
	/// Puts the pointer back once more after the window that was in front before the overlay
	/// opened has the keyboard again, then stops defending the position.
	/// <para>
	/// Handing the foreground back is itself a focus change, and it is not instant: the ladder in
	/// <see cref="HideAndRestore"/> retries on a later dispatcher turn and then, if that fails
	/// too, waits <see cref="FocusRetryDelayMilliseconds"/> before a last attempt. So this can
	/// land a good hundred milliseconds after the button came up, and someone who released the
	/// drag and immediately moved the mouse elsewhere will feel it tug back. That is the trade
	/// the story asks for — a pointer that does not jump is worth more here than one that never
	/// argues — but it is a real cost, not a free win.
	/// </para>
	/// <para>
	/// The work runs off the UI thread, which is fine: setting the pointer position is not tied
	/// to a thread. It is stamped with the anchor it was registered against, so a handback that
	/// somehow outlives its own capture cannot touch the next one's pointer.
	/// </para>
	/// <para>
	/// Nothing waits for this. Deliberately: a cancelled capture waits for
	/// <see cref="ForegroundHandedBack"/> before it says anything, and the nudge that may follow
	/// runs for up to five seconds. Adding that to the wait would leave the user in silence for
	/// the whole of it.
	/// </para>
	/// </summary>
	private void RestoreCursorAfterForegroundHandback()
	{
		// The watch was installed when the capture opened; the same one serves to the end. Taken
		// into a local here so the async work below keeps operating on — and finally disposes —
		// this capture's watch even if a newer capture has replaced the field by the time it
		// finishes.
		var watch = _mouseWatch;
		_mouseWatch = null;
		_ = HandPointerBackAsync(_cursorAnchor.Generation, PointerNudge, PointerHoldFor, watch);
	}

	private async Task HandPointerBackAsync(
		int generation,
		PointerNudgeOptions nudge,
		TimeSpan hold,
		RealMouseInputWatch? watch)
	{
		try
		{
			await ForegroundHandedBack.ConfigureAwait(false);
			_cursorAnchor.RestoreIfCurrent(generation);

			// Snapshots taken before the wiggle so the reconciliation below can tell what
			// happened while it ran.
			long stepsBeforeNudge = watch?.HandSteps ?? 0;
			long grabsBeforeNudge = watch?.Teleports ?? 0;
			await NudgePointerAsync(generation, nudge, watch).ConfigureAwait(false);

			// If the hand moved during the wiggle, the run left the pointer exactly where the
			// hand put it — and the anchor still points at the old place. The hold that starts
			// next would read that difference as a grab and snap the pointer back out from under
			// the resting hand, because its own counter snapshot is taken after the movement
			// already happened. So the anchor is brought to the pointer first: the hold then
			// defends where the hand actually is. Not when a teleport also landed in that
			// stretch, though — the pointer may then be standing where a grab put it, and
			// rebasing would defend the grab; the anchor is left alone and the hold's first tick
			// undoes it. And when nothing moved at all, the anchor is left alone for the same
			// reason (issues #382 and #384).
			if (watch is { IsWatching: true }
				&& watch.HandSteps != stepsBeforeNudge
				&& watch.Teleports == grabsBeforeNudge
				&& _cursor.TryGet(out var handLeftItAt))
			{
				_cursorAnchor.RebaseIfCurrent(generation, handLeftItAt);
			}

			await HoldPointerAsync(generation, nudge, hold, watch).ConfigureAwait(false);
		}
		catch
		{
			// The pointer is a nicety. Nothing here may break a capture that has already
			// produced its picture.
		}
		finally
		{
			// The capture is over and the pointer belongs to the user again.
			_cursorAnchor.ClearIfCurrent(generation);
			DisposeOnUiThread(watch);
		}
	}

	/// <summary>
	/// Keeps the pointer where the capture — or the user's own hand — last put it, for a short
	/// while, undoing anything else that moves it (issues #379 and #382).
	/// <para>
	/// The two restores above both land before anything has reacted to the foreground changing. A
	/// magnifier that follows the keyboard caret moves the pointer to it later than that, so
	/// without this the pointer arrives back where the user left it and is then taken away to a
	/// text box they were never pointing at — every time, not occasionally.
	/// </para>
	/// <para>
	/// The hand is never fought: its movement re-baselines the defended position instead of
	/// ending the defense, buttons end it outright, and only movement with no hand behind it is
	/// undone. The rules live in <see cref="PointerHold"/>; the watch supplies the counts.
	/// </para>
	/// </summary>
	private Task HoldPointerAsync(
		int generation,
		PointerNudgeOptions nudge,
		TimeSpan hold,
		RealMouseInputWatch? watch)
	{
		if (hold <= TimeSpan.Zero)
			return Task.CompletedTask;

		// No hook, no hold. Without the signal, "no hand moved the pointer" would be an
		// assumption rather than an observation, and acting on it would mean dragging the pointer
		// back from under a hand for the length of the hold — the very thing this is built to
		// avoid. A capture then behaves as it did before the hold existed, which is a real
		// answer; guessing is not.
		if (watch is null || !watch.IsWatching)
			return Task.CompletedTask;

		if (!_cursorAnchor.HasAnchor || _cursorAnchor.Generation != generation)
			return Task.CompletedTask;

		var since = System.Diagnostics.Stopwatch.StartNew();

		return PointerHold.RunAsync(
			cursor: _cursor,
			baseline: _cursorAnchor.Anchor,
			handActs: () => watch.HandActs,
			handSteps: () => watch.HandSteps,
			teleports: () => watch.Teleports,
			stillCurrent: () => _cursorAnchor.Generation == generation,
			// Keep the anchor with the hand, so the wiggle below — and the settle, if anything
			// interrupts it — aim at where the hand actually is, not where the capture ended.
			followedTo: position => _cursorAnchor.RebaseIfCurrent(generation, position),
			// Whatever grabbed the pointer probably took the magnified view with it, so bring
			// the view back to where the pointer has been put.
			afterRestore: () => NudgePointerAsync(generation, nudge, watch),
			delay: Task.Delay,
			elapsed: () => since.Elapsed,
			pollInterval: PointerHoldPollInterval,
			hold: hold);
	}

	/// <summary>
	/// Removes the hook on the thread that installed it, as Windows requires. Falls back to
	/// removing it here if there is no queue to get back onto — worse than nothing would be
	/// leaving a system-wide hook installed for ever.
	/// </summary>
	private void DisposeOnUiThread(RealMouseInputWatch? watch)
	{
		if (watch is null)
			return;

		if (_dispatcherQueue is null || !_dispatcherQueue.TryEnqueue(watch.Dispose))
			watch.Dispose();
	}

	/// <summary>
	/// Starts a wiggle and does not wait for it. Used at the opening end, from a dispatcher
	/// callback that must not be held up for the length of the wiggle.
	/// </summary>
	private void StartPointerNudge(int generation, PointerNudgeOptions nudge)
	{
		_ = SafeNudgePointerAsync(generation, nudge, _mouseWatch);
	}

	private async Task SafeNudgePointerAsync(int generation, PointerNudgeOptions nudge, RealMouseInputWatch? watch)
	{
		try
		{
			await NudgePointerAsync(generation, nudge, watch).ConfigureAwait(false);
		}
		catch
		{
			// A wiggle that fails is a magnified view left where it was. It is not a reason to
			// take a capture down with it.
		}
	}

	/// <summary>
	/// Moves the pointer one pixel back and forth for a while, so a magnifier that has swung its
	/// view somewhere else brings it back to the mouse (issues #373 and #375).
	/// <para>
	/// Runs at both ends of a capture, for the same reason each time. The overlay opening and the
	/// overlay closing are both focus changes, and ZoomText answers a focus change by following
	/// whatever it thinks is interesting — the top-left corner as the overlay appears, a flashing
	/// caret in the application underneath as it goes away. The pointer is held perfectly still
	/// across both, which is right for the pointer and is exactly why the magnifier has no reason
	/// to look at it.
	/// </para>
	/// <para>
	/// Runs after the restore, not instead of it, and finishes on the very pixel the restore put
	/// the pointer on. The order matters: wiggling around a position that had drifted would
	/// advertise the wrong place.
	/// </para>
	/// </summary>
	private async Task NudgePointerAsync(int generation, PointerNudgeOptions nudge, RealMouseInputWatch? watch)
	{
		if (!nudge.Enabled || _cursorAnchor.Generation != generation || !_cursorAnchor.HasAnchor)
			return;

		var anchor = _cursorAnchor.Anchor;
		var plan = PointerNudgePlanner.Plan(anchor, nudge);

		_nudgeOwnsPointer = true;
		try
		{
			await PointerNudgeRunner.RunAsync(
				_nudgeCursor,
				Task.Delay,
				anchor,
				plan,
				TimeSpan.FromMilliseconds(nudge.IntervalMilliseconds),
				() => NudgeVerdict(generation),
				// The watch is what lets the run tell a grab from the hand and reclaim the
				// pointer instead of dying on a one-pixel drift (issues #382 and #384). Without
				// one — not asked for, or the hook refused to install — the run keeps the old
				// rule and stops on any foreign move.
				watch is { IsWatching: true } ? () => watch.HandSteps : null,
				watch is { IsWatching: true } ? () => watch.Teleports : null).ConfigureAwait(false);
		}
		finally
		{
			_nudgeOwnsPointer = false;
		}
	}

	/// <summary>
	/// Whether a wiggle in progress may carry on, and what to do with the pointer if not.
	/// <para>
	/// A drag in flight is the one case that ends the wiggle where it stands. The pointer is
	/// under the user's hand, the rectangle they are drawing starts from it, and putting the
	/// pointer back on the anchor would move the edge of their selection by a pixel just as they
	/// pressed the button. Only the opening wiggle can meet a drag; the closing one runs after
	/// the button is already up.
	/// </para>
	/// <para>
	/// Otherwise the wiggle stops when it is no longer this capture's to run — a new capture has
	/// claimed the anchor, or something on the UI thread has already taken the pointer back —
	/// and tidies up after itself so it never leaves the pointer a pixel out.
	/// </para>
	/// </summary>
	private PointerNudgeVerdict NudgeVerdict(int generation)
	{
		if (_dragging)
			return PointerNudgeVerdict.StopAndLeave;

		if (!_nudgeOwnsPointer || _cursorAnchor.Generation != generation)
			return PointerNudgeVerdict.StopAndSettle;

		return PointerNudgeVerdict.Continue;
	}

	/// <summary>
	/// Ends any wiggle in progress and puts the pointer back on the anchor, now, on the calling
	/// thread. Called before anything re-reads the live pointer to anchor on it.
	/// <para>
	/// A wiggle spends half its life a pixel off the anchor. Without this, finishing a capture
	/// from the keyboard or with Escape while one was running would read that pixel as the
	/// pointer's real position and anchor the rest of the capture on it — and because bringing
	/// the overlay forward again re-anchors too, pressing the hotkey repeatedly would walk the
	/// pointer steadily to the right, a pixel a press. That is the drift both the pointer-stays-
	/// put story (#371) and the wiggle's own plan exist to prevent.
	/// </para>
	/// <para>
	/// Only a displacement shaped like the wiggle's own is undone. A pointer the user has moved
	/// themselves is left exactly where they put it, which is what keeps this from firing on the
	/// mouse path, where the release position is genuinely theirs.
	/// </para>
	/// </summary>
	private void SettleAnyNudgeInProgress()
	{
		if (!_nudgeOwnsPointer)
			return;

		// Stops the running wiggle on its next tick; it then finds the pointer already home and
		// writes nothing.
		_nudgeOwnsPointer = false;

		if (!_cursorAnchor.HasAnchor)
			return;

		var anchor = _cursorAnchor.Anchor;
		if (!_cursor.TryGet(out var current))
			return;

		if (current != anchor && PointerNudgePlanner.IsWiggleDisplacement(anchor, current, PointerNudge.DistancePixels))
			_cursor.TrySet(anchor);
	}

	private void Announce(string message, AutomationNotificationKind kind)
	{
		if (string.IsNullOrWhiteSpace(message))
			return;

		try
		{
			EnsureElementRefs();
			if (_overlay is null)
				return;

			// CreatePeerForElement rather than FromElement so the very first
			// announcement fires even before a peer exists.
			var peer = FrameworkElementAutomationPeer.CreatePeerForElement(_overlay);
			peer?.RaiseNotificationEvent(
				kind,
				AutomationNotificationProcessing.MostRecent,
				message,
				AnnouncementActivityId);
		}
		catch
		{
			// An announcement that cannot be raised must not break the capture.
		}
	}

	private void Overlay_SizeChanged(object sender, SizeChangedEventArgs e)
	{
		// Keep crosshair stretched to current size and at last pointer position (or center)
		var pos = _lastPointerPos ?? new Point(e.NewSize.Width / 2.0, e.NewSize.Height / 2.0);
		UpdateCrosshair(pos);
		_keyboard.Resize(e.NewSize.Width, e.NewSize.Height);
	}

	private void UpdateCrosshair(Point pos)
	{
		EnsureElementRefs();
		if (_crosshairV is null || _crosshairH is null || _overlay is null) return;
		double w = _overlay.ActualWidth;
		double h = _overlay.ActualHeight;
		if (double.IsNaN(w) || double.IsNaN(h) || w <= 0 || h <= 0) return;

		_crosshairV.Height = h;
		Canvas.SetLeft(_crosshairV, Math.Round(pos.X));
		Canvas.SetTop(_crosshairV, 0);

		_crosshairH.Width = w;
		Canvas.SetTop(_crosshairH, Math.Round(pos.Y));
		Canvas.SetLeft(_crosshairH, 0);
	}

	private void EnsureElementRefs()
	{
		if (_img != null && _overlay != null && _selection != null && _crosshairV != null && _crosshairH != null)
			return;
		if (this.Content is not FrameworkElement root)
			return;
		_img ??= root.FindName("Img") as Microsoft.UI.Xaml.Controls.Image;
		_overlay ??= root.FindName("Overlay") as Microsoft.UI.Xaml.Controls.Canvas;
		_selection ??= root.FindName("Selection") as Microsoft.UI.Xaml.Shapes.Rectangle;
		_crosshairV ??= root.FindName("CrosshairV") as Microsoft.UI.Xaml.Shapes.Rectangle;
		_crosshairH ??= root.FindName("CrosshairH") as Microsoft.UI.Xaml.Shapes.Rectangle;
	}

	private void EnsureCrossCursor()
	{
		try
		{
			if (CURSOR_CROSS != IntPtr.Zero) SetCursor(CURSOR_CROSS);
		}
		catch { }
	}

	private void Overlay_PointerEntered(object sender, PointerRoutedEventArgs e)
	{
		EnsureElementRefs();
		EnsureCrossCursor();
	}

	private void Overlay_PointerExited(object sender, PointerRoutedEventArgs e)
	{
		try
		{
			if (CURSOR_ARROW != IntPtr.Zero) SetCursor(CURSOR_ARROW);
		}
		catch { }
	}

	public void PrepareWindowForReuse()
	{
		PrepareWindow();
		HideAndRestore();
	}

	private void Overlay_Loaded(object sender, RoutedEventArgs e)
	{
		if (_initialLayoutDone) return;
		_initialLayoutDone = true;
		
		int left = GetSystemMetrics(SM_XVIRTUALSCREEN);
		int top = GetSystemMetrics(SM_YVIRTUALSCREEN);
		int width = GetSystemMetrics(SM_CXVIRTUALSCREEN);
		int height = GetSystemMetrics(SM_CYVIRTUALSCREEN);
		var bounds = new System.Drawing.Rectangle(left, top, width, height);
		
		InitializeCrosshairAtCursor(bounds);
		ResetKeyboardSelection();
		TryFocusOverlay();
	}

	private void ResetSelection()
	{
		EnsureElementRefs();
		if (_selection is not null)
		{
			_selection.Visibility = Visibility.Collapsed;
			_selection.Width = 0;
			_selection.Height = 0;
			Canvas.SetLeft(_selection, 0);
			Canvas.SetTop(_selection, 0);
		}
	}

        /// <summary>
        /// True when focus landed on the overlay Canvas — which is what makes a reader read
        /// out its name and HelpText, and so what decides whether the instructions have to be
        /// announced separately (issue #265).
        /// </summary>
        private bool TryFocusOverlay()
        {
                try { return _overlay?.Focus(FocusState.Programmatic) == true; } catch { return false; }
        }

        private void RememberForegroundWindow()
        {
                try
                {
                        var current = GetForegroundWindow();
                        if (current != IntPtr.Zero && current != _hwnd)
                        {
                                _previousForeground = current;
                        }
                        else
                        {
                                _previousForeground = IntPtr.Zero;
                        }
                }
                catch
                {
                        _previousForeground = IntPtr.Zero;
                }
        }

	private bool TryRestoreForegroundWindow()
	{
		var target = _previousForeground;
		if (target == IntPtr.Zero || target == _hwnd)
		{
			_previousForeground = IntPtr.Zero;
			return false;
		}
		if (!IsWindow(target))
		{
			_previousForeground = IntPtr.Zero;
			return false;
		}
		bool success = false;
		try
		{
			uint targetThread = GetWindowThreadProcessId(target, out uint targetProcessId);
			uint currentThread = GetCurrentThreadId();
			bool attached = false;
			if (targetThread != 0 && targetThread != currentThread)
			{
				try { attached = AttachThreadInput(currentThread, targetThread, true); } catch { attached = false; }
			}
			try
			{
				if (targetProcessId != 0)
				{
					try { AllowSetForegroundWindow(targetProcessId); } catch { }
				}
				bool isIconic = false;
				try { isIconic = IsIconic(target); } catch { isIconic = false; }
				if (isIconic)
				{
					try { ShowWindow(target, SW_RESTORE); } catch { }
				}
				try { BringWindowToTop(target); } catch { }
				try { SetForegroundWindow(target); } catch { }
				try { SetActiveWindow(target); } catch { }
				try { SetFocus(target); } catch { }
			}
			finally
			{
				if (attached)
				{
					try { AttachThreadInput(currentThread, targetThread, false); } catch { }
				}
			}
			success = GetForegroundWindow() == target;
		}
		catch
		{
			success = false;
		}
		if (success)
		{
			// Focus restoration completed, so we can stop tracking the prior foreground window.
			_previousForeground = IntPtr.Zero;
		}
		else if (!IsWindow(target) || !IsWindowVisible(target))
		{
			// The window is no longer valid; clear the handle to avoid repeated, futile retries.
			_previousForeground = IntPtr.Zero;
		}
		return success;
	}

	/// <summary>
	/// Waits for the window that was in front before this overlay opened to get the keyboard
	/// back, or for the attempt to be given up on. Already complete when there is nothing to
	/// wait for.
	/// <para>
	/// A cancelled capture is followed by an error message and, 50 ms later, by whatever
	/// shortcut the user configured to read it — and that shortcut lands wherever the keyboard
	/// is. The restore below runs a ladder of retries at that same 50 ms spacing, so the two
	/// used to race, and the shortcut could be typed into a capture overlay that had not
	/// finished standing down (issue #342). A successful OCR is never affected: it spends
	/// hundreds of milliseconds on the network first.
	/// </para>
	/// </summary>
	public Task ForegroundHandedBack => _foregroundHandback?.Task ?? Task.CompletedTask;

	private void HideAndRestore()
	{
		UninstallKeyboardHook();
		// Runs continuations asynchronously so a waiter cannot resume inline on this thread and
		// carry on with the rest of the capture in the middle of the handback.
		var handback = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
		_foregroundHandback = handback;

		try { this.AppWindow?.Hide(); } catch { }
		if (TryRestoreForegroundWindow() || _previousForeground == IntPtr.Zero)
		{
			handback.TrySetResult();
			return;
		}
		if (_dispatcherQueue is null)
		{
			handback.TrySetResult();
			return;
		}
		if (!_dispatcherQueue.TryEnqueue(DispatcherQueuePriority.Low, () =>
		{
			if (TryRestoreForegroundWindow() || _previousForeground == IntPtr.Zero)
			{
				handback.TrySetResult();
				return;
			}
			_ = Task.Run(async () =>
			{
				// True once the last rung owns the completion. Until then this method does,
				// because a waiter that is never released would hold up the message saying the
				// capture was cancelled — worse than releasing it a moment early.
				bool lastRungOwnsIt = false;
				try
				{
					await Task.Delay(FocusRetryDelayMilliseconds);
					if (this._dispatcherQueue is DispatcherQueue queue && this._previousForeground != IntPtr.Zero)
					{
						lastRungOwnsIt = queue.TryEnqueue(DispatcherQueuePriority.Low, () =>
						{
							try
							{
								if (!this.TryRestoreForegroundWindow())
								{
									this._previousForeground = IntPtr.Zero;
								}
							}
							finally
							{
								handback.TrySetResult();
							}
						});
					}
				}
				finally
				{
					if (!lastRungOwnsIt)
						handback.TrySetResult();
				}
			});
		}))
		{
			handback.TrySetResult();
		}
	}

	private void InstallKeyboardHook()
	{
		if (_keyboardHookHandle != IntPtr.Zero)
			return; // Already installed

		_keyboardHookProc = KeyboardHookCallback;
		using var curProcess = System.Diagnostics.Process.GetCurrentProcess();
		using var curModule = curProcess.MainModule;
		var moduleHandle = GetModuleHandle(curModule?.ModuleName);
		_keyboardHookHandle = SetWindowsHookEx(WH_KEYBOARD_LL, _keyboardHookProc, moduleHandle, 0);
	}

	private void UninstallKeyboardHook()
	{
		if (_keyboardHookHandle == IntPtr.Zero)
			return;

		UnhookWindowsHookEx(_keyboardHookHandle);
		_keyboardHookHandle = IntPtr.Zero;
		_keyboardHookProc = null;
	}

	private IntPtr KeyboardHookCallback(int nCode, IntPtr wParam, IntPtr lParam)
	{
		if (nCode >= 0 && wParam == (IntPtr)WM_KEYDOWN)
		{
			int vkCode = Marshal.ReadInt32(lParam);
			if (vkCode == VK_ESCAPE)
			{
				// Cancel the selection on Escape key. The hook can run ahead of the UI
				// thread's own message handling, so the work goes through the dispatcher;
				// CompleteSelection then takes its own turn to hide, so the cancel is
				// announced from a window that is still there to be announced from.
				_dragging = false;
				if (_dispatcherQueue is null || !_dispatcherQueue.TryEnqueue(
					() => CompleteSelection(null, CancelledAnnouncement, AutomationNotificationKind.ActionAborted)))
				{
					// Nothing will run the cancel on the queue, so it runs here — a
					// low-level hook is delivered on the thread that installed it, which is
					// this window's own. Merely releasing the caller instead would leave a
					// full-screen topmost overlay on screen and a global keyboard hook
					// installed, with the capture looking finished to everyone above.
					CompleteSelection(null, CancelledAnnouncement, AutomationNotificationKind.ActionAborted);
				}
				return (IntPtr)1; // Suppress the key
			}
		}
		return CallNextHookEx(_keyboardHookHandle, nCode, wParam, lParam);
	}
}
