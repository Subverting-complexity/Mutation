using System;
using System.IO;
using System.Runtime.CompilerServices;
using CognitiveSupport;

namespace Mutation.Tests;

/// <summary>
/// Points <see cref="ErrorLogger"/> at a temp directory for the whole test run.
///
/// Plenty of the code under test logs, and the logger's default destination is the
/// user's real <c>%LOCALAPPDATA%\Mutation\logs\Mutation_Errors.log</c> — the first file
/// anyone reads when the app misbehaves. Fixture output landing there ("no audio
/// device", "your account is not approved for the fast-mode beta") is indistinguishable
/// from a genuine failure, and it churns the 100 KB rotation so real entries roll out of
/// the live file early. This has already cost real time during a diagnosis.
///
/// A module initializer rather than a fixture: it runs when the test assembly is
/// loaded, so it cannot be raced by a test that logs before some fixture got its turn.
/// </summary>
internal static class TestLogRedirect
{
	[ModuleInitializer]
	internal static void Initialize()
	{
		// A fixed path, not a per-run one: the logger's own rotation caps it, and a new
		// directory per run would silt up the temp folder.
		string directory = Path.Combine(Path.GetTempPath(), "Mutation.Tests", "logs");
		ErrorLogger.RedirectTo(directory);
	}
}
