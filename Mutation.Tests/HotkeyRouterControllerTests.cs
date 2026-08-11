using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Reflection;
using System.Runtime.CompilerServices;
using CognitiveSupport;
using Mutation.Ui;
using Mutation.Ui.Core;
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

	private static void CallAttachEntry(HotkeyRouterController controller, HotkeyRouterEntry entry)
		=> typeof(HotkeyRouterController)
			.GetMethod("AttachEntry", BindingFlags.NonPublic | BindingFlags.Instance)!
			.Invoke(controller, new object[] { entry });

	// The gate has to be wired the way the constructor wires it, or an unmute never reaches the
	// rows. Injected by hand here because the constructor needs a ListView and a DispatcherQueue.
	private static FirstTouchGate InjectAnnouncementGate(HotkeyRouterController controller)
	{
		var gate = new FirstTouchGate();
		SetField(controller, "_announcementGate", gate);
		var unmute = (EventHandler)typeof(HotkeyRouterController)
			.GetMethod("UnmuteAnnouncements", BindingFlags.NonPublic | BindingFlags.Instance)!
			.CreateDelegate(typeof(EventHandler), controller);
		gate.Touched += unmute;
		return gate;
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

	// ----- The rewrite notice surviving the refresh that follows it (issue #332) -----

	[Fact]
	public void RefreshRegistrations_LeavesTheNoticeFromTheCommitThatTriggeredItStanding()
	{
		// The real sequence when the user tabs out of a "From" box: the box commits and
		// announces, and the controller then refreshes — which re-commits every row, including
		// this one. That second pass finds nothing left to change, and an announcing one would
		// take the notice back down again before it had been spoken.
		var (controller, _, _, entries, _) = BuildController(autoPersist: false, injectSettingsManager: false);
		var entry = new HotkeyRouterEntry(new HotKeyRouterSettings.HotKeyRouterMap("Ctrl+C", "Ctrl+V"));
		entries.Add(entry);

		entry.FromHotkey = "shift+ctrl+a";
		entry.CommitFromHotkey();
		CallRefreshRegistrations(controller);

		Assert.Equal("Shortcut to listen for now reads CTRL+SHIFT+A.", entry.FromCommitAnnouncement);
	}

	[Fact]
	public void RefreshRegistrations_DoesNotInventANoticeForRowsTheUserDidNotTouch()
	{
		// The bookkeeping commit runs over every row. A row loaded from a settings file that
		// needs tidying up would otherwise announce itself the first time anything on the page
		// changed.
		var (controller, _, _, entries, _) = BuildController(autoPersist: false, injectSettingsManager: false);
		var untouched = new HotkeyRouterEntry(new HotKeyRouterSettings.HotKeyRouterMap("Ctrl+C", "Ctrl+V"));
		entries.Add(untouched);

		CallRefreshRegistrations(controller);

		Assert.Null(untouched.FromCommitAnnouncement);
		Assert.Null(untouched.ToCommitAnnouncement);
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

	// ----- What each row says about being live (issue #343) -----

	[Fact]
	public void RefreshRegistrations_WithARouteTheAppHolds_TellsThatRowItIsLive()
	{
		// The page used to report every row as "not currently bound", working ones included,
		// because nothing ever handed the controller a way to find out.
		var (controller, _, _, entries, _) = BuildController(autoPersist: false, injectSettingsManager: false);
		var live = new HotkeyRouterEntry(new HotKeyRouterSettings.HotKeyRouterMap("Ctrl+Alt+1", "Ctrl+Shift+M"));
		var pending = new HotkeyRouterEntry(new HotKeyRouterSettings.HotKeyRouterMap("Ctrl+Alt+2", "Ctrl+Shift+N"));
		entries.Add(live);
		entries.Add(pending);
		SetField(controller, "_liveRoutes", (Func<IReadOnlyList<RegisteredRouterRoute>?>)(() =>
			new[] { new RegisteredRouterRoute("CTRL+ALT+1", "CTRL+SHIFT+M", true, null) }));

		CallRefreshRegistrations(controller);

		Assert.Equal(HotkeyBindingState.Bound, live.BindingState);
		Assert.Equal(HotkeyBindingState.NotYetApplied, pending.BindingState);
	}

	[Fact]
	public void RefreshRegistrations_WithNoWayToAsk_LeavesEveryRowSayingNothing()
	{
		// The shape every other caller has. Nothing is known, so no row claims anything.
		var (controller, _, _, entries, _) = BuildController(autoPersist: false, injectSettingsManager: false);
		var entry = new HotkeyRouterEntry(new HotKeyRouterSettings.HotKeyRouterMap("Ctrl+Alt+1", "Ctrl+Shift+M"));
		entries.Add(entry);

		CallRefreshRegistrations(controller);

		Assert.Equal(HotkeyBindingState.Unknown, entry.BindingState);
		Assert.Null(entry.RouteStatusText);
	}

	[Fact]
	public void RefreshRegistrations_AHalfTypedRow_IsNotAskedAboutAtAll()
	{
		// It cannot be a route, and its own "Enter a hotkey." already says what is wrong.
		var (controller, _, _, entries, _) = BuildController(autoPersist: false, injectSettingsManager: false);
		var entry = new HotkeyRouterEntry(new HotKeyRouterSettings.HotKeyRouterMap("Ctrl+", string.Empty));
		entries.Add(entry);
		SetField(controller, "_liveRoutes", (Func<IReadOnlyList<RegisteredRouterRoute>?>)(() =>
			Array.Empty<RegisteredRouterRoute>()));

		CallRefreshRegistrations(controller);

		Assert.Equal(HotkeyBindingState.Unknown, entry.BindingState);
	}

	[Fact]
	public void RefreshRegistrations_ARouteThatFailedToRegister_CarriesTheReasonOntoTheRow()
	{
		var (controller, _, _, entries, _) = BuildController(autoPersist: false, injectSettingsManager: false);
		var entry = new HotkeyRouterEntry(new HotKeyRouterSettings.HotKeyRouterMap("Ctrl+Alt+1", "Ctrl+Shift+M"));
		entries.Add(entry);
		SetField(controller, "_liveRoutes", (Func<IReadOnlyList<RegisteredRouterRoute>?>)(() =>
			new[] { new RegisteredRouterRoute("CTRL+ALT+1", "CTRL+SHIFT+M", false, "The shortcut is already registered by another application.") }));

		CallRefreshRegistrations(controller);

		Assert.Equal(HotkeyBindingState.Failed, entry.BindingState);
		Assert.Equal("The shortcut is already registered by another application.", entry.BindingErrorMessage);
	}

	// ----- Showing an error without reading it out (issue #350) -----

	[Fact]
	public void ARowBuiltWhileThePageIsUntouched_IsMutedBeforeItReachesTheScreen()
	{
		// The row already carries "Enter a hotkey." from its own constructor, so the mute has to
		// be on before the binding runs or the page announces on the way in.
		var (controller, _, _, entries, _) = BuildController(autoPersist: false, injectSettingsManager: false);
		InjectAnnouncementGate(controller);
		var entry = new HotkeyRouterEntry(new HotKeyRouterSettings.HotKeyRouterMap(string.Empty, string.Empty));

		CallAttachEntry(controller, entry);
		entries.Add(entry);

		Assert.True(entry.AnnouncementsMuted);
		// And the written half is untouched, which is the whole point of gating only the sound.
		Assert.Equal("Enter a hotkey.", entry.BindingErrorMessage);
	}

	[Fact]
	public void OnceThePageHasBeenUsed_EveryRowIsFreeToSpeakIncludingUntouchedOnes()
	{
		// A duplicate can appear on a row because the user edited a different row, or a core
		// hotkey higher up the page. That row was never touched and it is exactly the news worth
		// interrupting for, which is why the gate is one answer for the page.
		var (controller, _, _, entries, _) = BuildController(autoPersist: false, injectSettingsManager: false);
		var gate = InjectAnnouncementGate(controller);
		var edited = new HotkeyRouterEntry(new HotKeyRouterSettings.HotKeyRouterMap("Ctrl+Alt+1", "Ctrl+Shift+M"));
		var untouched = new HotkeyRouterEntry(new HotKeyRouterSettings.HotKeyRouterMap("Ctrl+Alt+2", "Ctrl+Shift+N"));
		foreach (var entry in new[] { edited, untouched })
		{
			CallAttachEntry(controller, entry);
			entries.Add(entry);
		}
		Assert.True(untouched.AnnouncementsMuted);

		edited.FromHotkey = "Ctrl+Alt+3";

		Assert.True(gate.HasBeenTouched);
		Assert.False(edited.AnnouncementsMuted);
		Assert.False(untouched.AnnouncementsMuted);
	}

	[Fact]
	public void ABookkeepingCommit_DoesNotCountAsTheUserUsingThePage()
	{
		// The refresh re-commits every row. If that opened the gate, loading a settings file
		// that needed tidying would announce the page's errors without anyone touching it.
		var (controller, _, _, entries, _) = BuildController(autoPersist: false, injectSettingsManager: false);
		var gate = InjectAnnouncementGate(controller);
		var entry = new HotkeyRouterEntry(new HotKeyRouterSettings.HotKeyRouterMap("Ctrl+Alt+1", "Ctrl+Shift+M"));
		CallAttachEntry(controller, entry);
		entries.Add(entry);

		CallRefreshRegistrations(controller);

		Assert.False(gate.HasBeenTouched);
		Assert.True(entry.AnnouncementsMuted);
	}

	[Fact]
	public void CeasingToBeADuplicate_GetsTheRowItsStatusBack()
	{
		// ApplyDuplicates is reached without a refresh when a *core* hotkey commits: the Hotkeys
		// page recomputes across all its lists and stops there. A live route flagged because a
		// core hotkey took its chord goes to Unknown, and nothing about unflagging it restored
		// what it was — so it stayed silent about being live until a router box lost focus or the
		// page was rebuilt (issue #343).
		var (controller, _, _, entries, _) = BuildController(autoPersist: false, injectSettingsManager: false);
		var entry = new HotkeyRouterEntry(new HotKeyRouterSettings.HotKeyRouterMap("Ctrl+Alt+1", "Ctrl+Shift+M"));
		entries.Add(entry);
		SetField(controller, "_liveRoutes", (Func<IReadOnlyList<RegisteredRouterRoute>?>)(() =>
			new[] { new RegisteredRouterRoute("CTRL+ALT+1", "CTRL+SHIFT+M", true, null) }));
		CallRefreshRegistrations(controller);
		Assert.Equal(HotkeyBindingState.Bound, entry.BindingState);

		controller.ApplyDuplicates(new HashSet<int> { 0 });
		Assert.Equal(HotkeyBindingState.Unknown, entry.BindingState);

		controller.ApplyDuplicates(new HashSet<int>());

		Assert.Equal(HotkeyBindingState.Bound, entry.BindingState);
	}

	[Fact]
	public void ARowsTextChangingIsOnlyTheUserWhenItIsNotBookkeeping()
	{
		// A row's text does not change only when the user types: a commit that rewrites a chord
		// into the app's canonical spelling raises the same notification, and SyncSettings commits
		// every row on every refresh. Today those passes happen to find nothing to rewrite, but
		// relying on that would mean a reordering could open the gate at page load with nothing
		// failing. This pins the guard rather than the accident (issue #350).
		var (controller, _, _, _, _) = BuildController(autoPersist: false, injectSettingsManager: false);
		var gate = InjectAnnouncementGate(controller);

		SetField(controller, "_inBookkeepingCommit", true);
		CallEntryPropertyChanged(controller, nameof(HotkeyRouterEntry.FromHotkey));
		Assert.False(gate.HasBeenTouched);

		SetField(controller, "_inBookkeepingCommit", false);
		CallEntryPropertyChanged(controller, nameof(HotkeyRouterEntry.ToHotkey));
		Assert.True(gate.HasBeenTouched);
	}

	private static void CallEntryPropertyChanged(HotkeyRouterController controller, string propertyName)
		=> typeof(HotkeyRouterController)
			.GetMethod("Entry_PropertyChanged", BindingFlags.NonPublic | BindingFlags.Instance)!
			.Invoke(controller, new object?[] { null, new PropertyChangedEventArgs(propertyName) });

	[Fact]
	public void WithNoGateAtAll_NoRowIsEverMuted()
	{
		// Every other user of this controller behaves exactly as it did before.
		var (controller, _, _, entries, _) = BuildController(autoPersist: false, injectSettingsManager: false);
		var entry = new HotkeyRouterEntry(new HotKeyRouterSettings.HotKeyRouterMap(string.Empty, string.Empty));

		CallAttachEntry(controller, entry);
		entries.Add(entry);

		Assert.False(entry.AnnouncementsMuted);
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
