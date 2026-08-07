using System.Collections.Generic;
using Mutation.Ui.Services;
using Windows.System;
using static Mutation.Ui.Services.HotkeyRegistrationTable;

namespace Mutation.Tests;

/// <summary>
/// Covers <see cref="HotkeyRegistrationTable"/> — the registration bookkeeping behind every
/// global shortcut in the app, and the largest untested unit before this (issue #255).
/// <para>
/// Everything here fails silently in production. A router refresh that forgets to release
/// its old ids leaves a chord bound to a mapping the user deleted; a duplicate that reaches
/// Windows comes back as an unexplained failure and gets blamed on another application; a
/// WM_HOTKEY for a released id that still finds a callback runs the wrong action. None of
/// these throw, and for a user who cannot see the Settings screen confirm otherwise, the
/// only evidence is the wrong thing happening.
/// </para>
/// </summary>
public class HotkeyRegistrationTableTests
{
	private static Hotkey Chord(VirtualKey key, bool control = true, bool shift = false, bool alt = false, bool win = false) =>
		new() { Key = key, Control = control, Shift = shift, Alt = alt, Win = win };

	private static (HotkeyRegistrationTable Table, FakeHotkeyPlatform Platform) NewTable()
	{
		var platform = new FakeHotkeyPlatform();
		return (new HotkeyRegistrationTable(platform), platform);
	}

	[Fact]
	public void Register_binds_the_chord_and_reports_the_id_it_used()
	{
		var (table, platform) = NewTable();

		var outcome = table.Register(Chord(VirtualKey.M), () => { }, HotkeyGroup.Core);

		Assert.True(outcome.Success);
		Assert.Null(outcome.ErrorMessage);
		Assert.Equal(new[] { outcome.Id }, platform.Bound);
		Assert.Equal(1, table.Count);
	}

	[Fact]
	public void Register_passes_the_composed_modifiers_and_key_through_to_windows()
	{
		var (table, platform) = NewTable();
		var chord = Chord(VirtualKey.K, control: true, shift: true);

		var outcome = table.Register(chord, () => { }, HotkeyGroup.Core);

		Assert.Equal(
			(HotkeyModifiers.Compose(chord), (uint)VirtualKey.K),
			platform.Requests[outcome.Id]);
	}

	[Fact]
	public void A_chord_this_process_already_holds_is_refused_without_asking_windows()
	{
		var (table, platform) = NewTable();
		table.Register(Chord(VirtualKey.M), () => { }, HotkeyGroup.Core);
		int callsAfterFirst = platform.Requests.Count;

		var second = table.Register(Chord(VirtualKey.M), () => { }, HotkeyGroup.Router);

		Assert.False(second.Success);
		Assert.Equal(NoId, second.Id);
		Assert.Equal("The shortcut is already registered.", second.ErrorMessage);
		// Windows was never asked: a second registration of a chord we already own comes back
		// as a plain failure, indistinguishable from another app holding it.
		Assert.Equal(callsAfterFirst, platform.Requests.Count);
		Assert.Equal(1, table.Count);
	}

	[Fact]
	public void The_same_chord_spelled_with_a_different_modifier_order_is_still_a_duplicate()
	{
		var (table, _) = NewTable();
		table.Register(new Hotkey { Control = true, Shift = true, Key = VirtualKey.J }, () => { }, HotkeyGroup.Core);

		var second = table.Register(new Hotkey { Shift = true, Control = true, Key = VirtualKey.J }, () => { }, HotkeyGroup.Core);

		Assert.False(second.Success);
	}

	[Fact]
	public void Chords_that_differ_only_by_a_modifier_are_not_duplicates()
	{
		var (table, _) = NewTable();
		table.Register(Chord(VirtualKey.M), () => { }, HotkeyGroup.Core);

		var withShift = table.Register(Chord(VirtualKey.M, shift: true), () => { }, HotkeyGroup.Core);

		Assert.True(withShift.Success);
		Assert.Equal(2, table.Count);
	}

	[Fact]
	public void A_registration_windows_refuses_is_not_recorded_and_leaves_the_chord_free()
	{
		var (table, platform) = NewTable();
		platform.NextFailureCode = 1409;

		var failed = table.Register(Chord(VirtualKey.M), () => { }, HotkeyGroup.Core);

		Assert.False(failed.Success);
		Assert.Equal("The shortcut is already registered by another application.", failed.ErrorMessage);
		Assert.Equal(0, table.Count);
		Assert.Empty(platform.Bound);

		// The chord was never ours, so a later attempt has to reach Windows again rather than
		// being refused as our own duplicate.
		Assert.True(table.Register(Chord(VirtualKey.M), () => { }, HotkeyGroup.Core).Success);
	}

	[Fact]
	public void A_failed_registration_still_reports_the_id_it_tried_so_the_log_can_name_it()
	{
		var (table, platform) = NewTable();
		platform.NextFailureCode = 5;

		var failed = table.Register(Chord(VirtualKey.M), () => { }, HotkeyGroup.Core);

		Assert.NotEqual(NoId, failed.Id);
	}

