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
	/// </summary>
	public static TempDirectoryValidation Normalize(string? value)
	{
		string trimmed = (value ?? string.Empty).Trim();

		if (trimmed.Length == 0)
			return Repaired("The temp directory cannot be blank.");

		if (!Path.IsPathFullyQualified(trimmed))
		{
			return Repaired(
				$"'{trimmed}' is not a full path. The temp directory must start with a drive, for example C:\\Recordings.");
		}

		try
		{
			// Resolves any '..' segments so what is stored is what is used, and
			// throws on the characters Windows will not accept in a path.
			return new TempDirectoryValidation(Path.GetFullPath(trimmed), null);
		}
		catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
		{
			return Repaired($"'{trimmed}' is not a valid folder path. {ex.Message}");
		}
	}

	/// <summary>
	/// The full message to show when <see cref="Normalize"/> had to fall back,
	/// including where recordings will be stored instead.
	/// </summary>
	public static string ComposeMessage(string problem, string replacementPath) =>
		$"{problem} Recordings will be stored in {replacementPath} instead.";

	private static TempDirectoryValidation Repaired(string problem) =>
		new(SettingsDefaults.Speech.TempDirectory, problem);
}
