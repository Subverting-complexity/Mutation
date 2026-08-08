using System.Collections.ObjectModel;
using System.Reflection;
using System.Runtime.CompilerServices;
using CognitiveSupport;
using Mutation.Ui;
using Mutation.Ui.Services;

namespace Mutation.Tests;

// HotkeyRouterController is internal sealed and its constructor requires WinUI types
// (ListView, DispatcherQueue). The methods under test (SyncSettings, ShouldPersist,
// RecalculateDuplicates) never touch those fields, so we bypass the constructor with
// RuntimeHelpers.GetUninitializedObject and inject only the fields we exercise.
public class HotkeyRouterControllerTests
{
	private static (HotkeyRouterController controller,
		Settings settings,
		FakeSettingsManager settingsManager,
		ObservableCollection<HotkeyRouterEntry> entries,
		List<(string From, string To)> snapshot)
	// injectSettingsManager mirrors the two real shapes: the main window passes a manager
	// with autoPersist on, while the settings page passes null with autoPersist off. It is
	// separable so a test can prove the _autoPersist gate itself rather than relying on a
	// null field to make the save impossible.
	BuildController(bool initialized = true, bool autoPersist = true, bool injectSettingsManager = true)
	{
		var controller = (HotkeyRouterController)RuntimeHelpers.GetUninitializedObject(typeof(HotkeyRouterController));
		var settings = new Settings();
		var settingsManager = new FakeSettingsManager();
		var entries = new ObservableCollection<HotkeyRouterEntry>();
		var snapshot = new List<(string From, string To)>();

		SetField(controller, "_settings", settings);
		SetField(controller, "_settingsManager", injectSettingsManager ? settingsManager : null);
		SetField(controller, "_entries", entries);
		SetField(controller, "_persistedSnapshot", snapshot);
		SetField(controller, "_initialized", initialized);
		SetField(controller, "_autoPersist", autoPersist);

		return (controller, settings, settingsManager, entries, snapshot);
	}

	private static void SetField(object instance, string name, object? value)
	{
		var field = typeof(HotkeyRouterController).GetField(name, BindingFlags.NonPublic | BindingFlags.Instance);
		Assert.NotNull(field);
		field!.SetValue(instance, value);
	}

	private static List<(string From, string To)> CallSyncSettings(HotkeyRouterController controller)
		=> (List<(string From, string To)>)typeof(HotkeyRouterController)
			.GetMethod("SyncSettings", BindingFlags.Public | BindingFlags.Instance)!
			.Invoke(controller, null)!;

	private static bool CallShouldPersist(HotkeyRouterController controller, List<(string From, string To)> pairs)
		=> (bool)typeof(HotkeyRouterController)
			.GetMethod("ShouldPersist", BindingFlags.NonPublic | BindingFlags.Instance)!
			.Invoke(controller, new object[] { pairs })!;

	private static void CallRefreshRegistrations(HotkeyRouterController controller)
		=> typeof(HotkeyRouterController)
			.GetMethod("RefreshRegistrations", BindingFlags.NonPublic | BindingFlags.Instance)!
			.Invoke(controller, null);

	private static void CallRecalculateDuplicates(HotkeyRouterController controller)
		=> typeof(HotkeyRouterController)
			.GetMethod("RecalculateDuplicates", BindingFlags.NonPublic | BindingFlags.Instance)!
			.Invoke(controller, null);

	[Fact]
	public void CommittingABoxLeavesItsNoticeStandingThroughTheRefreshThatFollows()
	{
		// Every commit is followed by RefreshRegistrations, which calls SyncSettings, which
		// re-commits every row. The row that just committed is now canonical, so the second
		// commit finds nothing changed and clears the notice it had already raised — before
		// the deferred announcement it queued ever runs. That cancels the notice on every
		// real path, so the boxes announce nothing at all.
		var (controller, _, _, entries, _) = BuildController();
		var entry = new HotkeyRouterEntry(new HotKeyRouterSettings.HotKeyRouterMap("Ctrl+C", "Ctrl+V"));
		entries.Add(entry);

		entry.FromHotkey = "shift+ctrl+a";
		entry.CommitFromHotkey();
		CallRefreshRegistrations(controller);

		Assert.Equal("Shortcut to listen for now reads CTRL+SHIFT+A.", entry.FromCommitAnnouncement);
	}