	[Fact]
	public void Ids_are_never_reused_even_after_the_chord_is_released()
	{
		var (table, _) = NewTable();
		var first = table.Register(Chord(VirtualKey.M), () => { }, HotkeyGroup.Router);
		table.ClearGroup(HotkeyGroup.Router);

		var second = table.Register(Chord(VirtualKey.M), () => { }, HotkeyGroup.Router);

		// A WM_HOTKEY already in the message queue when the refresh ran carries the old id.
		// Handing that id to the new mapping would fire the wrong action exactly once.
		Assert.NotEqual(first.Id, second.Id);
	}

	[Fact]
	public void Dispatch_runs_the_callback_registered_for_that_id()
	{
		var (table, _) = NewTable();
		int firstRuns = 0, secondRuns = 0;
		var first = table.Register(Chord(VirtualKey.M), () => firstRuns++, HotkeyGroup.Core);
		var second = table.Register(Chord(VirtualKey.N), () => secondRuns++, HotkeyGroup.Core);

		Assert.True(table.Dispatch(second.Id));

		Assert.Equal(0, firstRuns);
		Assert.Equal(1, secondRuns);
	}

	[Fact]
	public void Dispatch_of_an_unknown_id_does_nothing_and_says_so()
	{
		var (table, _) = NewTable();

		Assert.False(table.Dispatch(4242));
	}

	[Fact]
	public void A_released_id_no_longer_routes_anywhere()
	{
		var (table, _) = NewTable();
		int runs = 0;
		var registered = table.Register(Chord(VirtualKey.M), () => runs++, HotkeyGroup.Prompt);

		table.ClearGroup(HotkeyGroup.Prompt);

		// The message queue can still hold a WM_HOTKEY for a shortcut released a moment ago.
		Assert.False(table.Dispatch(registered.Id));
		Assert.Equal(0, runs);
	}

	[Fact]
	public void Clearing_a_group_releases_that_group_and_leaves_the_others_bound()
	{
		var (table, platform) = NewTable();
		var core = table.Register(Chord(VirtualKey.M), () => { }, HotkeyGroup.Core);
		var router = table.Register(Chord(VirtualKey.N), () => { }, HotkeyGroup.Router);
		var prompt = table.Register(Chord(VirtualKey.O), () => { }, HotkeyGroup.Prompt);

		table.ClearGroup(HotkeyGroup.Router);

		Assert.Equal(new[] { router.Id }, platform.Released);
		Assert.Equal(new[] { core.Id, prompt.Id }, platform.Bound);
		Assert.Equal(new[] { core.Id }, table.IdsIn(HotkeyGroup.Core));
		Assert.Empty(table.IdsIn(HotkeyGroup.Router));
		Assert.Equal(new[] { prompt.Id }, table.IdsIn(HotkeyGroup.Prompt));
	}

	[Fact]
	public void A_refresh_can_rebind_the_same_chord_it_just_released()
	{
		var (table, _) = NewTable();
		table.Register(Chord(VirtualKey.R), () => { }, HotkeyGroup.Router);

		table.ClearGroup(HotkeyGroup.Router);
		var again = table.Register(Chord(VirtualKey.R), () => { }, HotkeyGroup.Router);

		// Saving the Settings dialog without touching the router refreshes it. If the released
		// chord stayed in the duplicate set, every mapping would fail from the first save on.
		Assert.True(again.Success);
	}

	[Fact]
	public void Repeated_refreshes_do_not_accumulate_registrations()
	{
		var (table, platform) = NewTable();

		for (int pass = 0; pass < 5; pass++)
		{
			table.ClearGroup(HotkeyGroup.Router);
			table.Register(Chord(VirtualKey.R), () => { }, HotkeyGroup.Router);
			table.Register(Chord(VirtualKey.T), () => { }, HotkeyGroup.Router);
		}

		Assert.Equal(2, table.Count);
		Assert.Equal(2, platform.Bound.Count);
	}

	[Fact]
	public void ClearAll_releases_every_group_and_frees_every_chord()
	{
		var (table, platform) = NewTable();
		table.Register(Chord(VirtualKey.M), () => { }, HotkeyGroup.Core);
		table.Register(Chord(VirtualKey.N), () => { }, HotkeyGroup.Router);
		table.Register(Chord(VirtualKey.O), () => { }, HotkeyGroup.Prompt);

		table.ClearAll();

		Assert.Equal(0, table.Count);
		Assert.Empty(platform.Bound);
		Assert.Equal(3, platform.Released.Count);
		Assert.False(table.IsRegistered(Chord(VirtualKey.M)));
		Assert.True(table.Register(Chord(VirtualKey.M), () => { }, HotkeyGroup.Core).Success);
	}

	[Fact]
	public void Clearing_a_group_twice_releases_nothing_the_second_time()
	{
		var (table, platform) = NewTable();
		table.Register(Chord(VirtualKey.M), () => { }, HotkeyGroup.Prompt);

		table.ClearGroup(HotkeyGroup.Prompt);
		table.ClearGroup(HotkeyGroup.Prompt);

		// Unregistering an id Windows has already released is harmless, but doing it means the
		// table lost track of what it owns.
		Assert.Single(platform.Released);
	}

