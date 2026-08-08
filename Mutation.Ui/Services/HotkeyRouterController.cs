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

	private bool _initialized;
	private HotkeyManager? _hotkeyManager;

	public HotkeyRouterController(
		ObservableCollection<HotkeyRouterEntry> entries,
		Settings settings,
		ISettingsManager? settingsManager,
		DispatcherQueue dispatcherQueue,
		ListView routerListView,
		bool autoPersist = true)
	{
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
		_initialized = true;
	}

	public void AttachHotkeyManager(HotkeyManager hotkeyManager)
	{
		_hotkeyManager = hotkeyManager ?? throw new ArgumentNullException(nameof(hotkeyManager));
		RefreshRegistrations();
	}

	public void AddNewMapping()
	{
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

		if (_settings.HotKeyRouterSettings is not null)
			_settings.HotKeyRouterSettings.Mappings.Remove(entry.Map);

		DetachEntry(entry);
		_entries.Remove(entry);
		RefreshRegistrations();
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

	/// <summary>
	/// Takes down the notice left by the last time this box tidied itself up, on the way back
	/// into it (issue #332).
	/// </summary>
	public void ClearFromAnnouncement(object sender)
	{
		if (sender is FrameworkElement { DataContext: HotkeyRouterEntry entry })
			entry.ClearFromCommitAnnouncement();
	}

	/// <summary>The same, for the "To" box.</summary>
	public void ClearToAnnouncement(object sender)
	{
		if (sender is FrameworkElement { DataContext: HotkeyRouterEntry entry })
			entry.ClearToCommitAnnouncement();
	}

	public List<(string From, string To)> SyncSettings()
	{
		_settings.HotKeyRouterSettings ??= new HotKeyRouterSettings();

		// Silently: this runs on every commit and on save, over every row. The row the user
		// just left has already said what it rewrote, and re-committing it here would only
		// wipe that notice; the rows they never touched have nothing to report at all.
		foreach (var entry in _entries)
			entry.CommitSilently();

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

	private void RefreshRegistrations()
	{
		_settings.HotKeyRouterSettings ??= new HotKeyRouterSettings();

		RecalculateDuplicates();
		var normalizedPairs = SyncSettings();

		if (_hotkeyManager is null)
		{
			foreach (var entry in _entries)
				entry.SetBindingResult(HotkeyBindingState.Inactive, null);

			if (_autoPersist && ShouldPersist(normalizedPairs))
			{
				_settingsManager!.SaveSettingsToFile(_settings);
				UpdateSnapshot(normalizedPairs);
			}
			return;
		}

		var mappings = _settings.HotKeyRouterSettings.Mappings;
		var results = _hotkeyManager.RefreshRouterHotkeys(mappings);
		var resultLookup = results.ToDictionary(r => r.Map);

		foreach (var entry in _entries)
		{
			if (resultLookup.TryGetValue(entry.Map, out var result))
				entry.SetBindingResult(result.Success ? HotkeyBindingState.Bound : HotkeyBindingState.Failed, result.ErrorMessage);
			else
				entry.SetBindingResult(HotkeyBindingState.Inactive, null);
		}

		if (_autoPersist && ShouldPersist(normalizedPairs))
		{
			_settingsManager!.SaveSettingsToFile(_settings);
			UpdateSnapshot(normalizedPairs);
		}
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
	/// The router's "From" chords as the conflict finder wants them. Handed out so a host that
	/// shows more than one hotkey list can check them all against each other in one go — the
	/// registration table is shared, so a route and a core hotkey do compete (issue #321).
	/// </summary>
	internal IReadOnlyList<HotkeyConflictFinder.ConfiguredHotkey> ConfiguredFromChords()
	{
		var configured = new List<HotkeyConflictFinder.ConfiguredHotkey>(_entries.Count);
		foreach (var entry in _entries)
			configured.Add(new(entry.IsFromValid ? entry.NormalizedFromHotkey : null, ClaimsTheChord: true));
		return configured;
	}

	/// <summary>Flags the routes at <paramref name="duplicates"/> and clears the rest.</summary>
	internal void ApplyDuplicates(IReadOnlySet<int> duplicates)
	{
		for (int i = 0; i < _entries.Count; i++)
			_entries[i].SetDuplicate(duplicates.Contains(i));
	}

	/// <summary>
	/// Set by a host that owns other hotkey lists on the same screen, to check every list at
	/// once instead of this one alone (issue #321). It is expected to call back into
	/// <see cref="ConfiguredFromChords"/> and <see cref="ApplyDuplicates"/>.
	/// </summary>
	internal Action? CheckDuplicatesAcrossLists { get; set; }

	/// <summary>
	/// Flags the routes whose "from" shortcut is already taken by another route. Compared as
	/// chords rather than as text, so the answer is the one registration will give — the
	/// screen used to compare the typed strings and could wave through a pair the hotkey
	/// table then refused (issue #306).
	/// <para>
	/// This is the narrow answer, over the router list alone. A host that has set
	/// <see cref="CheckDuplicatesAcrossLists"/> gets the wide one instead, because a route can
	/// just as easily collide with a hotkey on another list.
	/// </para>
	/// </summary>
	private void RecalculateDuplicates()
	{
		if (CheckDuplicatesAcrossLists is { } acrossLists)
		{
			acrossLists();
			return;
		}

		ApplyDuplicates(HotkeyConflictFinder.DuplicateIndexes(ConfiguredFromChords()));
	}

	private void AttachEntry(HotkeyRouterEntry entry) =>
		entry.PropertyChanged += Entry_PropertyChanged;

	private void DetachEntry(HotkeyRouterEntry entry) =>
		entry.PropertyChanged -= Entry_PropertyChanged;

	private void Entry_PropertyChanged(object? sender, PropertyChangedEventArgs e)
	{
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
