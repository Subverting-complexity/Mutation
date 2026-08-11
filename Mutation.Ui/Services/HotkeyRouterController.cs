using CognitiveSupport;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Mutation.Ui;
using Mutation.Ui.Core;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Threading.Tasks;

namespace Mutation.Ui.Services;

internal sealed class HotkeyRouterController
{
	private readonly Settings _settings;
	private readonly ISettingsManager? _settingsManager;
	private readonly DispatcherQueue _dispatcherQueue;
	private readonly ListView _routerListView;
	private readonly ObservableCollection<HotkeyRouterEntry> _entries;
	private readonly List<(string From, string To)> _persistedSnapshot = new();
	private readonly bool _autoPersist;
	private readonly Action? _crossListDuplicateCheck;
	private readonly Func<IReadOnlyList<RegisteredRouterRoute>?>? _liveRoutes;
	private readonly FirstTouchGate? _announcementGate;

	private bool _initialized;

	// Set only while SyncSettings is re-committing every row. See Entry_PropertyChanged.
	private bool _inBookkeepingCommit;

	/// <param name="announcementGate">
	/// Whether the page holding these rows has been used yet. Until it has, a row's error is on
	/// screen but not read out, because rows built from settings can arrive already carrying one
	/// (issue #350). Null leaves every row free to speak, which is what it did before.
	/// </param>
	/// <param name="liveRoutes">
	/// The routes the running app actually holds, asked freshly each time the rows are
	/// refreshed. Read-only: it reports what registration did, it does not cause any. That
	/// matters on the Settings page, which edits a copy of the settings — registering from the
	/// copy would claim chords the user might still cancel out of.
	/// <para>
	/// Null means there is no way to ask, and every row then says nothing about being live
	/// rather than guessing. The old shape took a <see cref="HotkeyManager"/> here, was never
	/// given one by anybody, and so left every row reporting "not currently bound" including
	/// the ones that worked (issue #343).
	/// </para>
	/// </param>
	/// <param name="crossListDuplicateCheck">
	/// Runs the duplicate check on an owner that has more lists to compare than this one — the
	/// Hotkeys page, which holds the core hotkey rows as well. Null leaves the router checking
	/// its own "From" chords against each other, which is all it can see by itself.
	/// <para>
	/// The owner is expected to call back into <see cref="ConfiguredFromHotkeys"/> and
	/// <see cref="ApplyDuplicates"/>: this list still owns its own rows, it just no longer
	/// decides on its own which of them clash (issue #321).
	/// </para>
	/// </param>
	public HotkeyRouterController(
		ObservableCollection<HotkeyRouterEntry> entries,
		Settings settings,
		ISettingsManager? settingsManager,
		DispatcherQueue dispatcherQueue,
		ListView routerListView,
		bool autoPersist = true,
		Action? crossListDuplicateCheck = null,
		Func<IReadOnlyList<RegisteredRouterRoute>?>? liveRoutes = null,
		FirstTouchGate? announcementGate = null)
	{
		_crossListDuplicateCheck = crossListDuplicateCheck;
		_liveRoutes = liveRoutes;
		_announcementGate = announcementGate;
		if (_announcementGate is not null)
			_announcementGate.Touched += UnmuteAnnouncements;
		_entries = entries ?? throw new ArgumentNullException(nameof(entries));
		_settings = settings ?? throw new ArgumentNullException(nameof(settings));
		_dispatcherQueue = dispatcherQueue ?? throw new ArgumentNullException(nameof(dispatcherQueue));
		_routerListView = routerListView ?? throw new ArgumentNullException(nameof(routerListView));
		_autoPersist = autoPersist;
		if (autoPersist && settingsManager is null)
			throw new ArgumentNullException(nameof(settingsManager), "settingsManager is required when autoPersist is true.");
		_settingsManager = settingsManager;
	}

	public ObservableCollection<HotkeyRouterEntry> Entries => _entries;

	public void Initialize()
	{
		_initialized = false;

		_settings.HotKeyRouterSettings ??= new HotKeyRouterSettings();

		foreach (var entry in _entries)
			DetachEntry(entry);
		_entries.Clear();
		foreach (var map in _settings.HotKeyRouterSettings.Mappings)
		{
			var entry = new HotkeyRouterEntry(map);
			AttachEntry(entry);
			_entries.Add(entry);
		}

		var initialPairs = _entries
			.Where(e => e.IsValid && e.NormalizedFromHotkey is not null && e.NormalizedToHotkey is not null)
			.Select(e => (From: e.NormalizedFromHotkey!, To: e.NormalizedToHotkey!))
			.ToList();
		UpdateSnapshot(initialPairs);

		RecalculateDuplicates();

		// Before the page is shown, so a stored route already says whether it is live rather
		// than waiting for the user to touch something first.
		ApplyBindingStates();

		_initialized = true;
	}

