using System;
using System.IO;
using System.Security;

namespace CognitiveSupport;

/// <summary>
/// The one answer to "do these two strings name the same file or folder?".
/// <para>
/// There used to be three: the session planner and the recordings migrator each did
/// <see cref="Path.GetFullPath(string)"/> plus a case-insensitive compare, while
/// <c>SpeechToTextManager</c> had a private helper of the same name that skipped the
/// normalization. Two spellings of one recording therefore compared equal in one place and
/// unequal in the other, and nothing in either signature said so (issue #306).
/// </para>
/// <para>
/// It lives here rather than in <c>Mutation.Ui</c> because <see cref="SilenceRemovalLog"/>
/// needs the same rule and cannot reach up into the UI project. It depends on nothing but
/// <see cref="System.IO"/>, so the lower project is where a rule both layers follow belongs
/// (issue #324).
/// </para>
/// </summary>
public static class PathEquality
{
	/// <summary>
	/// Whether <paramref name="first"/> and <paramref name="second"/> name the same file or
	/// folder. Different spellings of one path — relative versus absolute, mixed case, a
	/// <c>..</c> hop through a sibling folder — compare equal, because the path a caller
	/// remembered and the path a directory scan rebuilt need not match character for
	/// character. A blank path matches nothing, not even another blank: two sessions with no
	/// path are not "the same recording".
	/// </summary>
	public static bool SamePath(string? first, string? second)
	{
		if (string.IsNullOrWhiteSpace(first) || string.IsNullOrWhiteSpace(second))
			return false;

		return string.Equals(Canonicalize(first), Canonicalize(second), StringComparison.OrdinalIgnoreCase);
	}

	/// <summary>
	/// The absolute form of a path, or the path itself when Windows will not resolve it.
	/// <para>
	/// A settings file can hold anything the user typed, so a path with an illegal character
	/// or a device name reaches here. Throwing would take out the caller — session refresh
	/// runs this over every file it lists — for a string that simply cannot match a real
	/// recording, so an unresolvable path falls back to comparing what it says.
	/// </para>
	/// </summary>
	private static string Canonicalize(string path)
	{
		try
		{
			return Path.GetFullPath(path);
		}
		catch (Exception ex) when (ex is ArgumentException or NotSupportedException
			or PathTooLongException or SecurityException or IOException)
		{
			return path.Trim();
		}
	}
}
