using System;
using System.Collections.Generic;
using System.IO;
using Xunit;

namespace Mutation.Tests;

// The race in issue #250 was invisible: every test passed, and the class that broke the
// redaction tests did nothing wrong on its own — it just loaded settings, which replaces
// ErrorLogger's process-wide secret snapshot, from a class xunit was free to run in
// parallel with them.
//
// Attributes alone would not have stopped it coming back, because nothing about writing a
// new settings test suggests you are touching a logger. This scans the test sources
// instead, so the next one fails loudly at the point it is added rather than flaking
// months later on someone else's machine.
public class ErrorLoggerStaticsCollectionTests
{
	// Both go through SettingsManager.RegisterSecretsForRedaction.
	private static readonly string[] RegisteringCalls =
	[
		"LoadAndEnsureSettings(",
		"SaveSettingsToFile(",
	];

	[Fact]
	public void EveryTestClassThatLoadsOrSavesSettingsSharesTheErrorLoggerCollection()
	{
		string testSourceDirectory = FindTestSourceDirectory();
		string attribute = $"[Collection({nameof(ErrorLoggerCollection)}.{nameof(ErrorLoggerCollection.Name)})]";

		var unserialised = new List<string>();
		foreach (string path in Directory.EnumerateFiles(testSourceDirectory, "*.cs", SearchOption.AllDirectories))
		{
			string fileName = Path.GetFileName(path);

			// Build output, and this scanner itself — it carries every marker it
			// looks for, as string literals.
			if (path.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
				|| path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
				|| fileName == $"{nameof(ErrorLoggerStaticsCollectionTests)}.cs")
				continue;

			string source = File.ReadAllText(path);

			// A real manager, not one of the ISettingsManager fakes: only the real one
			// reaches ErrorLogger.
			if (!source.Contains("new SettingsManager(", StringComparison.Ordinal))
				continue;

			bool registers = false;
			foreach (string call in RegisteringCalls)
				registers |= source.Contains(call, StringComparison.Ordinal);

			if (registers && !source.Contains(attribute, StringComparison.Ordinal))
				unserialised.Add(fileName);
		}

		Assert.True(
			unserialised.Count == 0,
			"These test files load or save settings, which replaces ErrorLogger's secret "
				+ "snapshot, but do not carry " + attribute + ": "
				+ string.Join(", ", unserialised)
				+ ". Add the attribute, or the redaction tests will flake under parallel runs.");
	}

	// Walks up from the test binary to the repository root, the same way
	// ReadmeSettingsCoverageTests finds the README. The solution file is the marker.
	private static string FindTestSourceDirectory()
	{
		var directory = new DirectoryInfo(AppContext.BaseDirectory);
		while (directory is not null)
		{
			string tests = Path.Combine(directory.FullName, "Mutation.Tests");
			if (File.Exists(Path.Combine(directory.FullName, "Mutation.slnx")) && Directory.Exists(tests))
				return tests;

			directory = directory.Parent;
		}

		throw new InvalidOperationException(
			$"Could not find the Mutation.Tests sources above {AppContext.BaseDirectory}.");
	}
}
