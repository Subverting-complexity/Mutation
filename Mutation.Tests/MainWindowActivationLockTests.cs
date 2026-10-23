using Mutation.Ui.Core;
using System.Collections.Generic;
using Xunit;

namespace Mutation.Tests;

/// <summary>
/// The rules for disabling the main window while a capture opened from another application
/// is on screen, so the main window cannot become active and be read out ahead of the
/// overlay (issue #446).
/// </summary>
public class MainWindowActivationLockTests
{
	private sealed class FakeWindow : IMainWindowEnablement
	{
		public bool IsForeground { get; set; }
		public bool IsEnabled { get; set; } = true;
		public List<bool> Writes { get; } = new();

		public void SetEnabled(bool enabled)
		{
			Writes.Add(enabled);
			IsEnabled = enabled;
		}
	}

	[Fact]
	public void A_capture_from_another_app_disables_the_main_window_until_released()
	{
		var window = new FakeWindow();
		var sut = new MainWindowActivationLock(window);

		var hold = sut.Hold();

		Assert.False(window.IsEnabled);
		Assert.True(sut.IsHoldingWindowDisabled);

		hold.Dispose();

		Assert.True(window.IsEnabled);
		Assert.False(sut.IsHoldingWindowDisabled);
		Assert.Equal(new[] { false, true }, window.Writes);
	}

	[Fact]
	public void A_capture_from_the_main_window_leaves_it_alone()
	{
		// Disabling the active window would take the keyboard from it with nowhere to go, and
		// the capture's hand-back would then return focus to a disabled window.
		var window = new FakeWindow { IsForeground = true };
		var sut = new MainWindowActivationLock(window);

		sut.Hold().Dispose();

		Assert.Empty(window.Writes);
		Assert.True(window.IsEnabled);
	}

	[Fact]
	public void A_main_window_a_dialog_already_disabled_is_never_enabled_by_the_lock()
	{
		// The prompt editor disables the main window while it is open (issue #441). Enabling it
		// at the end of a capture would let the user reach it behind the editor.
		var window = new FakeWindow { IsEnabled = false };
		var sut = new MainWindowActivationLock(window);

		sut.Hold().Dispose();

		Assert.Empty(window.Writes);
		Assert.False(window.IsEnabled);
	}

	[Fact]
	public void An_earlier_release_does_not_enable_the_window_during_a_later_capture()
	{
		// The release waits for the hand-back, and the next capture can start inside that wait.
		var window = new FakeWindow();
		var sut = new MainWindowActivationLock(window);

		var first = sut.Hold();
		var second = sut.Hold();
		first.Dispose();

		Assert.False(window.IsEnabled);

		second.Dispose();

		Assert.True(window.IsEnabled);
		Assert.Equal(new[] { false, true }, window.Writes);
	}

	[Fact]
	public void Releasing_the_same_hold_twice_releases_it_once()
	{
		var window = new FakeWindow();
		var sut = new MainWindowActivationLock(window);

		var first = sut.Hold();
		var second = sut.Hold();
		first.Dispose();
		first.Dispose();

		Assert.False(window.IsEnabled);

		second.Dispose();

		Assert.True(window.IsEnabled);
	}

	[Fact]
	public void Each_capture_decides_afresh_whether_to_disable()
	{
		var window = new FakeWindow { IsForeground = true };
		var sut = new MainWindowActivationLock(window);

		sut.Hold().Dispose();
		window.IsForeground = false;
		var hold = sut.Hold();

		Assert.False(window.IsEnabled);

		hold.Dispose();

		Assert.True(window.IsEnabled);
	}
}
