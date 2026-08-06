using System;
using System.IO;

namespace Mutation.Tests;

/// <summary>
/// A settings file in a private temp directory, removed whole when the test finishes.
///
/// <para><see cref="Mutation.Ui.Services.SettingsManager"/> saves atomically: it writes
/// <c>Mutation.json.tmp</c> and, via <c>File.Replace</c>, leaves <c>Mutation.json.bak</c>
/// behind. A fixture that deletes only the settings file leaves those two siblings in
/// <c>%TEMP%</c> for good, so every run of the suite adds more. Owning a directory rather
/// than a single path means the cleanup cannot miss a file the manager decides to write
/// next to it.</para>
/// </summary>
internal sealed class TempSettingsFile : IDisposable
{
	private readonly string _directory;

	/// <param name="prefix">Short label naming the fixture, so a leftover directory in
	/// <c>%TEMP%</c> can be traced back to the test that made it.</param>
	/// <param name="contents">Initial file contents, or null to leave the file absent —
	/// which is how a first run with no settings yet looks.</param>
	public TempSettingsFile(string prefix, string? contents = null)
	{
		_directory = Directory.CreateTempSubdirectory($"mutation-{prefix}-").FullName;
		FilePath = Path.Combine(_directory, "Mutation.json");

		if (contents is not null)
			File.WriteAllText(FilePath, contents);
	}

	/// <summary>Full path of the settings file to hand to a SettingsManager.</summary>
	public string FilePath { get; }

	public void Dispose()
	{
		// Best-effort: a test that has already failed should report its own failure, not
		// be buried under a cleanup exception from a file something else still holds.
		try { Directory.Delete(_directory, recursive: true); } catch { /* best-effort cleanup */ }
	}
}
