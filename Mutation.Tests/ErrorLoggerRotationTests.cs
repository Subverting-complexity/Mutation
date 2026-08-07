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

	[Fact]
	public void AppendWithRotation_BackupLocked_StillWritesTheEntry()
	{
		// The user has Mutation_Errors.log.old open in an editor holding a share
		// lock, so File.Delete throws. Before issue #244 that took the entry down
		// with it and logging stayed dead for the rest of the run.
		File.WriteAllText(_backupPath, "stale backup");
		string bulk = new string('z', 200);
		File.WriteAllText(_logPath, bulk);

		using (new FileStream(_backupPath, FileMode.Open, FileAccess.Read, FileShare.None))
		{
			ErrorLogger.AppendWithRotation(_logPath, "must survive\n", maxLogFileSizeBytes: 100);
		}

		// Rotation could not happen, so the entry lands on the end of the
		// over-sized log rather than being discarded.
		Assert.Equal(bulk + "must survive\n", File.ReadAllText(_logPath));
		Assert.Equal("stale backup", File.ReadAllText(_backupPath));
	}

	[Fact]
	public void AppendWithRotation_BackupUnlocked_RotatesOnTheNextEntry()
	{
		// The failure above must not be sticky: once the lock is gone, the next
		// entry rotates as normal instead of growing the log forever.
		File.WriteAllText(_backupPath, "stale backup");
		File.WriteAllText(_logPath, new string('z', 200));

		using (new FileStream(_backupPath, FileMode.Open, FileAccess.Read, FileShare.None))
		{
			ErrorLogger.AppendWithRotation(_logPath, "during lock\n", maxLogFileSizeBytes: 100);
		}

		ErrorLogger.AppendWithRotation(_logPath, "after lock\n", maxLogFileSizeBytes: 100);

		Assert.Equal("after lock\n", File.ReadAllText(_logPath));
		Assert.Equal(new string('z', 200) + "during lock\n", File.ReadAllText(_backupPath));
	}
}
