using System.Collections.Generic;
using Mutation.Ui.Services;

namespace Mutation.Tests;

/// <summary>
/// Stands in for Windows' RegisterHotKey/UnregisterHotKey. The build machine has no window
/// handle and no guarantee that any given chord is free, so the real API can only ever be
/// tested by luck — while the bookkeeping above it is exactly where a registration leak or
/// a mis-routed chord would hide.
/// </summary>
internal sealed class FakeHotkeyPlatform : IHotkeyPlatform
{
	/// <summary>Ids Windows currently considers bound, in the order they were registered.</summary>
	public List<int> Bound { get; } = new();

	/// <summary>Every id ever released, including ones released twice.</summary>
	public List<int> Released { get; } = new();

	/// <summary>The (modifiers, key) pair each id was registered with.</summary>
	public Dictionary<int, (uint Modifiers, uint Key)> Requests { get; } = new();

	/// <summary>Set to make the next registration fail with this Win32 error code, then reset.</summary>
	public int? NextFailureCode { get; set; }

	public bool Register(int id, uint modifiers, uint virtualKey, out int errorCode)
	{
		Requests[id] = (modifiers, virtualKey);

		if (NextFailureCode is int code)
		{
			NextFailureCode = null;
			errorCode = code;
			return false;
		}

		Bound.Add(id);
		errorCode = 0;
		return true;
	}

	public void Unregister(int id)
	{
		Released.Add(id);
		Bound.Remove(id);
	}
}