	// ----- SyncSettings -----

	[Fact]
	public void SyncSettings_AllEntriesInvalid_PreservesExistingMappings()
	{
		// Regression: a transient invalid validation state during startup must not wipe persisted settings.
		var (controller, settings, _, entries, _) = BuildController();
		settings.HotKeyRouterSettings.Mappings.Add(new HotKeyRouterSettings.HotKeyRouterMap("CTRL+C", "CTRL+V"));

		// Add an entry whose From/To are invalid (cannot parse)
		entries.Add(new HotkeyRouterEntry(new HotKeyRouterSettings.HotKeyRouterMap(string.Empty, string.Empty)));

		var result = CallSyncSettings(controller);

		Assert.Single(result);
		Assert.Equal("CTRL+C", result[0].From);
		Assert.Equal("CTRL+V", result[0].To);
		// The persisted settings collection must remain populated.
		Assert.Single(settings.HotKeyRouterSettings.Mappings);
		Assert.Equal("CTRL+C", settings.HotKeyRouterSettings.Mappings[0].FromHotKey);
	}

	[Fact]
	public void SyncSettings_ValidEntries_UpdatesSettingsMappings()
	{
		var (controller, settings, _, entries, _) = BuildController();
		entries.Add(new HotkeyRouterEntry(new HotKeyRouterSettings.HotKeyRouterMap("Ctrl+C", "Ctrl+V")));
		entries.Add(new HotkeyRouterEntry(new HotKeyRouterSettings.HotKeyRouterMap("Ctrl+X", "Ctrl+Y")));

		var result = CallSyncSettings(controller);

		Assert.Equal(2, result.Count);
		Assert.Equal("CTRL+C", result[0].From);
		Assert.Equal("CTRL+V", result[0].To);
		Assert.Equal("CTRL+X", result[1].From);
		Assert.Equal("CTRL+Y", result[1].To);
		Assert.Equal(2, settings.HotKeyRouterSettings.Mappings.Count);
	}

	[Fact]
	public void SyncSettings_OnlyValidEntries_DiscardsInvalidOnes()
	{
		var (controller, settings, _, entries, _) = BuildController();
		entries.Add(new HotkeyRouterEntry(new HotKeyRouterSettings.HotKeyRouterMap("Ctrl+C", "Ctrl+V")));
		entries.Add(new HotkeyRouterEntry(new HotKeyRouterSettings.HotKeyRouterMap("Ctrl+", "")));

		var result = CallSyncSettings(controller);

		Assert.Single(result);
		Assert.Equal("CTRL+C", result[0].From);
		Assert.Single(settings.HotKeyRouterSettings.Mappings);
	}

	// ----- ShouldPersist -----

	[Fact]
	public void ShouldPersist_NotInitialized_ReturnsFalse()
	{
		var (controller, _, _, _, _) = BuildController(initialized: false);
		var pairs = new List<(string From, string To)> { ("A", "B") };

		Assert.False(CallShouldPersist(controller, pairs));
	}

	[Fact]
	public void ShouldPersist_InitializedAndCountDiffers_ReturnsTrue()
	{
		var (controller, _, _, _, snapshot) = BuildController();
		snapshot.Add(("CTRL+C", "CTRL+V"));

		var pairs = new List<(string From, string To)>
		{
			("CTRL+C", "CTRL+V"),
			("CTRL+X", "CTRL+Y"),
		};

		Assert.True(CallShouldPersist(controller, pairs));
	}

	[Fact]
	public void ShouldPersist_InitializedAndPairsIdentical_ReturnsFalse()
	{
		var (controller, _, _, _, snapshot) = BuildController();
		snapshot.Add(("CTRL+C", "CTRL+V"));
		snapshot.Add(("CTRL+X", "CTRL+Y"));

		var pairs = new List<(string From, string To)>
		{
			("CTRL+C", "CTRL+V"),
			("CTRL+X", "CTRL+Y"),
		};

		Assert.False(CallShouldPersist(controller, pairs));
	}

