using Mutation.Ui.Core;
using System;
using Xunit;

namespace Mutation.Tests;

/// <summary>
/// How a capture names each foreground change in its log, and when it takes the foreground
/// back for the overlay (issue #446).
/// </summary>
public class CaptureForegroundTraceTests
{
	private static readonly IntPtr Main = new(0x100);
	private static readonly IntPtr Overlay = new(0x200);
	private static readonly IntPtr Elsewhere = new(0x300);

	[Fact]
	public void Each_window_is_classified_by_its_role_in_the_capture()
	{
		var sut = new CaptureForegroundTrace(Main, Overlay);

		Assert.Equal(CaptureWindowRole.MainWindow, sut.Classify(Main));
		Assert.Equal(CaptureWindowRole.Overlay, sut.Classify(Overlay));
		Assert.Equal(CaptureWindowRole.Other, sut.Classify(Elsewhere));
		Assert.Equal(CaptureWindowRole.None, sut.Classify(IntPtr.Zero));
	}

	[Fact]
	public void The_main_window_taking_the_foreground_while_the_overlay_is_open_is_taken_back()
	{
		var sut = new CaptureForegroundTrace(Main, Overlay);

		Assert.True(sut.ShouldReclaim(Main));
	}

	[Fact]
	public void Nothing_else_is_taken_back()
	{
		var sut = new CaptureForegroundTrace(Main, Overlay);

		Assert.False(sut.ShouldReclaim(Overlay));
		Assert.False(sut.ShouldReclaim(Elsewhere));
		Assert.False(sut.ShouldReclaim(IntPtr.Zero));
	}

	[Fact]
	public void Once_the_selection_has_finished_the_main_window_is_not_taken_back()
	{
		// Pulling the overlay back then would put a finished capture on screen again.
		var sut = new CaptureForegroundTrace(Main, Overlay);

		sut.MarkOverlayClosed();

		Assert.False(sut.OverlayOpen);
		Assert.False(sut.ShouldReclaim(Main));
	}

	[Fact]
	public void No_main_window_means_nothing_is_ever_taken_back()
	{
		// A zero main-window handle must not match the zero "no window" foreground.
		var sut = new CaptureForegroundTrace(IntPtr.Zero, Overlay);

		Assert.Equal(CaptureWindowRole.None, sut.Classify(IntPtr.Zero));
		Assert.False(sut.ShouldReclaim(IntPtr.Zero));
	}

	[Fact]
	public void The_log_line_names_the_main_window_and_says_it_is_being_taken_back()
	{
		var sut = new CaptureForegroundTrace(Main, Overlay);

		string line = sut.Describe(Main, null, null);

		Assert.Contains("MAIN WINDOW", line);
		Assert.Contains("hwnd=0x100", line);
		Assert.Contains("taking it back", line);
	}

	[Fact]
	public void The_log_line_names_another_application_by_process_and_class()
	{
		var sut = new CaptureForegroundTrace(Main, Overlay);

		string line = sut.Describe(Elsewhere, "Notepad", "notepad");

		Assert.Contains("another window (notepad, Notepad)", line);
		Assert.DoesNotContain("taking it back", line);
	}

	[Fact]
	public void The_log_line_survives_a_window_it_could_not_read()
	{
		var sut = new CaptureForegroundTrace(Main, Overlay);

		string line = sut.Describe(Elsewhere, null, " ");

		Assert.Contains("another window (unknown process, unknown class)", line);
	}

	[Fact]
	public void The_overlay_and_an_empty_foreground_are_named_plainly()
	{
		var sut = new CaptureForegroundTrace(Main, Overlay);

		Assert.Contains("the capture overlay", sut.Describe(Overlay, null, null));
		Assert.Contains("no window", sut.Describe(IntPtr.Zero, null, null));
	}
}
