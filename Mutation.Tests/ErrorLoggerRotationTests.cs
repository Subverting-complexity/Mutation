using System;
using System.IO;
using CognitiveSupport;
using Xunit;

namespace Mutation.Tests;

// Exercises ErrorLogger.AppendWithRotation against a real temp file so the
// size-cap rotation is verified in isolation from the fixed OS log locations.
public class ErrorLoggerRotationTests : IDisposable
{
	private readonly string _dir;
	private readonly string _logPath;
	private readonly string _backupPath;

	public ErrorLoggerRotationTests()
	{
		_dir = Path.Combine(Path.GetTempPath(), "MutationErrorLoggerRotationTests_" + Guid.NewGuid().ToString("N"));
		Directory.CreateDirectory(_dir);
		_logPath = Path.Combine(_dir, "Mutation_Errors.log");
		_backupPath = _logPath + ".old";
	}

	public void Dispose()
	{
		try { Directory.Delete(_dir, recursive: true); } catch { /* best-effort cleanup */ }
	}

	[Fact]
	public void AppendWithRotation_CreatesFileWhenMissing()
	{
		ErrorLogger.AppendWithRotation(_logPath, "first entry\n", maxLogFileSizeBytes: 1024);

		Assert.True(File.Exists(_logPath));
		Assert.Equal("first entry\n", File.ReadAllText(_logPath));
		Assert.False(File.Exists(_backupPath));
	}

	[Fact]
	public void AppendWithRotation_UnderLimit_AppendsWithoutRotating()
	{
		ErrorLogger.AppendWithRotation(_logPath, "a\n", maxLogFileSizeBytes: 1024);
		ErrorLogger.AppendWithRotation(_logPath, "b\n", maxLogFileSizeBytes: 1024);

		Assert.Equal("a\nb\n", File.ReadAllText(_logPath));
		Assert.False(File.Exists(_backupPath));
	}

	[Fact]
	public void AppendWithRotation_OverLimit_RotatesToOldThenWritesFresh()
	{
		string bulk = new string('x', 200);
		File.WriteAllText(_logPath, bulk);

		ErrorLogger.AppendWithRotation(_logPath, "new entry\n", maxLogFileSizeBytes: 100);

		// The oversized content moved to .old; the live file holds only the new entry.
		Assert.True(File.Exists(_backupPath));
		Assert.Equal(bulk, File.ReadAllText(_backupPath));
		Assert.Equal("new entry\n", File.ReadAllText(_logPath));
	}

	[Fact]
	public void AppendWithRotation_SecondRotation_OverwritesPreviousBackup()
	{
		File.WriteAllText(_backupPath, "stale backup");
		File.WriteAllText(_logPath, new string('y', 200));

		ErrorLogger.AppendWithRotation(_logPath, "latest\n", maxLogFileSizeBytes: 100);

		// The pre-existing .old is replaced by the rotated content, not appended to.
		Assert.Equal(new string('y', 200), File.ReadAllText(_backupPath));
		Assert.Equal("latest\n", File.ReadAllText(_logPath));
	}
}