	public void AddNewMapping()
	{
		// Before the row is built, so the row is free to speak from the moment it exists. Being
		// told the empty row wants a shortcut is the answer to the button the user just pressed
		// — this is the one case where announcing an untouched row's error is right (issue #350).
		_announcementGate?.Touch();

		_settings.HotKeyRouterSettings ??= new HotKeyRouterSettings();

		var map = new HotKeyRouterSettings.HotKeyRouterMap(string.Empty, string.Empty);

		var entry = new HotkeyRouterEntry(map);
		AttachEntry(entry);
		_entries.Add(entry);

		RefreshRegistrations();

		TryFocusFromTextBox(entry);
	}

	public void DeleteMapping(object sender)
	{
		if (sender is not FrameworkElement element || element.Tag is not HotkeyRouterEntry entry)
			return;

		// Deleting a mapping can leave a duplicate behind on another row, and that row is now
		// worth hearing about even though the user never edited it.
		_announcementGate?.Touch();

		if (_settings.HotKeyRouterSettings is not null)
			_settings.HotKeyRouterSettings.Mappings.Remove(entry.Map);

		DetachEntry(entry);
		_entries.Remove(entry);
		RefreshRegistrations();
	}

	/// <summary>
	/// Takes down the "From" box's rewrite notice as the user comes back into it. See
	/// <see cref="HotkeyRouterEntry.ClearFromCommitAnnouncement"/> for why it does not simply
	/// stay put — and note that entering one box leaves the other box's notice alone, because
	/// tabbing from "From" to "To" is how a mapping is filled in and would otherwise wipe the
	/// first notice before it had been read out.
	/// </summary>
	public void ClearFromCommitAnnouncement(object sender)
	{
		if (sender is FrameworkElement { DataContext: HotkeyRouterEntry entry })
			entry.ClearFromCommitAnnouncement();
	}

	/// <summary>The same, for the "To" box.</summary>
	public void ClearToCommitAnnouncement(object sender)
	{
		if (sender is FrameworkElement { DataContext: HotkeyRouterEntry entry })
			entry.ClearToCommitAnnouncement();
	}

	public void CommitFromLostFocus(object sender)
	{
		if (sender is FrameworkElement { DataContext: HotkeyRouterEntry entry })
		{
			entry.CommitFromHotkey();
			RefreshRegistrations();
		}
	}

	public void CommitToLostFocus(object sender)
	{
		if (sender is FrameworkElement { DataContext: HotkeyRouterEntry entry })
		{
			entry.CommitToHotkey();
			RefreshRegistrations();
		}
	}

	public List<(string From, string To)> SyncSettings()
	{
		_settings.HotKeyRouterSettings ??= new HotKeyRouterSettings();

		// Silently. This runs over every row on every refresh, including the one the user
		// just edited — which has already committed and announced. An announcing pass here
		// finds nothing left to change and would take that notice straight back down again
		// (issue #332).
		//
		// Flagged as bookkeeping while it runs, so a row this pass rewrites is not mistaken for a
		// row the user typed in and does not open the announcement gate (issue #350).
		_inBookkeepingCommit = true;
		try
		{
			foreach (var entry in _entries)
				entry.CommitSilently();
		}
		finally
		{
			_inBookkeepingCommit = false;
		}

		var validEntries = _entries
			.Where(e => e.IsValid && e.NormalizedFromHotkey is not null && e.NormalizedToHotkey is not null)
			.ToList();

		// If no entries are currently valid but existing settings contain mappings, preserve them.
		// Avoids wiping user settings due to a transient validation state during startup.
		if (validEntries.Count == 0 && _settings.HotKeyRouterSettings.Mappings.Count > 0)
		{
			return _settings.HotKeyRouterSettings.Mappings
				.Where(m => !string.IsNullOrWhiteSpace(m.FromHotKey) && !string.IsNullOrWhiteSpace(m.ToHotKey))
				.Select(m => (From: m.FromHotKey!, To: m.ToHotKey!))
				.ToList();
		}

		var normalizedPairs = validEntries
			.Select(e => (From: e.NormalizedFromHotkey!, To: e.NormalizedToHotkey!))
			.ToList();

		var existing = _settings.HotKeyRouterSettings.Mappings;

		bool changed = existing.Count != normalizedPairs.Count;
		if (!changed)
		{
			for (int i = 0; i < existing.Count; i++)
			{
				var existingFrom = existing[i].FromHotKey ?? string.Empty;
				var existingTo = existing[i].ToHotKey ?? string.Empty;

				if (!string.Equals(existingFrom, normalizedPairs[i].From, StringComparison.Ordinal) ||
					!string.Equals(existingTo, normalizedPairs[i].To, StringComparison.Ordinal))
				{
					changed = true;
					break;
				}
			}
		}

		if (changed)
		{
			var updatedMaps = normalizedPairs
				.Select(pair => new HotKeyRouterSettings.HotKeyRouterMap(pair.From, pair.To))
				.ToList();

			_settings.HotKeyRouterSettings.Mappings = updatedMaps;

			for (int i = 0; i < validEntries.Count; i++)
				validEntries[i].ReplaceBackingMap(updatedMaps[i]);
		}

		return normalizedPairs;
	}

