using System;
using System.Collections.Generic;
using Mutation.Ui;
using Mutation.Ui.Core;
using Xunit;

namespace Mutation.Tests;

/// <summary>
/// Covers what a capture-device hot-plug does to the user's microphone selection
/// (issue #264). The rules here are the accessibility contract: a device appearing or
/// disappearing elsewhere in the list must not disturb the selected microphone, and
/// only a change the user's audio actually depends on is worth announcing.
/// </summary>
public class CaptureDeviceSelectionPlannerTests
{
	private static CaptureDeviceInfo Device(string id) => new(id, $"Microphone {id}");

	private static readonly CaptureDeviceInfo Usb = Device("usb");
	private static readonly CaptureDeviceInfo Onboard = Device("onboard");
	private static readonly CaptureDeviceInfo Headset = Device("headset");

	[Fact]
	public void Keeps_the_selection_when_the_selected_device_is_still_present()
	{
		var update = CaptureDeviceSelectionPlanner.Plan(new[] { Onboard, Usb }, Usb.Id);

		Assert.Equal(CaptureDeviceListOutcome.SelectionPreserved, update.Outcome);
		Assert.Equal(Usb, update.Device);
	}

	// A different microphone being plugged in must not move the user off theirs.
	[Fact]
	public void Keeps_the_selection_when_another_device_is_added()
	{
		var before = new[] { Onboard, Usb };
		var after = new[] { Onboard, Usb, Headset };

		Assert.Equal(
			CaptureDeviceSelectionPlanner.Plan(before, Usb.Id),
			CaptureDeviceSelectionPlanner.Plan(after, Usb.Id));
	}

	// The selection is matched by ID, not by position: an unplug that shifts the
	// remaining devices must not silently hand the user a different microphone.
	[Fact]
	public void Keeps_the_selection_when_an_earlier_device_is_removed()
	{
		var update = CaptureDeviceSelectionPlanner.Plan(new[] { Usb, Headset }, Usb.Id);

		Assert.Equal(CaptureDeviceListOutcome.SelectionPreserved, update.Outcome);
		Assert.Equal(Usb, update.Device);
	}

	[Fact]
	public void Replaces_the_selection_when_the_selected_device_is_gone()
	{
		var update = CaptureDeviceSelectionPlanner.Plan(new[] { Onboard, Headset }, Usb.Id);

		Assert.Equal(CaptureDeviceListOutcome.SelectionReplaced, update.Outcome);
		Assert.Equal(Onboard, update.Device);
	}

	// Adopting a device when none was selected is not the same as losing the one that
	// was: the caller words its announcement from this outcome, and telling the user a
	// microphone "was disconnected" when one was just connected is the opposite of what
	// happened.
	[Fact]
	public void Adopts_the_first_device_when_nothing_was_selected()
	{
		var update = CaptureDeviceSelectionPlanner.Plan(new[] { Onboard, Usb }, selectedId: null);

		Assert.Equal(CaptureDeviceListOutcome.SelectionAdopted, update.Outcome);
		Assert.Equal(Onboard, update.Device);
	}

	[Fact]
	public void Distinguishes_adopting_a_device_from_losing_one()
	{
		var devices = new[] { Onboard, Usb };

		Assert.NotEqual(
			CaptureDeviceSelectionPlanner.Plan(devices, selectedId: null).Outcome,
			CaptureDeviceSelectionPlanner.Plan(devices, Headset.Id).Outcome);
	}

	[Theory]
	[InlineData("usb")]
	[InlineData(null)]
	public void Reports_no_devices_when_the_list_is_empty(string? selectedId)
	{
		var update = CaptureDeviceSelectionPlanner.Plan(Array.Empty<CaptureDeviceInfo>(), selectedId);

		Assert.Equal(CaptureDeviceListOutcome.NoDevices, update.Outcome);
		Assert.Null(update.Device);
	}
}
