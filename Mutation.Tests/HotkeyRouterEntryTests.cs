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

	[Fact]
	public void Setter_InvalidInput_ClearsMapValueDuringTypingPhase()
	{
		// Pins down current behavior: while the user is typing (setter, commit=false),
		// invalid input nulls the map's FromHotKey. SyncSettings reads `IsValid` rather
		// than the map directly, so this transient null does not leak to persisted settings.
		var map = Map("Ctrl+C", "Ctrl+V");
		var entry = new HotkeyRouterEntry(map);

		entry.FromHotkey = "Ctrl+";

		Assert.Null(map.FromHotKey);
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
}
