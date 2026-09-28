using CognitiveSupport;
using System;
using System.IO;

namespace Mutation.Ui.Views.SettingsUi;

/// <summary>
/// The result of checking the Temp directory setting: the path to use, and — when
/// the entered value could not be used — what was wrong with it.
/// </summary>
/// <param name="Path">The path to store. Always usable; the default when the
/// entered value was not.</param>
/// <param name="Problem">User-facing explanation, or null when the entered value
/// was fine.</param>
public readonly record struct TempDirectoryValidation(string Path, string? Problem)
{
	public bool WasRepaired => Problem is not null;
}

/// <summary>
/// Validates and normalises the Temp directory — the folder dictation recordings
/// are written to.
///
/// It is the one free-text path on the settings pages; every other numeric field is
/// bounded in XAML. A blank value used to be stored verbatim, and
/// <c>Path.Combine("", "Sessions")</c> then resolved to a path relative to the
/// executable, so recordings landed next to the install (or failed outright under
/// Program Files) with nothing said to the user (issue #230).
/// </summary>
public static class TempDirectorySetting
{
	/// <summary>
	/// Returns the path to store for <paramref name="value"/>, falling back to
	/// <see cref="SettingsDefaults.Speech.TempDirectory"/> with an explanation when
	/// the value is blank, relative, or not a path at all.
	///
	/// The value may name Windows variables (<c>%USERPROFILE%\Recordings</c>) or start
	/// with <c>~</c> for the home folder. Those are checked in their expanded form — which
	/// must still be a full path, or the #230 fault is back — but stored as typed, so the
	/// setting keeps following the variable. A value with no variable is stored resolved,
	/// as before.
	/// </summary>
	public static TempDirectoryValidation Normalize(string? value)
	{
		string trimmed = (value ?? string.Empty).Trim();

		if (trimmed.Length == 0)
			return Repaired("The temp directory cannot be blank.");

		string expanded = FolderPathVariables.Expand(trimmed);

		// A leftover %NAME% is taken as a misspelt variable when the path could not be used
		// without it, or when the value already relies on other variables. A plain full path
		// is left alone: Windows allows '%' in folder names, 'D:\Reports %Q3%' is legal, and
		// refusing it would move someone's recordings away from a folder that used to work.
		if (FolderPathVariables.HasUnresolvedVariable(expanded)
			&& (!Path.IsPathFullyQualified(expanded) || FolderPathVariables.UsesVariables(trimmed)))
		{
			return Repaired(
				$"'{trimmed}' names a variable Windows does not know. Check the spelling, for example %USERPROFILE%\\Recordings.");
		}

		if (!Path.IsPathFullyQualified(expanded))
		{
			return Repaired(
				$"'{trimmed}' is not a full path. The temp directory must start with a drive or a variable, for example C:\\Recordings or %USERPROFILE%\\Recordings.");
		}

		try
		{
			// Resolves any '..' segments so what is stored is what is used, and
			// throws on the characters Windows will not accept in a path.
			string resolved = Path.GetFullPath(expanded);
			return new TempDirectoryValidation(
				FolderPathVariables.UsesVariables(trimmed) ? trimmed : resolved,
				null);
		}
		catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
		{
			return Repaired($"'{trimmed}' is not a valid folder path. {ex.Message}");
		}
	}

	/// <summary>
	/// The folder a stored temp directory actually names, with its variables expanded.
	/// Everything that reads or writes recordings goes through this.
	/// </summary>
	public static string Resolve(string? storedPath) => FolderPathVariables.Expand(storedPath);

	/// <summary>
	/// The full message to show when <see cref="Normalize"/> had to fall back,
	/// including where recordings will be stored instead. A path with variables is
	/// shown expanded as well, so the user sees the real folder.
	/// </summary>
	public static string ComposeMessage(string problem, string replacementPath)
	{
		string expanded = FolderPathVariables.Expand(replacementPath);
		string where = string.Equals(expanded, replacementPath, StringComparison.Ordinal)
			? replacementPath
			: $"{replacementPath} ({expanded})";
		return $"{problem} Recordings will be stored in {where} instead.";
	}

	private static TempDirectoryValidation Repaired(string problem) =>
		new(SettingsDefaults.Speech.TempDirectory, problem);
}
