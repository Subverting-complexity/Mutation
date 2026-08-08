using System;
using System.Collections.Generic;
using Mutation.Ui;
using Mutation.Ui.Core;
using Xunit;

namespace Mutation.Tests;

/// <summary>
/// Covers what a re-enumeration does to a selection whose device has gone (issue #313).
///
/// <para>
/// The manager used to keep the selected device's ID for ever, on the grounds that the
/// level-write retry path re-resolves it. On a machine with one microphone that turned a
/// re-plug into a dead end: unplug it and the list empties, plug it back in and the ID
/// matches again, so <see cref="CaptureDeviceSelectionPlanner"/> reported the selection
/// preserved — silent by design — and nothing re-opened capture. The waveform and the
/// level meter stayed dead, and a blind user was told nothing at all.
/// </para>
///
/// <para>
/// Real <c>MMDevice</c> instances need hardware, so the two halves of the fix are covered
/// where they are decidable: the rule that drops the selection, and what the planner then
/// makes of the device coming back.
/// </para>
/// </summary>
public class CaptureSelectionSurvivalTests
{
	private static CaptureDeviceInfo Device(string id) => new(id, $"Microphone {id}");

	private static readonly CaptureDeviceInfo Usb = Device("usb");
	private static readonly CaptureDeviceInfo Onboard = Device("onboard");

	[Fact]
	public void A_selection_survives_a_re_enumeration_that_still_contains_it()
	{
		Assert.True(AudioDeviceManager.ContainsDeviceId(new[] { Onboard, Usb }, Usb.Id));
	}

	[Fact]
	public void A_selection_does_not_survive_a_re_enumeration_without_it()
	{
		Assert.False(AudioDeviceManager.ContainsDeviceId(new[] { Onboard }, Usb.Id));
	}

	[Fact]
	public void A_selection_does_not_survive_an_empty_enumeration()
	{
		Assert.False(AudioDeviceManager.ContainsDeviceId(Array.Empty<CaptureDeviceInfo>(), Usb.Id));
	}

	// Endpoint IDs are compared the same way everywhere else in the manager. A casing
	// difference between two enumerations must not read as the device having vanished —
	// that would drop the user's microphone and announce a replacement for nothing.
	[Fact]
	public void A_selection_survives_a_difference_in_the_casing_of_its_id()
	{
		Assert.True(AudioDeviceManager.ContainsDeviceId(new[] { Device("USB") }, "usb"));
	}

	// The whole point, in sequence: with the stale ID dropped, the device coming back is
	// something the planner can see, and SelectionAdopted is the outcome that both
	// announces and runs the full selection path — which is what re-opens capture.
	[Fact]
	public void Re_plugging_the_only_microphone_is_adopted_and_announced()
	{
		var afterUnplug = Array.Empty<CaptureDeviceInfo>();
		Assert.False(AudioDeviceManager.ContainsDeviceId(afterUnplug, Usb.Id));

		// The selection has been dropped, so the re-plug is planned with nothing selected.
		var update = CaptureDeviceSelectionPlanner.Plan(new[] { Usb }, selectedId: null);

		Assert.Equal(CaptureDeviceListOutcome.SelectionAdopted, update.Outcome);
		Assert.Equal(Usb, update.Device);
	}

	// The other half of the contract. The mute and level retry paths re-enumerate
	// routinely and get the same set back; if that started dropping the selection, the
	// app would announce a microphone change and restart capture over and over.
	[Fact]
	public void A_routine_re_enumeration_leaves_a_present_selection_silent()
	{
		IReadOnlyList<CaptureDeviceInfo> devices = new[] { Onboard, Usb };

		Assert.True(AudioDeviceManager.ContainsDeviceId(devices, Usb.Id));
		Assert.Equal(
			CaptureDeviceListOutcome.SelectionPreserved,
			CaptureDeviceSelectionPlanner.Plan(devices, Usb.Id).Outcome);
	}
}
