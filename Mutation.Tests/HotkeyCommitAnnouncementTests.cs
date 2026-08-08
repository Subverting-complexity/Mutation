using Mutation.Ui.Core;

namespace Mutation.Tests;

/// <summary>
/// Issue #327: leaving a hotkey box rewrites what is in it, and the rewrite happened in
/// silence. These cover the two halves of getting that right — saying something when the box
/// changed, and saying nothing the rest of the time.
/// </summary>
public class HotkeyCommitAnnouncementTests
{
	[Fact]
	public void ARewrittenBoxSaysWhatItNowHolds()
	{
		// The case from the issue: typed one way, committed another. A sighted user sees it.
		string? message = HotkeyCommitAnnouncement.For(
			"Speak clipboard", "shift+ctrl+a", "CTRL+SHIFT+A", validationMessage: null);

		Assert.Equal("Speak clipboard now reads CTRL+SHIFT+A.", message);
	}

	[Fact]
	public void TheRowIsNamedSoTheUserKnowsWhichOneChanged()
	{
		// By the time a polite announcement lands the focus has moved to the next row, and
		// seventeen hotkey rows sound identical without the label.
		string? message = HotkeyCommitAnnouncement.For(
			"Toggle microphone", "alt+m", "ALT+M", validationMessage: null);

		Assert.StartsWith("Toggle microphone ", message);
	}

	[Theory]
	[InlineData(null)]
	[InlineData("")]
	[InlineData("   ")]
	public void ABoxWithNoLabelStillReportsTheRewrite(string? header)
	{
		string? message = HotkeyCommitAnnouncement.For(
			header, "alt+m", "ALT+M", validationMessage: null);

		Assert.Equal("Now reads ALT+M.", message);
	}

	[Fact]
	public void AnAlreadyCanonicalRowStaysSilent()
	{
		// Commit runs on every LostFocus, so this is the case that happens seventeen times
		// in a row while the user tabs down the Hotkeys page.
		Assert.Null(HotkeyCommitAnnouncement.For(
			"Speak clipboard", "CTRL+SHIFT+A", "CTRL+SHIFT+A", validationMessage: null));
	}

	[Fact]
	public void AnEmptyBoxLeftEmptyStaysSilent()
	{
		Assert.Null(HotkeyCommitAnnouncement.For("Speak clipboard", "", "", validationMessage: null));
	}

	[Fact]
	public void ClearingABoxIsNotAnnouncedAsARewrite()
	{
		// The Clear button did what it was asked to, and the box's own validation message
		// already says the field is now empty.
		Assert.Null(HotkeyCommitAnnouncement.For(
			"Speak clipboard", "CTRL+SHIFT+A", "", validationMessage: null));
	}

	[Fact]
	public void AValidationErrorIsNotTalkedOverByTheRewrite()
	{
		// Both messages sit under the same box. Being told the shortcut is unusable outranks
		// being told how it is now spelled.
		Assert.Null(HotkeyCommitAnnouncement.For(
			"Speak clipboard", "ctrl+", "CTRL+", validationMessage: "Add a key after the modifiers."));
	}

	[Fact]
	public void AWhitespaceOnlyValidationMessageIsNotTreatedAsAnError()
	{
		// A blank string means the validator had nothing to say, however it spelled "nothing".
		string? message = HotkeyCommitAnnouncement.For(
			"Speak clipboard", "shift+ctrl+a", "CTRL+SHIFT+A", validationMessage: "  ");

		Assert.Equal("Speak clipboard now reads CTRL+SHIFT+A.", message);
	}

	[Fact]
	public void TrimmingAloneCountsAsAChangeWorthReporting()
	{
		// The box really does hold something different afterwards, and a screen-reader user
		// re-reading the field would otherwise find it disagreeing with what they typed.
		string? message = HotkeyCommitAnnouncement.For(
			"Send hotkey after OCR", "  ^{F5}  ", "^{F5}", validationMessage: null);

		Assert.Equal("Send hotkey after OCR now reads ^{F5}.", message);
	}
}
