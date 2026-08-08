using System;
using System.Collections.Generic;
using CognitiveSupport;
using Mutation.Ui;

namespace Mutation.Tests;

public class HotkeyRouterEntryTests
{
	private static HotKeyRouterSettings.HotKeyRouterMap Map(string from, string to)
		=> new HotKeyRouterSettings.HotKeyRouterMap(from, to);

	[Fact]
	public void Ctor_NullMap_Throws()
	{
		Assert.Throws<ArgumentNullException>(() => new HotkeyRouterEntry(null!));
	}

	[Fact]
	public void Ctor_ValidMap_NormalizesAndMarksValid()
	{
		var entry = new HotkeyRouterEntry(Map("Ctrl+C", "Ctrl+V"));

		Assert.True(entry.IsFromValid);
		Assert.True(entry.IsToValid);
		Assert.True(entry.IsValid);
		Assert.False(entry.IsDuplicate);
		Assert.Equal("CTRL+C", entry.NormalizedFromHotkey);
		Assert.Equal("CTRL+V", entry.NormalizedToHotkey);
	}

	[Fact]
	public void Ctor_EmptyMap_MarksInvalid()
	{
		var entry = new HotkeyRouterEntry(Map(string.Empty, string.Empty));

		Assert.False(entry.IsFromValid);
		Assert.False(entry.IsToValid);
		Assert.False(entry.IsValid);
		Assert.True(entry.HasBindingError);
	}

	[Fact]
	public void Ctor_InvalidFromHotkey_MarksFromInvalid()
	{
		var entry = new HotkeyRouterEntry(Map("Ctrl+", "Ctrl+V"));

		Assert.False(entry.IsFromValid);
		Assert.True(entry.IsToValid);
		Assert.False(entry.IsValid);
	}

	[Fact]
	public void SetDuplicate_True_InvalidatesEntryAndClearsBinding()
	{
		var entry = new HotkeyRouterEntry(Map("Ctrl+C", "Ctrl+V"));
		entry.SetBindingResult(HotkeyBindingState.Bound, null);

		entry.SetDuplicate(true);

		Assert.True(entry.IsDuplicate);
		Assert.False(entry.IsFromInputValid);
		Assert.False(entry.IsValid);
		Assert.Equal(HotkeyBindingState.Inactive, entry.BindingState);
	}

	[Fact]
	public void SetDuplicate_False_RestoresValidity()
	{
		var entry = new HotkeyRouterEntry(Map("Ctrl+C", "Ctrl+V"));
		entry.SetDuplicate(true);
		entry.SetDuplicate(false);

		Assert.False(entry.IsDuplicate);
		Assert.True(entry.IsFromInputValid);
		Assert.True(entry.IsValid);
	}

	[Fact]
	public void SetBindingResult_Failed_PopulatesBindingError()
	{
		var entry = new HotkeyRouterEntry(Map("Ctrl+C", "Ctrl+V"));

		entry.SetBindingResult(HotkeyBindingState.Failed, "registration failed");

		Assert.Equal(HotkeyBindingState.Failed, entry.BindingState);
		Assert.True(entry.HasBindingError);
		Assert.Equal("registration failed", entry.BindingErrorMessage);
	}

	[Fact]
	public void SetBindingResult_Bound_ClearsBindingError()
	{
		var entry = new HotkeyRouterEntry(Map("Ctrl+C", "Ctrl+V"));
		entry.SetBindingResult(HotkeyBindingState.Failed, "boom");

		entry.SetBindingResult(HotkeyBindingState.Bound, null);

		Assert.Equal(HotkeyBindingState.Bound, entry.BindingState);
		Assert.False(entry.HasBindingError);
		Assert.Null(entry.BindingErrorMessage);
	}

	// While the user is typing (setter, commit=false) invalid input clears the map's
	// value. It clears it to blank, not null: RefreshRegistrations auto-persists, so a
	// half-typed hotkey does reach the file, and a null there is what the load-time
	// repair reports back at the user on the next launch (issue #247).
	[Fact]
	public void Setter_InvalidInput_BlanksMapValueDuringTypingPhase()
	{
		var map = Map("Ctrl+C", "Ctrl+V");
		var entry = new HotkeyRouterEntry(map);

		entry.FromHotkey = "Ctrl+";

		Assert.Equal(string.Empty, map.FromHotKey);
	}

