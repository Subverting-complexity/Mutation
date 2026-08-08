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

	// ----- Saying out loud what the box rewrote itself to (issue #332) -----
	//
	// A sighted user watches "shift+ctrl+a" become "CTRL+SHIFT+A" on the way out of the box.
	// A screen-reader user heard nothing, and the next reading of the field disagreed with
	// what they had typed. The hotkey editor above this section was fixed for #327; these two
	// boxes are the same defect on the same page.

	[Fact]
	public void CommittingAFromBoxThatTidiedItselfUpSaysSo()
	{
		var entry = new HotkeyRouterEntry(Map("Ctrl+C", "Ctrl+V"));

		entry.FromHotkey = "shift+ctrl+a";
		entry.CommitFromHotkey();

		Assert.Equal("Shortcut to listen for now reads CTRL+SHIFT+A.", entry.FromCommitAnnouncement);
	}

	[Fact]
	public void CommittingAToBoxThatTidiedItselfUpSaysSo()
	{
		// The row's two boxes are named apart, because the notice lands after the focus has
		// moved on and "now reads CTRL+ALT+Y" alone would not say which one changed.
		var entry = new HotkeyRouterEntry(Map("Ctrl+C", "Ctrl+V"));

		entry.ToHotkey = "alt+ctrl+y";
		entry.CommitToHotkey();

		Assert.Equal("Shortcut to send when triggered now reads CTRL+ALT+Y.", entry.ToCommitAnnouncement);
	}

	[Fact]
	public void ABoxAlreadyWrittenTheAppsOwnWayStaysSilent()
	{
		// Tabbing across a row that needs no tidying is the common case by far, and has to
		// stay quiet or the page talks over the user on every row.
		var entry = new HotkeyRouterEntry(Map("Ctrl+C", "Ctrl+V"));

		entry.FromHotkey = "CTRL+X";
		entry.CommitFromHotkey();
		entry.ToHotkey = "CTRL+B";
		entry.CommitToHotkey();

		Assert.Null(entry.FromCommitAnnouncement);
		Assert.Null(entry.ToCommitAnnouncement);
	}

	[Fact]
	public void HalfTypedTextIsLeftToTheErrorRatherThanAnnounced()
	{
		// The row already says the shortcut is unusable. Being told how it is now spelled on
		// top of that is the less useful of the two, and both in one breath is neither.
		var entry = new HotkeyRouterEntry(Map("Ctrl+C", "Ctrl+V"));

		entry.FromHotkey = "ctrl+shift+";
		entry.CommitFromHotkey();

		Assert.Null(entry.FromCommitAnnouncement);
	}

	[Fact]
	public void ADuplicateOutranksTheRewriteNotice()
	{
		var entry = new HotkeyRouterEntry(Map("Ctrl+C", "Ctrl+V"));
		entry.SetDuplicate(true);

		entry.FromHotkey = "shift+ctrl+a";
		entry.CommitFromHotkey();

		Assert.Null(entry.FromCommitAnnouncement);
	}

	[Fact]
	public void AFromBoxIsStillAnnouncedWhileTheToBoxBesideItIsEmpty()
	{
		// Every newly added row starts with both boxes empty, so a notice that waited for the
		// whole row to be valid would never be heard on the row most likely to need it.
		var entry = new HotkeyRouterEntry(Map(string.Empty, string.Empty));

		entry.FromHotkey = "shift+ctrl+a";
		entry.CommitFromHotkey();

		Assert.False(entry.IsToValid);
		Assert.Equal("Shortcut to listen for now reads CTRL+SHIFT+A.", entry.FromCommitAnnouncement);
	}

	[Fact]
	public void LoadingASettingsFileThatNeedsTidyingUpSaysNothing()
	{
		// The constructor canonicalises too. Announcing that would read every stored row aloud
		// on the way into the page.
		var entry = new HotkeyRouterEntry(Map("shift+ctrl+a", "alt+ctrl+y"));

		Assert.Equal("CTRL+SHIFT+A", entry.FromHotkey);
		Assert.Null(entry.FromCommitAnnouncement);
		Assert.Null(entry.ToCommitAnnouncement);
	}

	[Fact]
	public void RebuildingTheRowLeavesAPendingNoticeStanding()
	{
		// Saving the page rewrites the backing maps and hands each row a new one. Treating
		// that as a fresh silent load would swallow the notice for the very edit that
		// triggered the save.
		var entry = new HotkeyRouterEntry(Map("Ctrl+C", "Ctrl+V"));
		entry.FromHotkey = "shift+ctrl+a";
		entry.CommitFromHotkey();

		entry.ReplaceBackingMap(Map("CTRL+SHIFT+A", "CTRL+V"));

		Assert.Equal("Shortcut to listen for now reads CTRL+SHIFT+A.", entry.FromCommitAnnouncement);
	}

	[Fact]
	public void ComingBackIntoTheBoxTakesTheNoticeDown()
	{
		var entry = new HotkeyRouterEntry(Map("Ctrl+C", "Ctrl+V"));
		entry.FromHotkey = "shift+ctrl+a";
		entry.CommitFromHotkey();

		entry.ClearFromCommitAnnouncement();

		Assert.Null(entry.FromCommitAnnouncement);
	}

	[Fact]
	public void TabbingFromTheFromBoxIntoTheToBoxLeavesTheFromNoticeStanding()
	{
		// Issue #344. One notice per row meant leaving "From" set it and arriving in "To"
		// took it straight back down — both inside the one focus change, and both before the
		// announcement the notice had queued could be spoken. So the box the issue was
		// actually about was the one box it never worked for.
		var entry = new HotkeyRouterEntry(Map("Ctrl+C", "Ctrl+V"));

		entry.FromHotkey = "shift+ctrl+a";
		entry.CommitFromHotkey();
		// What arriving in the "To" box does.
		entry.ClearToCommitAnnouncement();

		Assert.Equal("Shortcut to listen for now reads CTRL+SHIFT+A.", entry.FromCommitAnnouncement);
	}

	[Fact]
	public void EachBoxTakesDownItsOwnNoticeAndOnlyItsOwn()
	{
		var entry = new HotkeyRouterEntry(Map("Ctrl+C", "Ctrl+V"));
		entry.FromHotkey = "shift+ctrl+a";
		entry.CommitFromHotkey();
		entry.ToHotkey = "alt+ctrl+y";
		entry.CommitToHotkey();

		entry.ClearFromCommitAnnouncement();

		Assert.Null(entry.FromCommitAnnouncement);
		Assert.Equal("Shortcut to send when triggered now reads CTRL+ALT+Y.", entry.ToCommitAnnouncement);
	}

	[Fact]
	public void TheNoticeIsPublishedSoTheRowCanBind()
	{
		// The row is a data template with no code-behind to call LiveMessage.Show from, so the
		// change notification is the whole delivery mechanism.
		var entry = new HotkeyRouterEntry(Map("Ctrl+C", "Ctrl+V"));
		var raised = new List<string?>();
		entry.PropertyChanged += (_, e) =>
		{
			if (e.PropertyName == nameof(HotkeyRouterEntry.FromCommitAnnouncement))
				raised.Add(entry.FromCommitAnnouncement);
		};

		entry.FromHotkey = "shift+ctrl+a";
		entry.CommitFromHotkey();
		entry.ClearFromCommitAnnouncement();

		Assert.Equal(["Shortcut to listen for now reads CTRL+SHIFT+A.", null], raised);
	}
}
