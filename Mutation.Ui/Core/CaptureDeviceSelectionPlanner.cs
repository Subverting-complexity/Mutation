#nullable enable
using System.Collections.Generic;
using System.Linq;

namespace Mutation.Ui.Core;

/// <summary>
/// Decides what a capture-device list change means for the user's current selection.
///
/// Separated from the window so the rule can be tested directly, because it is what
/// keeps a hot-plug from disrupting the user: a device appearing or disappearing
/// elsewhere in the list must leave the selected microphone exactly where it was —
/// same device, same capture, same level controls, and no announcement claiming
/// otherwise. Only the selected microphone actually going away is a change worth
/// acting on and telling the user about.
/// </summary>
public static class CaptureDeviceSelectionPlanner
{
	/// <param name="devices">The devices now present, in the order they will be shown.</param>
	/// <param name="selectedId">The endpoint ID of the currently selected microphone,
	/// or <c>null</c> when none is selected.</param>
	public static CaptureDeviceListUpdate Plan(IReadOnlyList<CaptureDeviceInfo> devices, string? selectedId)
	{
		var stillPresent = selectedId is null
			? null
			: devices.FirstOrDefault(device => device.Id == selectedId);

		if (stillPresent is not null)
			return new CaptureDeviceListUpdate(CaptureDeviceListOutcome.SelectionPreserved, stillPresent);

		if (devices.Count == 0)
			return new CaptureDeviceListUpdate(CaptureDeviceListOutcome.NoDevices, null);

		// Nothing was selected before, or what was selected is gone: the first device
		// takes over. Both cases need the full selection path to run, so the capture and
		// the level controls follow the device that is actually live.
		return new CaptureDeviceListUpdate(CaptureDeviceListOutcome.SelectionReplaced, devices[0]);
	}
}