	public void UpdateSnapshot(IEnumerable<(string From, string To)> normalizedPairs)
	{
		_persistedSnapshot.Clear();
		_persistedSnapshot.AddRange(normalizedPairs);
	}

	/// <summary>
	/// Settles every row after something on the page changed: which rows clash, what is written
	/// back into settings, and what each row now says about being live.
	/// <para>
	/// It registers nothing. It used to look as though it might — there was a branch that called
	/// <c>RefreshRouterHotkeys</c> — but nothing ever handed this controller a
	/// <see cref="HotkeyManager"/>, so that branch never ran, and had it run on the Settings page
	/// it would have claimed chords out of a settings copy the user could still cancel. The rows
	/// now read the outcome of the registration the app already did (issue #343).
	/// </para>
	/// </summary>
	private void RefreshRegistrations()
	{
		_settings.HotKeyRouterSettings ??= new HotKeyRouterSettings();

		RecalculateDuplicates();
		var normalizedPairs = SyncSettings();

		ApplyBindingStates();

		if (_autoPersist && ShouldPersist(normalizedPairs))
		{
			_settingsManager!.SaveSettingsToFile(_settings);
			UpdateSnapshot(normalizedPairs);
		}
	}

	/// <summary>
	/// Tells each row where it stands against the routes the app actually holds. Asked once per
	/// refresh rather than once per row, because the answer is the same list for all of them.
	/// </summary>
	private void ApplyBindingStates()
	{
		var live = _liveRoutes?.Invoke();

		foreach (var entry in _entries)
		{
			// Offered as nothing unless the row is a usable mapping. A half-typed row cannot be
			// a route, and saying "not active yet" underneath its own "Enter a hotkey." would be
			// a second line telling the user what the first already told them.
			var (state, message) = RouterBindingStatus.For(
				entry.IsValid ? entry.NormalizedFromHotkey : null,
				entry.IsValid ? entry.NormalizedToHotkey : null,
				live);

			entry.SetBindingResult(state, message);
		}
	}

	/// <summary>
	/// True while the page has not been used yet, so a row shows its error without reading it
	/// out. One answer for the whole list rather than one per row — see
	/// <see cref="FirstTouchGate"/> (issue #350). With no gate, nothing is ever muted.
	/// </summary>
	private bool AnnouncementsMuted => _announcementGate is not null && !_announcementGate.HasBeenTouched;

	private void UnmuteAnnouncements(object? sender, EventArgs e)
	{
		foreach (var entry in _entries)
			entry.SetAnnouncementsMuted(false);
	}

	private bool ShouldPersist(List<(string From, string To)> normalizedPairs)
	{
		if (!_initialized)
			return false;

		if (_persistedSnapshot.Count != normalizedPairs.Count)
			return true;

		for (int i = 0; i < normalizedPairs.Count; i++)
		{
			var previous = _persistedSnapshot[i];
			var current = normalizedPairs[i];

			if (!string.Equals(previous.From, current.From, StringComparison.Ordinal) ||
				!string.Equals(previous.To, current.To, StringComparison.Ordinal))
			{
				return true;
			}
		}

		return false;
	}

	/// <summary>
	/// The "From" chord of every route, in row order. Each one is claimed from Windows, and
	/// text that does not parse yet is offered as nothing at all — it cannot be registered, so
	/// it cannot clash.
	/// </summary>
	public IReadOnlyList<HotkeyConflictFinder.ConfiguredHotkey> ConfiguredFromHotkeys()
	{
		var configured = new List<HotkeyConflictFinder.ConfiguredHotkey>(_entries.Count);
		foreach (var entry in _entries)
			configured.Add(new(entry.IsFromValid ? entry.NormalizedFromHotkey : null, ClaimsTheChord: true));

		return configured;
	}

