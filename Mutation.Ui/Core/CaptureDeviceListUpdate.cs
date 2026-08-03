#nullable enable
namespace Mutation.Ui.Core;

/// <summary>What a capture-device list change means for the user's selection.</summary>
public enum CaptureDeviceListOutcome
{
	/// <summary>The selected microphone is still present. The list is rebuilt around
	/// it and nothing else changes — no capture restart, no level re-probe, and no
	/// announcement beyond the list itself having changed.</summary>
	SelectionPreserved,

	/// <summary>The selected microphone is gone. Another one takes over, which the
	/// user must be told about: their audio is now coming from a different device.</summary>
	SelectionReplaced,

	/// <summary>Nothing is left to select.</summary>
	NoDevices,
}

/// <param name="Outcome">What happened to the selection.</param>
/// <param name="Device">The microphone that is now selected, or <c>null</c> when none
/// is available.</param>
public readonly record struct CaptureDeviceListUpdate(
	CaptureDeviceListOutcome Outcome,
	CaptureDeviceInfo? Device);
