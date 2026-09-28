using System;
using System.Text.RegularExpressions;

namespace CognitiveSupport;

/// <summary>
/// Turns a folder path the user typed — which may name Windows variables such as
/// <c>%USERPROFILE%\Mutation</c>, or start with <c>~</c> for their home folder — into the
/// real path it stands for.
/// <para>
/// The setting keeps what the user typed, not the expansion, so a settings file copied to
/// another account or machine still points at that account's own folder. That means every
/// place that turns the setting into a file path has to expand it first, and has to do so
/// the same way; this class is that one way. It lives here rather than in <c>Mutation.Ui</c>
/// so the recorder in the core and the settings check in the UI can share it.
/// </para>
/// </summary>
public static class FolderPathVariables
{
	private static readonly Regex UnresolvedVariable = new(@"%[^%\\/]+%", RegexOptions.CultureInvariant);

	/// <summary>
	/// Returns <paramref name="path"/> with every known <c>%NAME%</c> variable replaced by its
	/// value, and a leading <c>~</c> (on its own, or followed by a slash) replaced by the
	/// user's home folder. A variable Windows does not know is left as typed, so
	/// <see cref="HasUnresolvedVariable"/> can report it. Surrounding whitespace is trimmed.
	/// </summary>
	public static string Expand(string? path)
	{
		string trimmed = (path ?? string.Empty).Trim();

		if (trimmed == "~" || trimmed.StartsWith(@"~\", StringComparison.Ordinal) || trimmed.StartsWith("~/", StringComparison.Ordinal))
		{
			string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
			// Left alone when Windows cannot say where home is, so the caller sees a path that
			// is not fully qualified and rejects it, rather than one quietly rooted elsewhere.
			if (!string.IsNullOrEmpty(home))
				trimmed = home + trimmed[1..];
		}

		return Environment.ExpandEnvironmentVariables(trimmed);
	}

	/// <summary>
	/// Whether <paramref name="path"/> names a variable. A path with none is stored in its
	/// resolved form; a path with one is stored as typed, so it keeps following the variable.
	/// </summary>
	public static bool UsesVariables(string? path) =>
		!string.Equals((path ?? string.Empty).Trim(), Expand(path), StringComparison.Ordinal)
		|| HasUnresolvedVariable(path);

	/// <summary>
	/// Whether <paramref name="expandedPath"/> still holds a <c>%NAME%</c> after expansion —
	/// a variable Windows does not know, most often a typing mistake such as
	/// <c>%USERPROFILES%</c>. Left in, it would become a folder literally named that.
	/// </summary>
	public static bool HasUnresolvedVariable(string? expandedPath) =>
		expandedPath is not null && UnresolvedVariable.IsMatch(expandedPath);
}
