using System;

namespace Mutation.Ui.Core;

/// <summary>
/// Keeps the main window from being activated while a screen capture is on screen, by
/// disabling it for the life of the capture.
///
/// <para>
/// Issue #446. With focus in another application, a capture hotkey sometimes made a screen
/// reader read the control that had focus in the main window — "Playback speed 1.5" — before
/// the capture overlay, so the overlay's own name was cut off or not heard at all. The overlay
/// is a second window in the same process, and the main window became active for a moment
/// while it was being shown. A disabled window cannot take that moment: Windows skips it when
/// it chooses a window to activate, and it cannot have the keyboard. That is the same thing
/// that keeps the prompt editor's owner out of the way (issue #441).
/// </para>
///
/// <para>
/// Left alone when the main window is already the foreground window. That is a capture started
/// from the main window itself, with a button or a hotkey, and the user expects to go back
/// there when it ends. Disabling the active window would take the keyboard away from it with
/// nowhere for it to go, and the hand-back at the end would have to put it back on a disabled
/// window. Left alone too when something else has already disabled it, so this never enables
/// a window a modal dialog is still holding down.
/// </para>
///
/// <para>
/// Counted rather than on or off. The capture's release waits for the overlay to hand the
/// foreground back, which can take a few dispatcher turns, and the next capture may start
/// inside that wait. A plain flag would let the earlier release enable the main window in
/// the middle of the later capture. With a count, the window comes back only when the last
/// hold is released. The first hold decides whether this capture touches the window at all.
/// </para>
///
/// <para>
/// Not thread-safe. Every hold and release runs on the UI thread, which is also the thread
/// that owns the main window.
/// </para>
/// </summary>
public sealed class MainWindowActivationLock
{
	private readonly IMainWindowEnablement _window;
	private int _holds;
	private bool _disabledByThis;

	public MainWindowActivationLock(IMainWindowEnablement window)
	{
		_window = window ?? throw new ArgumentNullException(nameof(window));
	}

	/// <summary>Whether this lock has the main window disabled right now.</summary>
	public bool IsHoldingWindowDisabled => _disabledByThis;

	/// <summary>
	/// Takes a hold for one capture. Dispose the result to release it; disposing it more than
	/// once releases it once.
	/// </summary>
	public IDisposable Hold()
	{
		if (_holds == 0)
		{
			_disabledByThis = false;
			if (!_window.IsForeground && _window.IsEnabled)
			{
				_window.SetEnabled(false);
				_disabledByThis = true;
			}
		}
		_holds++;
		return new Release(this);
	}

	private void ReleaseOne()
	{
		if (_holds == 0)
			return;
		_holds--;
		if (_holds == 0 && _disabledByThis)
		{
			_disabledByThis = false;
			_window.SetEnabled(true);
		}
	}

	private sealed class Release : IDisposable
	{
		private MainWindowActivationLock? _owner;

		public Release(MainWindowActivationLock owner) => _owner = owner;

		public void Dispose()
		{
			var owner = _owner;
			_owner = null;
			owner?.ReleaseOne();
		}
	}
}
