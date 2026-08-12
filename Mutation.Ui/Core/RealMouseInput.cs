namespace Mutation.Ui.Core;

/// <summary>
/// Decides whether a mouse event means a hand is on the mouse, as opposed to a program moving the
/// pointer.
///
/// <para>
/// This is the rule the pointer hold rests on, so it lives here rather than inside the hook that
/// feeds it. The hook itself is a few lines of interop that cannot be built without installing a
/// real system-wide hook; the decision is arithmetic on two numbers and can be stated plainly and
/// checked. Everything that could be got wrong is here — which flags mean injected, and which
/// messages count whatever their flags.
/// </para>
///
/// <para>
/// Windows marks input it did not get from a device. A magnifier moving the pointer either
/// injects the movement, which arrives marked, or places the cursor, which produces no mouse
/// event at all. Mutation's own wiggle injects, so it is marked too and cannot be mistaken for
/// its user.
/// </para>
/// </summary>
public static class RealMouseInput
{
	/// <summary>LLMHF_INJECTED — the event did not come from a mouse.</summary>
	public const uint InjectedFlag = 0x00000001;

	/// <summary>
	/// LLMHF_LOWER_IL_INJECTED — injected by something running at a lower integrity level. Also
	/// not a hand on a mouse.
	/// </summary>
	public const uint LowerIntegrityInjectedFlag = 0x00000002;

	public const int MouseMove = 0x0200;
	public const int LeftButtonDown = 0x0201;
	public const int LeftButtonUp = 0x0202;
	public const int RightButtonDown = 0x0204;
	public const int RightButtonUp = 0x0205;
	public const int MiddleButtonDown = 0x0207;
	public const int MiddleButtonUp = 0x0208;
	public const int Wheel = 0x020A;
	public const int ExtraButtonDown = 0x020B;
	public const int ExtraButtonUp = 0x020C;
	public const int HorizontalWheel = 0x020E;

	/// <summary>
	/// Whether this event means the user has taken the mouse.
	/// <para>
	/// Any button or wheel counts, whatever it is marked as. Remote desktop and some KVM software
	/// deliver a real hand's movement marked as injected, and mistaking a user for a program is
	/// the expensive way round: it means an application dragging the pointer back out from under
	/// someone for a second and a half. A button is a deliberate act however it arrived.
	/// </para>
	/// <para>
	/// Movement is judged on the marks, which is what tells a magnifier — or Mutation's own
	/// wiggle — from a hand.
	/// </para>
	/// </summary>
	public static bool IsHandOnTheMouse(int message, uint flags)
	{
		if (IsButtonOrWheel(message))
			return true;

		if (message != MouseMove)
			return false;

		return (flags & (InjectedFlag | LowerIntegrityInjectedFlag)) == 0;
	}

	private static bool IsButtonOrWheel(int message) => message is
		LeftButtonDown or LeftButtonUp or
		RightButtonDown or RightButtonUp or
		MiddleButtonDown or MiddleButtonUp or
		ExtraButtonDown or ExtraButtonUp or
		Wheel or HorizontalWheel;
}
