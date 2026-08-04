using System;
using System.IO;
using CognitiveSupport;
using Xunit;

namespace Mutation.Tests;

// Shares a collection with ErrorLoggerRedactionTests: both drive ErrorLogger's
// process-wide secret registry, and xunit runs separate classes in parallel by default,
// so without this they would replace each other's snapshot mid-assertion.
//
// The redirect stays switched ON for the whole class — each test only ever repoints it
// at its own directory, never back to the defaults. Turning it off, even briefly, would
// let any test collection running in parallel append to the user's real log, which is
// the thing this feature exists to prevent. The default-path rules are covered through
// ErrorLogger.ResolveLogPath instead, which needs no global state.
[Collection(ErrorLoggerCollection.Name)]
public class ErrorLoggerRedirectTests : IDisposable
{
	private readonly string _dir;
	private readonly string _logPath;

	public ErrorLoggerRedirectTests()
	{
		_dir = Path.Combine(Path.GetTempPath(), "MutationErrorLoggerRedirectTests_" + Guid.NewGuid().ToString("N"));
		_logPath = Path.Combine(_dir, "Mutation_Errors.log");
		ErrorLogger.RedirectTo(_dir);
	}

	public void Dispose()
	{
		// Back to the run-wide temp directory, never to the defaults.
		TestLogRedirect.Initialize();
		try { Directory.Delete(_dir, recursive: true); } catch { /* best-effort cleanup */ }
	}

	[Fact]
	public void RedirectedLogging_WritesIntoTheGivenDirectory_CreatingIt()
	{
		ErrorLogger.LogInfo("Test", "hello from a redirected logger");

		Assert.True(File.Exists(_logPath));
		Assert.Contains("hello from a redirected logger", File.ReadAllText(_logPath), StringComparison.Ordinal);
	}

	[Fact]
	public void RedirectedLogging_DoesNotWriteBesideTheExe()
	{
		const string marker = "should not land beside the exe";
		string exeSideLog = Path.Combine(AppContext.BaseDirectory, "Mutation_Errors.log");

		ErrorLogger.LogError("Test", new InvalidOperationException(marker));

		// Asserted on content rather than a timestamp: the file may not exist at all
		// (which is also a pass), and file-time granularity is too coarse to trust.
		string besideExe = File.Exists(exeSideLog) ? File.ReadAllText(exeSideLog) : string.Empty;
		Assert.DoesNotContain(marker, besideExe, StringComparison.Ordinal);
		Assert.Contains(marker, File.ReadAllText(_logPath), StringComparison.Ordinal);
	}

	[Fact]
	public void PrimaryLogPath_FollowsTheRedirect()
	{
		Assert.Equal(_logPath, ErrorLogger.PrimaryLogPath);
	}

	[Fact]
	public void RedirectingAgain_MovesSubsequentEntriesAndLeavesEarlierOnesBehind()
	{
		string second = Path.Combine(Path.GetTempPath(), "MutationErrorLoggerRedirectTests_" + Guid.NewGuid().ToString("N"));
		try
		{
			ErrorLogger.LogInfo("Test", "before the move");
			ErrorLogger.RedirectTo(second);
			ErrorLogger.LogInfo("Test", "after the move");

			Assert.Contains("before the move", File.ReadAllText(_logPath), StringComparison.Ordinal);
			Assert.DoesNotContain("after the move", File.ReadAllText(_logPath), StringComparison.Ordinal);
			Assert.Contains("after the move", File.ReadAllText(Path.Combine(second, "Mutation_Errors.log")), StringComparison.Ordinal);
		}
		finally
		{
			ErrorLogger.RedirectTo(_dir);
			try { Directory.Delete(second, recursive: true); } catch { /* best-effort cleanup */ }
		}
	}

	[Fact]
	public void RedirectedLogging_StillRedactsRegisteredSecrets()
	{
		ErrorLogger.RegisterSecretValues(new[] { "super-secret-key-value" });
		try
		{
			ErrorLogger.LogInfo("Test", "the key is super-secret-key-value");

			string written = File.ReadAllText(_logPath);
			Assert.DoesNotContain("super-secret-key-value", written, StringComparison.Ordinal);
			Assert.Contains("***REDACTED***", written, StringComparison.Ordinal);
		}
		finally
		{
			ErrorLogger.RegisterSecretValues(null);
		}
	}

	// The default locations, exercised through the pure path resolver so no global is
	// switched off to observe them.
	[Theory]
	[InlineData(null)]
	[InlineData("")]
	[InlineData("   ")]
	public void NoRedirect_ResolvesToTheUserWritableDefault(string? directory)
	{
		Assert.Equal(
			Path.Combine(
				Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
				"Mutation", "logs", "Mutation_Errors.log"),
			ErrorLogger.ResolveLogPath(directory));
	}

	[Fact]
	public void ARedirect_ResolvesIntoThatDirectory()
	{
		Assert.Equal(
			Path.Combine(@"C:\somewhere", "Mutation_Errors.log"),
			ErrorLogger.ResolveLogPath(@"C:\somewhere"));
	}
}
