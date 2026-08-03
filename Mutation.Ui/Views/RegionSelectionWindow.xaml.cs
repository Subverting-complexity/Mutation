using Microsoft.UI.Dispatching;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media.Imaging;
using Mutation.Ui.Core;
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

	// Keyboard path for the overlay, so the four screenshot/OCR hotkeys are completable
	// without sight of the crosshair (issue #215).
	private readonly KeyboardRegionSelector _keyboard = new();
	private const string AnnouncementActivityId = "RegionSelection";

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

	[StructLayout(LayoutKind.Sequential)]
	private struct POINT { public int X; public int Y; }
	[DllImport("user32.dll", SetLastError = false)]
	private static extern bool GetCursorPos(out POINT lpPoint);
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
		_hwnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
		_dispatcherQueue = DispatcherQueue.GetForCurrentThread();
		EnsureElementRefs();
	}

	public void BringToFront()
	{
		try
		{
			this.Activate();
			SetForegroundWindow(_hwnd);
			int left = GetSystemMetrics(SM_XVIRTUALSCREEN);
			int top = GetSystemMetrics(SM_YVIRTUALSCREEN);
			int width = GetSystemMetrics(SM_CXVIRTUALSCREEN);
			int height = GetSystemMetrics(SM_CYVIRTUALSCREEN);
			SetWindowPos(_hwnd, HWND_TOPMOST, left, top, width, height, SWP_NOMOVE | SWP_NOSIZE);
			TryFocusOverlay();
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
		// Convert SoftwareBitmap directly into a WriteableBitmap to avoid encode/decode latency
		var converted = SoftwareBitmap.Convert(bitmap, BitmapPixelFormat.Bgra8, BitmapAlphaMode.Premultiplied);
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
		ResetSelection();
		RememberForegroundWindow();
		InstallKeyboardHook();
		// Show and activate for input (in case window was hidden for reuse)
		try { this.AppWindow?.Show(); } catch { }
		this.Activate();
		TryFocusOverlay();
		// Ensure TopMost without using SWP_SHOWWINDOW to avoid flicker
		int left = GetSystemMetrics(SM_XVIRTUALSCREEN);
		int top = GetSystemMetrics(SM_YVIRTUALSCREEN);
		int width = GetSystemMetrics(SM_CXVIRTUALSCREEN);
		int height = GetSystemMetrics(SM_CYVIRTUALSCREEN);
		SetWindowPos(_hwnd, HWND_TOPMOST, left, top, width, height, SWP_NOMOVE | SWP_NOSIZE);
		ResetKeyboardSelection();
		// The overlay is full-screen and topmost: without this the reader just lands on
		// an unnamed canvas with no hint that anything is expected of it (issue #215).
		Announce(
			"Select screen region. Move with the arrow keys, press Enter to set the first corner, move, then Enter again to capture. Control plus A selects the whole screen. Escape cancels.",
			AutomationNotificationKind.Other);
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
			_dragging = false;
			_tcs?.TrySetResult(null);
			Announce("Region selection cancelled.", AutomationNotificationKind.ActionAborted);
			ResetSelection();
			HideAndRestore();
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
			_dragging = false;
			_tcs?.TrySetResult(null);
			Announce("Region selection cancelled.", AutomationNotificationKind.ActionAborted);
			ResetSelection();
			HideAndRestore();
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
		Announce(
			$"Region captured, {Math.Round(rectPx.Width)} by {Math.Round(rectPx.Height)} pixels.",
			AutomationNotificationKind.ActionCompleted);
		_tcs?.TrySetResult(rectPx);
		ResetSelection();
		HideAndRestore();
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
		if (GetCursorPos(out POINT p))
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
			_dragging = false;
			_tcs?.TrySetResult(null);
			Announce("Region selection cancelled.", AutomationNotificationKind.ActionAborted);
			HideAndRestore();
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

			if (!GetCursorPos(out POINT p))
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

        private void TryFocusOverlay()
        {
                try { _overlay?.Focus(FocusState.Programmatic); } catch { }
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

	private void HideAndRestore()
	{
		UninstallKeyboardHook();
		try { this.AppWindow?.Hide(); } catch { }
		if (TryRestoreForegroundWindow() || _previousForeground == IntPtr.Zero)
		{
			return;
		}
		if (_dispatcherQueue is null)
		{
			return;
		}
		_dispatcherQueue.TryEnqueue(DispatcherQueuePriority.Low, () =>
		{
			if (TryRestoreForegroundWindow() || _previousForeground == IntPtr.Zero)
			{
				return;
			}
			_ = Task.Run(async () =>
			{
				await Task.Delay(FocusRetryDelayMilliseconds);
				if (this._dispatcherQueue is DispatcherQueue queue)
				{
					if (this._previousForeground == IntPtr.Zero)
					{
						return;
					}
					queue.TryEnqueue(DispatcherQueuePriority.Low, () =>
					{
						if (!this.TryRestoreForegroundWindow())
						{
							this._previousForeground = IntPtr.Zero;
						}
					});
				}
			});
		});
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
				// Cancel the selection on Escape key
				_dragging = false;
				_tcs?.TrySetResult(null);
				// Use dispatcher to ensure UI operations happen on the correct thread
				_dispatcherQueue?.TryEnqueue(() =>
				{
					Announce("Region selection cancelled.", AutomationNotificationKind.ActionAborted);
					HideAndRestore();
				});
				return (IntPtr)1; // Suppress the key
			}
		}
		return CallNextHookEx(_keyboardHookHandle, nCode, wParam, lParam);
	}
}
