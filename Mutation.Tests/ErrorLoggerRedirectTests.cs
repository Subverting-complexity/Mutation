using System;
using System.IO;
using CognitiveSupport;
using Xunit;

namespace Mutation.Tests;

// The redirect is a process-wide static, and TestLogRedirect has already pointed it at a
// temp directory for the whole run. Each test here repoints it to its own directory and
// restores the run-wide one afterwards, so nothing escapes to the user's real log even
// if a test fails part way.
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
		string exeSideLog = Path.Combine(AppContext.BaseDirectory, "Mutation_Errors.log");
		DateTime? before = File.Exists(exeSideLog) ? File.GetLastWriteTimeUtc(exeSideLog) : null;

		ErrorLogger.LogError("Test", new InvalidOperationException("should not land beside the exe"));

		DateTime? after = File.Exists(exeSideLog) ? File.GetLastWriteTimeUtc(exeSideLog) : null;
		Assert.Equal(before, after);
		// And it did go where it was told.
		Assert.Contains("should not land beside the exe", File.ReadAllText(_logPath), StringComparison.Ordinal);
	}

	[Fact]
	public void PrimaryLogPath_FollowsTheRedirect()
	{
		Assert.Equal(_logPath, ErrorLogger.PrimaryLogPath);
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

	[Theory]
	[InlineData(null)]
	[InlineData("")]
	[InlineData("   ")]
	public void BlankDirectory_RestoresTheDefaultLocations(string? directory)
	{
		ErrorLogger.RedirectTo(directory);

		Assert.Equal(
			Path.Combine(
				Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
				"Mutation", "logs", "Mutation_Errors.log"),
			ErrorLogger.PrimaryLogPath);
	}
}
