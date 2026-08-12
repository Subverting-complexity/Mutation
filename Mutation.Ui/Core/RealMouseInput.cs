using System;

namespace Mutation.Ui.Core;

/// <summary>What one low-level mouse event says about who is driving the mouse.</summary>
public enum RealMouseEventKind
{
	/// <summary>Not mouse use at all — a message that is neither movement nor a button.</summary>
	Nothing,

	/// <summary>
	/// A deliberate act: a button or the wheel. Ends any argument about who the pointer belongs
	/// to, whatever the event is flagged as.
	/// </summary>
	HandAct,

	/// <summary>
	/// One small step of unflagged movement — the size a hand actually produces, whether it is
	/// travelling somewhere or just resting on the mouse.
	/// </summary>
	HandStep,

	/// <summary>
	/// Unflagged movement too large for a hand to have made in one event. A driver moving the
	/// pointer produces exactly this: ZoomText's pointer routing arrives as a single unflagged
	/// event tens of pixels long, measured on the machine this matters on (issue #382).
	/// </summary>
	Teleport,

	/// <summary>Movement flagged as injected — SendInput, including Mutation's own wiggle.</summary>
	Synthetic
}

/// <summary>
/// Classifies a mouse event: a hand, a program, or nothing.
///
/// <para>
/// This is the rule the pointer hold and the wiggle rest on, so it lives here rather than inside
/// the hook that feeds it. The hook itself is a few lines of interop that cannot be tested
/// without installing a real system-wide hook; the decision is arithmetic and can be stated
/// plainly and checked.
/// </para>
///
/// <para>
/// The injected flags alone turned out not to be enough (issue #382). They do catch SendInput,
/// but a magnifier that moves the pointer through its driver produces events indistinguishable
/// from hardware by their flags — and a hand resting on a high-polling-rate mouse produces a
/// continuous stream of genuine hardware events while doing nothing at all. What separates the
/// two is the size of a single step. A hand on a 1000 Hz mouse moves a pixel or three per event;
/// no hand moves twenty pixels in one. So flags decide only what a hand cannot fake downward,
/// and distance decides what a driver cannot fake small.
/// </para>
///
/// <para>
/// Any button or wheel counts as the user, whatever it is flagged as. Remote desktop and some
/// KVM software deliver a real hand's input marked as injected, and mistaking a user for a
/// program is the expensive way round: it means an application dragging the pointer back out
/// from under someone. A button is a deliberate act however it arrived.
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
	/// The longest single-event movement still credited to a hand, in pixels on either axis.
	/// Measured hands on this machine's 1000 Hz mouse step one to three pixels per event even
	/// mid-flick; ZoomText's driver was measured stepping twenty-seven. Twelve leaves margin on
	/// both sides. A mouse polled at 125 Hz could exceed this in a genuinely fast flick, and the
	/// cost of that misreading is one corrected step during a hold — the cheap direction, since
	/// the next real step re-teaches the hold where the hand is.
	/// </summary>
	public const int HandStepLimitPixels = 12;

	/// <summary>
	/// How far a resting hand's jitter can put the pointer from where a wiggle expected it,
	/// in pixels on either axis, without meaning anything. Measured jitter is one to two pixels
	/// between two 50 ms looks; genuine travel crosses this in a single look. A hand crawling
	/// slower than this per look during an active wiggle is re-centred for the wiggle's duration
	/// — a real cost, accepted because the alternative was a wiggle that died on the first
	/// jitter pixel and so never ran at all (issue #384).
	/// </summary>
	public const int RestingHandJitterRadiusPixels = 3;

	/// <summary>
	/// Says what <paramref name="message"/> was, given its hook flags and how far it moved the
	/// pointer from the previous event, in pixels on the larger axis.
	/// </summary>
	public static RealMouseEventKind Classify(int message, uint flags, int stepPixels)
	{
		if (IsButtonOrWheel(message))
			return RealMouseEventKind.HandAct;

		if (message != MouseMove)
			return RealMouseEventKind.Nothing;

		if ((flags & (InjectedFlag | LowerIntegrityInjectedFlag)) != 0)
			return RealMouseEventKind.Synthetic;

		return Math.Abs(stepPixels) <= HandStepLimitPixels
			? RealMouseEventKind.HandStep
			: RealMouseEventKind.Teleport;
	}

	private static bool IsButtonOrWheel(int message) => message is
		LeftButtonDown or LeftButtonUp or
		RightButtonDown or RightButtonUp or
		MiddleButtonDown or MiddleButtonUp or
		ExtraButtonDown or ExtraButtonUp or
		Wheel or HorizontalWheel;
}