	[Fact]
	public void Setter_InvalidToInput_BlanksMapValueDuringTypingPhase()
	{
		var map = Map("Ctrl+C", "Ctrl+V");
		var entry = new HotkeyRouterEntry(map);

		entry.ToHotkey = "Ctrl+";

		Assert.Equal(string.Empty, map.ToHotKey);
	}

	[Fact]
	public void Constructor_WithInvalidInitialMapValue_DoesNotWipeIt()
	{
		// Regression: constructor goes through commit=true, which preserves the
		// existing map value even if the input is unparseable.
		var map = Map("not-a-real-hotkey", "Ctrl+V");
		var entry = new HotkeyRouterEntry(map);

		Assert.False(entry.IsFromValid);
		Assert.Equal("not-a-real-hotkey", map.FromHotKey);
	}

	[Fact]
	public void CommitFromHotkey_OnValidNewValue_UpdatesMap()
	{
		var map = Map("Ctrl+C", "Ctrl+V");
		var entry = new HotkeyRouterEntry(map);

		entry.FromHotkey = "Ctrl+X";
		entry.CommitFromHotkey();

		Assert.Equal("CTRL+X", map.FromHotKey);
	}

	[Fact]
	public void CommitToHotkey_OnValidNewValue_UpdatesMap()
	{
		var map = Map("Ctrl+C", "Ctrl+V");
		var entry = new HotkeyRouterEntry(map);

		entry.ToHotkey = "Ctrl+B";
		entry.CommitToHotkey();

		Assert.Equal("CTRL+B", map.ToHotKey);
	}

	// A row used to keep the modifier order the user typed, so "Shift+Ctrl+X" went to the
	// settings file spelled that way while the rest of the app spelled the same chord
	// "CTRL+SHIFT+X" (issue #323).
	[Fact]
	public void Commit_WritesTheChordInTheAppsOwnModifierOrder()
	{
		var map = Map("Ctrl+C", "Ctrl+V");
		var entry = new HotkeyRouterEntry(map);

		entry.FromHotkey = "Shift+Ctrl+X";
		entry.CommitFromHotkey();
		entry.ToHotkey = "alt+ctrl+y";
		entry.CommitToHotkey();

		Assert.Equal("CTRL+SHIFT+X", map.FromHotKey);
		Assert.Equal("CTRL+SHIFT+X", entry.FromHotkey);
		Assert.Equal("CTRL+ALT+Y", map.ToHotKey);
		Assert.Equal("CTRL+ALT+Y", entry.ToHotkey);
	}

	[Fact]
	public void Commit_LeavesHalfTypedTextOnScreenRatherThanErasingIt()
	{
		// It does not parse, so there is no canonical spelling to write. The row still has to
		// show what the user typed — the validation message beside it is what tells them it is
		// not finished.
		var entry = new HotkeyRouterEntry(Map("Ctrl+C", "Ctrl+V"));

		entry.FromHotkey = "ctrl+shift+";
		entry.CommitFromHotkey();

		Assert.False(entry.IsFromValid);
		Assert.Equal("CTRL+SHIFT", entry.FromHotkey);
	}

	// ----- Announcing a rewrite (issue #332) -----
	//
	// These two boxes tidied up in silence, exactly as the hotkey editors did before #327:
	// type "shift+ctrl+a", press Tab, and a sighted user watches it become "CTRL+SHIFT+A"
	// while a screen-reader user hears nothing and reads back something they did not type.

	[Fact]
	public void Commit_ThatRewritesTheFromBox_SaysWhatItNowReads()
	{
		var entry = new HotkeyRouterEntry(Map("Ctrl+C", "Ctrl+V"));

		entry.FromHotkey = "shift+ctrl+a";
		entry.CommitFromHotkey();

		Assert.Equal("Shortcut to listen for now reads CTRL+SHIFT+A.", entry.FromCommitAnnouncement);
		Assert.Null(entry.ToCommitAnnouncement);
	}

	[Fact]
	public void Commit_ThatRewritesTheToBox_SaysWhatItNowReads()
	{
		var entry = new HotkeyRouterEntry(Map("Ctrl+C", "Ctrl+V"));

		entry.ToHotkey = "alt+ctrl+y";
		entry.CommitToHotkey();

		Assert.Equal("Shortcut to send when triggered now reads CTRL+ALT+Y.", entry.ToCommitAnnouncement);
		Assert.Null(entry.FromCommitAnnouncement);
	}