	[Fact]
	public void ShouldPersist_InitializedAndOnePairChanged_ReturnsTrue()
	{
		var (controller, _, _, _, snapshot) = BuildController();
		snapshot.Add(("CTRL+C", "CTRL+V"));

		var pairs = new List<(string From, string To)>
		{
			("CTRL+C", "CTRL+B"),
		};

		Assert.True(CallShouldPersist(controller, pairs));
	}

	[Fact]
	public void ShouldPersist_OrdinalCaseSensitive()
	{
		var (controller, _, _, _, snapshot) = BuildController();
		snapshot.Add(("CTRL+C", "CTRL+V"));

		var pairs = new List<(string From, string To)>
		{
			("ctrl+c", "ctrl+v"),
		};

		Assert.True(CallShouldPersist(controller, pairs));
	}

	// ----- RecalculateDuplicates -----

	[Fact]
	public void RecalculateDuplicates_FlagsCollidingFromHotkeys()
	{
		var (controller, _, _, entries, _) = BuildController();
		var first = new HotkeyRouterEntry(new HotKeyRouterSettings.HotKeyRouterMap("Ctrl+C", "Ctrl+V"));
		var second = new HotkeyRouterEntry(new HotKeyRouterSettings.HotKeyRouterMap("Ctrl+C", "Ctrl+B"));
		entries.Add(first);
		entries.Add(second);

		CallRecalculateDuplicates(controller);

		Assert.True(first.IsDuplicate);
		Assert.True(second.IsDuplicate);
	}

	[Fact]
	public void RecalculateDuplicates_DoesNotFlagDistinctFromHotkeys()
	{
		var (controller, _, _, entries, _) = BuildController();
		var first = new HotkeyRouterEntry(new HotKeyRouterSettings.HotKeyRouterMap("Ctrl+C", "Ctrl+V"));
		var second = new HotkeyRouterEntry(new HotKeyRouterSettings.HotKeyRouterMap("Ctrl+X", "Ctrl+Y"));
		entries.Add(first);
		entries.Add(second);

		CallRecalculateDuplicates(controller);

		Assert.False(first.IsDuplicate);
		Assert.False(second.IsDuplicate);
	}

	[Fact]
	public void RecalculateDuplicates_ClearsDuplicateFlagWhenCollisionRemoved()
	{
		var (controller, _, _, entries, _) = BuildController();
		var first = new HotkeyRouterEntry(new HotKeyRouterSettings.HotKeyRouterMap("Ctrl+C", "Ctrl+V"));
		var second = new HotkeyRouterEntry(new HotKeyRouterSettings.HotKeyRouterMap("Ctrl+C", "Ctrl+B"));
		entries.Add(first);
		entries.Add(second);

		CallRecalculateDuplicates(controller);
		Assert.True(first.IsDuplicate);

		entries.Remove(second);
		CallRecalculateDuplicates(controller);

		Assert.False(first.IsDuplicate);
	}

	// ----- Handing the check to an owner that sees more lists (issue #321) -----

	[Fact]
	public void RecalculateDuplicates_WithAWiderCheckAvailable_LeavesTheAnswerToIt()
	{
		// The router only sees its own routes. The Hotkeys page also holds the core hotkey
		// rows, and a route can be taken by one of those just as easily as by another route,
		// so on that page the router must not answer for itself.
		var (controller, _, _, entries, _) = BuildController();
		var first = new HotkeyRouterEntry(new HotKeyRouterSettings.HotKeyRouterMap("Ctrl+C", "Ctrl+V"));
		var second = new HotkeyRouterEntry(new HotKeyRouterSettings.HotKeyRouterMap("Ctrl+C", "Ctrl+B"));
		entries.Add(first);
		entries.Add(second);
		int widerChecks = 0;
		SetField(controller, "_crossListDuplicateCheck", (Action)(() => widerChecks++));

		CallRecalculateDuplicates(controller);

		Assert.Equal(1, widerChecks);
		Assert.False(first.IsDuplicate);
		Assert.False(second.IsDuplicate);
	}

