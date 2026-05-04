using CognitiveSupport;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Mutation.Ui;
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
	private readonly ISettingsManager _settingsManager;
	private readonly DispatcherQueue _dispatcherQueue;
	private readonly ListView _routerListView;
	private readonly ObservableCollection<HotkeyRouterEntry> _entries;
	private readonly List<(string From, string To)> _persistedSnapshot = new();

	private bool _initialized;
	private HotkeyManager? _hotkeyManager;

	public HotkeyRouterController(
		ObservableCollection<HotkeyRouterEntry> entries,
		Settings settings,
		ISettingsManager settingsManager,
		DispatcherQueue dispatcherQueue,
		ListView routerListView)
	{
		_entries = entries ?? throw new ArgumentNullException(nameof(entries));
		_settings = settings ?? throw new ArgumentNullException(nameof(settings));
		_settingsManager = settingsManager ?? throw new ArgumentNullException(nameof(settingsManager));
		_dispatcherQueue = dispatcherQueue ?? throw new ArgumentNullException(nameof(dispatcherQueue));
		_routerListView = routerListView ?? throw new ArgumentNullException(nameof(routerListView));
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

	public List<(string From, string To)> SyncSettings()
	{
		_settings.HotKeyRouterSettings ??= new HotKeyRouterSettings();

		foreach (var entry in _entries)
		{
			entry.CommitFromHotkey();
			entry.CommitToHotkey();
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

	private void RefreshRegistrations()
	{
		_settings.HotKeyRouterSettings ??= new HotKeyRouterSettings();

		RecalculateDuplicates();
		var normalizedPairs = SyncSettings();

		if (_hotkeyManager is null)
		{
			foreach (var entry in _entries)
				entry.SetBindingResult(HotkeyBindingState.Inactive, null);

			if (ShouldPersist(normalizedPairs))
			{
				_settingsManager.SaveSettingsToFile(_settings);
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

		if (ShouldPersist(normalizedPairs))
		{
			_settingsManager.SaveSettingsToFile(_settings);
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

	private void RecalculateDuplicates()
	{
		var duplicates = _entries
			.Where(e => e.IsFromValid && e.NormalizedFromHotkey is not null)
			.GroupBy(e => e.NormalizedFromHotkey!, StringComparer.OrdinalIgnoreCase)
			.Where(g => g.Count() > 1)
			.SelectMany(g => g);

		var duplicateSet = new HashSet<HotkeyRouterEntry>(duplicates);

		foreach (var entry in _entries)
			entry.SetDuplicate(duplicateSet.Contains(entry));
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