	[Fact]
	public void Commit_OfTextAlreadyWrittenCanonically_StaysSilent()
	{
		// The common case by far. Tabbing across a row that is already tidy has to say
		// nothing, or the list talks over the user on every mapping they pass.
		var entry = new HotkeyRouterEntry(Map("Ctrl+C", "Ctrl+V"));

		entry.FromHotkey = "CTRL+SHIFT+A";
		entry.CommitFromHotkey();

		Assert.Null(entry.FromCommitAnnouncement);
	}

	[Fact]
	public void Commit_OfHalfTypedText_LeavesTheRowErrorToSpeak()
	{
		// "CTRL+SHIFT" is a rewrite of what was typed, but the row already has something more
		// important to say about it. Being told the shortcut is unusable outranks being told
		// how it is now spelled, and the two would otherwise arrive in the same breath.
		var entry = new HotkeyRouterEntry(Map("Ctrl+C", "Ctrl+V"));

		entry.FromHotkey = "ctrl+shift+";
		entry.CommitFromHotkey();

		Assert.NotNull(entry.BindingErrorMessage);
		Assert.Null(entry.FromCommitAnnouncement);
	}

	[Fact]
	public void ADuplicateFoundAfterTheCommit_TakesDownTheRewriteNotice()
	{
		// Duplicates are recomputed after the commit that caused them, so the notice can
		// already be standing by the time the clash is known. The clash is the more urgent
		// news and must not have a tidy-up notice talking over it.
		var entry = new HotkeyRouterEntry(Map("Ctrl+C", "Ctrl+V"));

		entry.FromHotkey = "shift+ctrl+a";
		entry.CommitFromHotkey();
		Assert.NotNull(entry.FromCommitAnnouncement);

		entry.SetDuplicate(true);

		Assert.Null(entry.FromCommitAnnouncement);
	}

	[Fact]
	public void BuildingARowFromSettings_SaysNothing()
	{
		// A settings file written before the app canonicalised these values would otherwise
		// read every tidied row aloud at startup, about an edit nobody made.
		var entry = new HotkeyRouterEntry(Map("shift+ctrl+a", "alt+ctrl+y"));

		Assert.Equal("CTRL+SHIFT+A", entry.FromHotkey);
		Assert.Null(entry.FromCommitAnnouncement);
		Assert.Null(entry.ToCommitAnnouncement);
	}

	[Fact]
	public void CommittingSilently_LeavesAStandingNoticeAlone()
	{
		// Saving the page re-commits every row. The row the user just left has already said
		// what it rewrote, and a second commit of the same text must not wipe that.
		var entry = new HotkeyRouterEntry(Map("Ctrl+C", "Ctrl+V"));

		entry.FromHotkey = "shift+ctrl+a";
		entry.CommitFromHotkey();

		entry.CommitSilently();

		Assert.Equal("Shortcut to listen for now reads CTRL+SHIFT+A.", entry.FromCommitAnnouncement);
	}

	[Fact]
	public void ReturningToTheBox_TakesTheNoticeDown()
	{
		// Left standing it becomes an ordinary line of text under the row, read out as current
		// content by anyone going down the page afterwards.
		var entry = new HotkeyRouterEntry(Map("Ctrl+C", "Ctrl+V"));

		entry.FromHotkey = "shift+ctrl+a";
		entry.CommitFromHotkey();

		entry.ClearFromCommitAnnouncement();

		Assert.Null(entry.FromCommitAnnouncement);
	}

	[Fact]
	public void TheSameRewriteTwiceIsAnnouncedTwice()
	{
		// Clearing on the way back in is what makes this work: an unchanged message would
		// otherwise be swallowed as "nothing new to say".
		var entry = new HotkeyRouterEntry(Map("Ctrl+C", "Ctrl+V"));
		var announcements = new List<string?>();
		entry.PropertyChanged += (_, e) =>
		{
			if (e.PropertyName == nameof(HotkeyRouterEntry.FromCommitAnnouncement))
				announcements.Add(entry.FromCommitAnnouncement);
		};

		entry.FromHotkey = "shift+ctrl+a";
		entry.CommitFromHotkey();
		entry.ClearFromCommitAnnouncement();
		entry.FromHotkey = "shift+ctrl+a";
		entry.CommitFromHotkey();

		Assert.Equal(
			new[] { "Shortcut to listen for now reads CTRL+SHIFT+A.", null, "Shortcut to listen for now reads CTRL+SHIFT+A." },
			announcements);
	}
}