	/// <summary>
	/// Flags the rows at <paramref name="duplicateRows"/> and clears the rest. The positions
	/// are those of <see cref="ConfiguredFromHotkeys"/>, whoever worked them out.
	/// <para>
	/// Settling the binding states afterwards is not tidiness. A row that becomes a duplicate is
	/// put back to <see cref="HotkeyBindingState.Unknown"/>, because a chord that is already
	/// taken is not a route; nothing about ceasing to be a duplicate restores what it was. On the
	/// paths that come through <see cref="RefreshRegistrations"/> that is covered, because the
	/// binding states are settled last there. This is also called straight from the Hotkeys page
	/// when a <em>core</em> hotkey commits, and that path stops here — so a live route that was
	/// flagged because a core hotkey took its chord, and then unflagged when the user moved that
	/// core hotkey elsewhere, stayed permanently silent about being live (issue #343).
	/// </para>
	/// </summary>
	public void ApplyDuplicates(IReadOnlySet<int> duplicateRows)
	{
		if (duplicateRows is null) throw new ArgumentNullException(nameof(duplicateRows));

		for (int i = 0; i < _entries.Count; i++)
			_entries[i].SetDuplicate(duplicateRows.Contains(i));

		ApplyBindingStates();
	}

	/// <summary>
	/// Flags the routes whose "from" shortcut is already taken. Compared as chords rather than
	/// as text, so the answer is the one registration will give — the screen used to compare
	/// the typed strings and could wave through a pair the hotkey table then refused (issue
	/// #306).
	/// <para>
	/// Handed to the owner when it has more lists to compare than this one, because the hotkey
	/// table is one table for the whole app and a route can be taken by a core hotkey just as
	/// easily as by another route (issue #321).
	/// </para>
	/// </summary>
	private void RecalculateDuplicates()
	{
		if (_crossListDuplicateCheck is not null)
		{
			_crossListDuplicateCheck();
			return;
		}

		ApplyDuplicates(HotkeyConflictFinder.DuplicateIndexes(ConfiguredFromHotkeys()));
	}

	private void AttachEntry(HotkeyRouterEntry entry)
	{
		// Set before the row reaches the screen. A row that arrives already carrying an error
		// has to be muted by then or it announces on the way in, which is the whole complaint.
		entry.SetAnnouncementsMuted(AnnouncementsMuted);
		entry.PropertyChanged += Entry_PropertyChanged;
	}

	private void DetachEntry(HotkeyRouterEntry entry) =>
		entry.PropertyChanged -= Entry_PropertyChanged;

	private void Entry_PropertyChanged(object? sender, PropertyChangedEventArgs e)
	{
		// Typing in either box, which is what opens the page's mouth (issue #350).
		//
		// A row's text does not only change through the two-way binding: a commit that rewrites a
		// chord into the app's canonical spelling raises this too, and the bookkeeping pass in
		// SyncSettings commits every row. Today those passes find nothing to rewrite, because the
		// entry's constructor already canonicalised before this handler was subscribed — but that
		// is an accident of ordering, not a rule, and relying on it means a future change could
		// open the gate at page load with no test failing. Hence the explicit guard.
		if (!_inBookkeepingCommit &&
			(e.PropertyName == nameof(HotkeyRouterEntry.FromHotkey) ||
			e.PropertyName == nameof(HotkeyRouterEntry.ToHotkey)))
		{
			_announcementGate?.Touch();
		}

		if (e.PropertyName == nameof(HotkeyRouterEntry.FromHotkey) ||
			e.PropertyName == nameof(HotkeyRouterEntry.IsFromValid))
		{
			RecalculateDuplicates();
		}
	}

	private void TryFocusFromTextBox(HotkeyRouterEntry entry)
	{
		_dispatcherQueue.TryEnqueue(async () =>
		{
			for (int i = 0; i < 8; i++)
			{
				var container = _routerListView.ContainerFromItem(entry) as ListViewItem;
				if (container?.ContentTemplateRoot is FrameworkElement root)
				{
					var fromTextBox = FindDescendant<TextBox>(root);
					if (fromTextBox != null)
					{
						fromTextBox.Focus(FocusState.Programmatic);
						fromTextBox.SelectAll();
						return;
					}
				}
				await Task.Delay(40);
			}
		});
	}

	private static T? FindDescendant<T>(DependencyObject root) where T : class
	{
		int count = VisualTreeHelper.GetChildrenCount(root);
		for (int i = 0; i < count; i++)
		{
			var child = VisualTreeHelper.GetChild(root, i);
			if (child is T typed)
				return typed;
			var result = FindDescendant<T>(child);
			if (result != null)
				return result;
		}
		return null;
	}
}