	[Fact]
	public void ConfiguredFromHotkeys_OffersTheChordEachRouteClaims()
	{
		var (controller, _, _, entries, _) = BuildController();
		entries.Add(new HotkeyRouterEntry(new HotKeyRouterSettings.HotKeyRouterMap("shift+ctrl+a", "Ctrl+V")));
		// Half-typed text cannot be registered, so it is offered as nothing rather than as a
		// row that might clash.
		entries.Add(new HotkeyRouterEntry(new HotKeyRouterSettings.HotKeyRouterMap("Ctrl+", "Ctrl+B")));

		var configured = controller.ConfiguredFromHotkeys();

		Assert.Equal(2, configured.Count);
		Assert.Equal("CTRL+SHIFT+A", configured[0].Text);
		Assert.True(configured[0].ClaimsTheChord);
		Assert.Null(configured[1].Text);
	}

	[Fact]
	public void ApplyDuplicates_FlagsExactlyTheRowsItIsGiven()
	{
		var (controller, _, _, entries, _) = BuildController();
		var first = new HotkeyRouterEntry(new HotKeyRouterSettings.HotKeyRouterMap("Ctrl+C", "Ctrl+V"));
		var second = new HotkeyRouterEntry(new HotKeyRouterSettings.HotKeyRouterMap("Ctrl+X", "Ctrl+B"));
		entries.Add(first);
		entries.Add(second);

		controller.ApplyDuplicates(new HashSet<int> { 1 });

		Assert.False(first.IsDuplicate);
		Assert.True(second.IsDuplicate);

		controller.ApplyDuplicates(new HashSet<int>());

		Assert.False(second.IsDuplicate);
	}

	// ----- autoPersist=false (settings page editor mode) -----

	[Fact]
	public void RefreshRegistrations_AutoPersistFalse_DoesNotInvokeSettingsManager()
	{
		// The manager is injected, so a save would land on it: the zero proves the
		// _autoPersist gate held, not that the field happened to be null.
		var (controller, _, settingsManager, entries, _) = BuildController(autoPersist: false);
		entries.Add(new HotkeyRouterEntry(new HotKeyRouterSettings.HotKeyRouterMap("Ctrl+C", "Ctrl+V")));

		CallRefreshRegistrations(controller);

		Assert.Equal(0, settingsManager.SaveCount);
	}

	[Fact]
	public void RefreshRegistrations_AutoPersistFalse_WithNoSettingsManager_DoesNotThrow()
	{
		// The settings page's real shape: autoPersist off and settingsManager null.
		var (controller, _, _, entries, _) =
			BuildController(autoPersist: false, injectSettingsManager: false);
		entries.Add(new HotkeyRouterEntry(new HotKeyRouterSettings.HotKeyRouterMap("Ctrl+C", "Ctrl+V")));

		CallRefreshRegistrations(controller);
	}

	[Fact]
	public void RefreshRegistrations_AutoPersistTrue_PersistsWhenSnapshotChanges()
	{
		var (controller, _, settingsManager, entries, _) = BuildController(autoPersist: true);
		entries.Add(new HotkeyRouterEntry(new HotKeyRouterSettings.HotKeyRouterMap("Ctrl+C", "Ctrl+V")));

		CallRefreshRegistrations(controller);

		Assert.Equal(1, settingsManager.SaveCount);
	}

	private sealed class FakeSettingsManager : ISettingsManager
	{
		public int SaveCount { get; private set; }
		public Settings? LastSaved { get; private set; }

		public string SettingsFilePath => "fake.json";

		public void SaveSettingsToFile(Settings settings)
		{
			SaveCount++;
			LastSaved = settings;
		}

		public void UpgradeSettings()
		{
		}

		public Settings LoadAndEnsureSettings() => new();
	}
}
