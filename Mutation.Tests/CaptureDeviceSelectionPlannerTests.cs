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

	[Fact]
	public void Selects_the_first_device_when_nothing_was_selected()
	{
		var update = CaptureDeviceSelectionPlanner.Plan(new[] { Onboard, Usb }, selectedId: null);

		Assert.Equal(CaptureDeviceListOutcome.SelectionReplaced, update.Outcome);
		Assert.Equal(Onboard, update.Device);
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

/// <summary>
/// Covers the check that decides whether a re-enumeration is worth telling anyone
/// about. The mute and level retry paths re-enumerate routinely; treating those as
/// device changes would rebuild the microphone combo and announce it every time
/// (issue #264).
/// </summary>
public class CaptureDeviceListChangeDetectionTests
{
	private static CaptureDeviceInfo Device(string id, string? name = null) =>
		new(id, name ?? $"Microphone {id}");

	[Fact]
	public void Same_devices_in_the_same_order_are_unchanged()
	{
		var before = new[] { Device("a"), Device("b") };
		var after = new[] { Device("a"), Device("b") };

		Assert.True(AudioDeviceManager.SameDeviceIds(before, after));
	}

	// Windows reports endpoint IDs with inconsistent casing across enumerations; a
	// case difference alone is not a device change.
	[Fact]
	public void Ids_are_compared_case_insensitively()
	{
		Assert.True(AudioDeviceManager.SameDeviceIds(
			new[] { Device("{0.0.1.00000000}.{ABC}") },
			new[] { Device("{0.0.1.00000000}.{abc}") }));
	}

	// A renamed device is the same device; announcing a hot-plug for it would be wrong.
	[Fact]
	public void A_changed_friendly_name_alone_is_not_a_device_change()
	{
		Assert.True(AudioDeviceManager.SameDeviceIds(
			new[] { Device("a", "Headset") },
			new[] { Device("a", "Headset (2- USB Audio)") }));
	}

	[Fact]
	public void An_added_device_is_a_change()
	{
		Assert.False(AudioDeviceManager.SameDeviceIds(
			new[] { Device("a") },
			new[] { Device("a"), Device("b") }));
	}

	[Fact]
	public void A_removed_device_is_a_change()
	{
		Assert.False(AudioDeviceManager.SameDeviceIds(
			new[] { Device("a"), Device("b") },
			new[] { Device("a") }));
	}

	[Fact]
	public void A_swapped_device_is_a_change()
	{
		Assert.False(AudioDeviceManager.SameDeviceIds(
			new[] { Device("a"), Device("b") },
			new[] { Device("a"), Device("c") }));
	}

	[Fact]
	public void Two_empty_enumerations_are_unchanged()
	{
		Assert.True(AudioDeviceManager.SameDeviceIds(
			Array.Empty<CaptureDeviceInfo>(),
			Array.Empty<CaptureDeviceInfo>()));
	}
}
