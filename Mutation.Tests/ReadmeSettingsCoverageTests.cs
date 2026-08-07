using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using CognitiveSupport;
using Xunit;

namespace Mutation.Tests;

// The README's settings example is the only place a user hand-editing Mutation.json can
// learn the key names. It had drifted to documenting 1 of the 27 text-to-speech options
// (issue #258), which is worse than documenting none: the section looks complete.
//
// This test fails the moment a new TextToSpeechSettings property is added without a line
// in the README, so the two cannot drift apart again silently.
public class ReadmeSettingsCoverageTests
{
	[Fact]
	public void ReadmeDocumentsEveryTextToSpeechSetting()
	{
		string readme = File.ReadAllText(FindReadme());

		var undocumented = new List<string>();
		foreach (PropertyInfo property in typeof(TextToSpeechSettings)
			.GetProperties(BindingFlags.Public | BindingFlags.Instance))
		{
			// Quoted, because that is how the key appears in the JSON example — a bare
			// name would also match the surrounding prose and let a key slip through
			// documented only in passing.
			if (!readme.Contains($"\"{property.Name}\"", StringComparison.Ordinal))
				undocumented.Add(property.Name);
		}

		Assert.True(
			undocumented.Count == 0,
			"README.md does not document these TextToSpeechSettings keys: "
				+ string.Join(", ", undocumented)
				+ ". Add them to the Configuration / Settings example.");
	}

	// Walks up from the test binary to the repository root. The solution file is the
	// marker: it sits beside README.md and nothing below it does.
	private static string FindReadme()
	{
		var directory = new DirectoryInfo(AppContext.BaseDirectory);
		while (directory is not null)
		{
			string readme = Path.Combine(directory.FullName, "README.md");
			if (File.Exists(Path.Combine(directory.FullName, "Mutation.slnx")) && File.Exists(readme))
				return readme;

			directory = directory.Parent;
		}

		throw new InvalidOperationException(
			$"Could not find the repository README.md above {AppContext.BaseDirectory}.");
	}
}