	[Fact]
	public void IdsIn_lists_a_groups_registrations_in_the_order_they_were_made()
	{
		var (table, _) = NewTable();
		var first = table.Register(Chord(VirtualKey.R), () => { }, HotkeyGroup.Router);
		table.Register(Chord(VirtualKey.X), () => { }, HotkeyGroup.Core);
		var second = table.Register(Chord(VirtualKey.S), () => { }, HotkeyGroup.Router);
		var third = table.Register(Chord(VirtualKey.T), () => { }, HotkeyGroup.Router);

		Assert.Equal(new[] { first.Id, second.Id, third.Id }, table.IdsIn(HotkeyGroup.Router));
	}

	[Fact]
	public void A_table_needs_something_to_register_against()
	{
		Assert.Throws<ArgumentNullException>(() => new HotkeyRegistrationTable(null!));
	}

	[Fact]
	public void A_registration_without_a_callback_is_refused_at_the_point_it_is_made()
	{
		var (table, platform) = NewTable();

		// Left to Windows, a null callback binds fine and only fails the first time the user
		// presses the chord — inside the WM_HOTKEY pump, far from the mistake.
		Assert.Throws<ArgumentNullException>(() => table.Register(Chord(VirtualKey.M), null!, HotkeyGroup.Core));
		Assert.Empty(platform.Bound);
	}

	[Fact]
	public void A_callback_that_throws_is_not_swallowed()
	{
		var (table, _) = NewTable();
		var registered = table.Register(Chord(VirtualKey.M), () => throw new InvalidOperationException("boom"), HotkeyGroup.Core);

		// Documents where the failure surfaces: the table does not catch, so the exception
		// reaches WndProc. Anything quieter would lose a hotkey action with no trace.
		Assert.Throws<InvalidOperationException>(() => table.Dispatch(registered.Id));
	}

	[Fact]
	public void IsRegistered_reports_what_is_currently_bound()
	{
		var (table, _) = NewTable();
		Assert.False(table.IsRegistered(Chord(VirtualKey.M)));

		table.Register(Chord(VirtualKey.M), () => { }, HotkeyGroup.Core);
		Assert.True(table.IsRegistered(Chord(VirtualKey.M)));

		table.ClearAll();
		Assert.False(table.IsRegistered(Chord(VirtualKey.M)));
	}

	[Fact]
	public void NormalizeHotkey_puts_the_modifiers_in_one_fixed_order()
	{
		var chord = new Hotkey { Win = true, Alt = true, Shift = true, Control = true, Key = VirtualKey.F1 };

		Assert.Equal("CTRL+SHIFT+ALT+WIN+F1", NormalizeHotkey(chord));
	}

	[Fact]
	public void NormalizeHotkey_leaves_a_bare_key_bare()
	{
		// No modifiers means no separator — a trailing "+" here would make an unmodified key
		// compare unequal to itself under any other spelling.
		Assert.Equal("F1", NormalizeHotkey(new Hotkey { Key = VirtualKey.F1 }));
	}

	[Fact]
	public void NormalizeHotkey_keeps_each_modifier_in_its_own_slot()
	{
		Assert.Equal("SHIFT+A", NormalizeHotkey(new Hotkey { Shift = true, Key = VirtualKey.A }));
		Assert.Equal("ALT+A", NormalizeHotkey(new Hotkey { Alt = true, Key = VirtualKey.A }));
		Assert.Equal("WIN+A", NormalizeHotkey(new Hotkey { Win = true, Key = VirtualKey.A }));
	}

	[Theory]
	[InlineData(1409, "The shortcut is already registered by another application.")]
	[InlineData(0, "Failed to register the shortcut.")]
	public void DescribeRegistrationError_explains_the_codes_worth_naming(int code, string expected)
	{
		Assert.Equal(expected, DescribeRegistrationError(code));
	}

	[Fact]
	public void DescribeRegistrationError_falls_back_to_the_system_text_for_anything_else()
	{
		string message = DescribeRegistrationError(5);

		Assert.False(string.IsNullOrWhiteSpace(message));
		Assert.NotEqual("Failed to register the shortcut.", message);
	}

	[Fact]
	public void Registrations_and_releases_are_written_to_the_log_the_manager_supplies()
	{
		var platform = new FakeHotkeyPlatform();
		var lines = new List<string>();
		var table = new HotkeyRegistrationTable(platform, lines.Add);

		table.Register(Chord(VirtualKey.M), () => { }, HotkeyGroup.Core);
		table.Register(Chord(VirtualKey.M), () => { }, HotkeyGroup.Core);

		Assert.Contains(lines, l => l.Contains("Hotkey registered") && l.Contains("CTRL+M"));
		Assert.Contains(lines, l => l.Contains("Duplicate hotkey detected"));
	}
}
