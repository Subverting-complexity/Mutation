using System;
using Mutation.Ui;
using Mutation.Ui.Core;
using Xunit;

namespace Mutation.Tests;

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
