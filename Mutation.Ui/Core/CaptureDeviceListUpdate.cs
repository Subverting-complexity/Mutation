#nullable enable
namespace Mutation.Ui.Core;

/// <summary>What a capture-device list change means for the user's selection.</summary>
public enum CaptureDeviceListOutcome
{
	/// <summary>The selected microphone is still present. The list is rebuilt around
	/// it and nothing else changes — no capture restart, no level re-probe, and no
	/// announcement beyond the list itself having changed.</summary>
	SelectionPreserved,

	/// <summary>The selected microphone is gone and another one takes over. The user
	/// must be told: their audio is now coming from a different device than the one
	/// they chose.</summary>
	SelectionReplaced,

	/// <summary>Nothing was selected and a device is now available, so it is adopted.
	/// Distinct from <see cref="SelectionReplaced"/> because nothing was lost — telling
	/// the user a microphone "was disconnected" here would be the opposite of what
	/// happened.</summary>
	SelectionAdopted,

	/// <summary>Nothing is left to select.</summary>
	NoDevices,
}

/// <param name="Outcome">What happened to the selection.</param>
/// <param name="Device">The microphone that is now selected, or <c>null</c> when none
/// is available.</param>
public readonly record struct CaptureDeviceListUpdate(
	CaptureDeviceListOutcome Outcome,
	CaptureDeviceInfo? Device);
