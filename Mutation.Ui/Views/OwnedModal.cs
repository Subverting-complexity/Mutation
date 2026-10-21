using System;
using System.Runtime.InteropServices;

namespace Mutation.Ui.Views;

/// <summary>
/// Makes a separate top-level window modal to another, the way a Win32 dialog is: the
/// dialog is owned by the main window, and the main window is disabled until the dialog
/// closes. All P/Invoke, no rules.
/// <para>
/// Owning keeps the dialog above the main window without the always-on-top flag the prompt
/// editor used before, which also floated it above every other application — including the
/// one the user had just switched to so they could copy text for a test run. Disabling the
/// owner is what stops a click, a Tab or Alt+Tab from reaching the main window behind it
/// (issue #441).
/// </para>
/// <para>
/// <c>OverlappedPresenter.IsModal</c> is not used: it throws unless the owner is already set,
/// which crashed the editor once, and on its own it does not say whether the owner is
/// disabled. The two calls here are the whole of what a Win32 modal is.
/// </para>
/// </summary>
internal sealed class OwnedModal
{
	private const int GWLP_HWNDPARENT = -8;

	[DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW", SetLastError = true)]
	private static extern IntPtr SetWindowLongPtr64(IntPtr hWnd, int nIndex, IntPtr dwNewLong);

	// 32-bit user32 has no SetWindowLongPtrW export; there the plain call takes a pointer-size value.
	[DllImport("user32.dll", EntryPoint = "SetWindowLongW", SetLastError = true)]
	private static extern int SetWindowLong32(IntPtr hWnd, int nIndex, int dwNewLong);

	private static void SetOwner(IntPtr dialog, IntPtr owner)
	{
		if (IntPtr.Size == 8)
			SetWindowLongPtr64(dialog, GWLP_HWNDPARENT, owner);
		else
			SetWindowLong32(dialog, GWLP_HWNDPARENT, owner.ToInt32());
	}

	[DllImport("user32.dll")]
	[return: MarshalAs(UnmanagedType.Bool)]
	private static extern bool EnableWindow(IntPtr hWnd, [MarshalAs(UnmanagedType.Bool)] bool bEnable);

	private readonly IntPtr _owner;
	private bool _ownerDisabled;

	private OwnedModal(IntPtr owner) => _owner = owner;

	/// <summary>
	/// Sets <paramref name="owner"/> as the owner of <paramref name="dialog"/> and disables it.
	/// Call <see cref="Release"/> as the dialog closes.
	/// </summary>
	public static OwnedModal Attach(IntPtr dialog, IntPtr owner)
	{
		var modal = new OwnedModal(owner);
		SetOwner(dialog, owner);
		EnableWindow(owner, false);
		modal._ownerDisabled = true;
		return modal;
	}

	/// <summary>
	/// Enables the owner again. Safe to call more than once.
	/// <para>
	/// Has to run while the dialog still exists — from its <c>Closed</c> event, not after.
	/// Windows hands activation to the next enabled window when the dialog goes, and if the
	/// owner is still disabled at that moment it is skipped: focus lands in whatever
	/// application was behind, and a screen-reader user is left somewhere they did not choose.
	/// </para>
	/// </summary>
	public void Release()
	{
		if (!_ownerDisabled)
			return;
		_ownerDisabled = false;
		EnableWindow(_owner, true);
	}
}
