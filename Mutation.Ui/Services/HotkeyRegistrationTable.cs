using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Threading;

namespace Mutation.Ui.Services;

/// <summary>
/// The bookkeeping behind global hotkeys: which shortcuts are already taken, which id
/// carries which callback, and which ids belong to a group that a refresh has to release
/// first.
/// <para>
/// Split out of <see cref="HotkeyManager"/> so it can be exercised without a window handle
/// and without asking Windows for chords the build machine may not have free. The failures
/// this guards against are silent ones — a router refresh that leaves its old ids
/// registered, or a prompt hotkey whose id still routes to the deleted prompt's callback —
/// and neither shows up as an exception.
/// </para>
/// </summary>
internal sealed class HotkeyRegistrationTable
{
	/// <summary>Which part of the app owns a registration, and therefore what a refresh clears.</summary>
	public enum HotkeyGroup
	{
		/// <summary>App shortcuts, replaced only by a full rebind.</summary>
		Core,

		/// <summary>Hotkey-router mappings, replaced whenever the router settings change.</summary>
		Router,

		/// <summary>Per-prompt shortcuts, replaced whenever the prompt library changes.</summary>
		Prompt,
	}

	/// <summary>The id handed back when nothing was registered.</summary>
	public const int NoId = -1;

	public readonly record struct RegistrationOutcome(
		string NormalizedHotkey,
		int Id,
		bool Success,
		string? ErrorMessage);

	private sealed record Entry(Action Callback, Hotkey Hotkey, HotkeyGroup Group);

	private readonly IHotkeyPlatform _platform;
	private readonly Action<string>? _log;
	private readonly Dictionary<int, Entry> _entries = new();

	// Every live shortcut, so a duplicate is refused before Windows is asked. Windows reports
	// a second registration of a chord this process already owns as a plain failure, which is
	// indistinguishable from another app holding it.
	//
	// Chords, not normalized strings: Hotkey carries value equality, so membership no longer
	// depends on any one spelling agreeing with any other (issue #306).
	private readonly HashSet<Hotkey> _registered = new();

	private int _nextId;

	public HotkeyRegistrationTable(IHotkeyPlatform platform, Action<string>? log = null)
	{
		_platform = platform ?? throw new ArgumentNullException(nameof(platform));
		_log = log;
	}

	/// <summary>How many registrations are live, across every group.</summary>
	public int Count => _entries.Count;

	/// <summary>Whether <paramref name="hotkey"/> is currently bound by this process.</summary>
	public bool IsRegistered(Hotkey hotkey) => _registered.Contains(hotkey);

	/// <summary>The live ids belonging to <paramref name="group"/>, in registration order.</summary>
	public IReadOnlyList<int> IdsIn(HotkeyGroup group)
	{
		var ids = new List<int>();
		foreach (var (id, entry) in _entries)
		{
			if (entry.Group == group)
				ids.Add(id);
		}
		ids.Sort();
		return ids;
	}

	/// <summary>
	/// Binds <paramref name="hotkey"/> to <paramref name="callback"/>. A chord this process
	/// already holds is refused without asking Windows, because a second registration comes
	/// back as an unexplained failure and would be reported to the user as a conflict with
	/// some other application.
	/// </summary>
	public RegistrationOutcome Register(Hotkey hotkey, Action callback, HotkeyGroup group)
	{
		if (callback is null) throw new ArgumentNullException(nameof(callback));

		string norm = NormalizeHotkey(hotkey);
		if (_registered.Contains(hotkey))
		{
			Log($"Duplicate hotkey detected: {norm}");
			return new RegistrationOutcome(norm, NoId, false, "The shortcut is already registered.");
		}

		int id = Interlocked.Increment(ref _nextId);
		uint modifiers = HotkeyModifiers.Compose(hotkey);
		if (!_platform.Register(id, modifiers, (uint)hotkey.Key, out int errorCode))
		{
			string message = DescribeRegistrationError(errorCode);
			Log($"Hotkey registration FAILED: {norm} (error={errorCode})");
			return new RegistrationOutcome(norm, id, false, message);
		}

		// A copy, because Hotkey is mutable and a caller that edits the instance it handed us
		// would otherwise move it inside the set and leave the chord permanently unbindable.
		Hotkey held = hotkey.Clone();
		_entries[id] = new Entry(callback, held, group);
		_registered.Add(held);
		Log($"Hotkey registered: {norm} (id={id}, group={group})");
		return new RegistrationOutcome(norm, id, true, null);
	}

	/// <summary>
	/// Runs the callback bound to <paramref name="id"/>. Returns false for an id this table
	/// does not know — which is what a WM_HOTKEY for a shortcut that has since been released
	/// looks like.
	/// </summary>
	public bool Dispatch(int id)
	{
		if (!_entries.TryGetValue(id, out var entry))
			return false;

		entry.Callback();
		return true;
	}

	/// <summary>
	/// Releases every registration in <paramref name="group"/>, leaving the other groups
	/// alone. Both the Windows binding and the duplicate-detection entry go, so the same
	/// chord can be registered again by the refresh that follows.
	/// </summary>
	public void ClearGroup(HotkeyGroup group)
	{
		var doomed = new List<int>();
		foreach (var (id, entry) in _entries)
		{
			if (entry.Group == group)
				doomed.Add(id);
		}

		foreach (int id in doomed)
			Release(id);
	}

	/// <summary>Releases every registration in every group.</summary>
	public void ClearAll()
	{
		var doomed = new List<int>(_entries.Keys);
		foreach (int id in doomed)
			Release(id);

		// Belt and braces: the sets are rebuilt from nothing on the next pass, so a stray
		// entry left by a partial failure above cannot make a chord permanently unbindable.
		_entries.Clear();
		_registered.Clear();
	}

	private void Release(int id)
	{
		if (!_entries.TryGetValue(id, out var entry))
			return;

		_platform.Unregister(id);
		_entries.Remove(id);
		_registered.Remove(entry.Hotkey);
	}

	/// <summary>
	/// Turns a Win32 error from RegisterHotKey into something a user can act on. 1409 is
	/// ERROR_HOTKEY_ALREADY_REGISTERED, and since this table refuses our own duplicates
	/// before calling Windows, seeing it here means another application holds the chord.
	/// </summary>
	public static string DescribeRegistrationError(int errorCode)
	{
		if (errorCode == 0)
			return "Failed to register the shortcut.";

		return errorCode switch
		{
			1409 => "The shortcut is already registered by another application.",
			_ => new Win32Exception(errorCode).Message,
		};
	}

	/// <summary>
	/// The chord as text, for a log line or a message to the user. Duplicate detection no
	/// longer goes through here — <see cref="Hotkey"/> compares by value — so this is a
	/// spelling, not an identity.
	/// </summary>
	public static string NormalizeHotkey(Hotkey hotkey) => hotkey.ToString();

	private void Log(string message) => _log?.Invoke(message);
}
