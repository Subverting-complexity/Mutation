using System;
using System.IO;
using CognitiveSupport;
using Mutation.Ui.Services;
using Mutation.Ui.Views.SettingsUi;

namespace Mutation.Tests;

// Covers the move of the default dictation temp directory from the
// world-readable C:\Temp\Mutation to %LOCALAPPDATA%\Mutation (#159):
// the settings rewrite rules and the best-effort recording migration.
public class TempDirectoryMigrationTests : IDisposable
{
	private readonly string _root;

	public TempDirectoryMigrationTests()
	{
		_root = Path.Combine(Path.GetTempPath(), $"mutation-tempdir-{Guid.NewGuid():N}");
		Directory.CreateDirectory(_root);
	}

	public void Dispose()
	{
		if (Directory.Exists(_root))
			Directory.Delete(_root, recursive: true);
	}

	private static Settings EnsureSettingsOn(string? tempDirectory)
	{
		var settings = new Settings
		{
			SpeechToTextSettings = new SpeechToTextSettings { TempDirectory = tempDirectory }
		};
		var manager = new SettingsManager("unused.json");
		manager.EnsureSettings(settings, isNewFile: false);
		return settings;
	}

	[Fact]
	public void DefaultTempDirectory_IsUnderLocalAppData()
	{
		string localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
		Assert.StartsWith(localAppData, SettingsDefaults.Speech.TempDirectory, StringComparison.OrdinalIgnoreCase);
	}

	[Fact]
	public void EnsureSettings_MissingTempDirectory_GetsProfileDefault()
	{
		var settings = EnsureSettingsOn(null);
		Assert.Equal(SettingsDefaults.Speech.TempDirectory, settings.SpeechToTextSettings!.TempDirectory);
	}

	[Theory]
	[InlineData(@"C:\Temp\Mutation")]
	[InlineData(@"c:\temp\mutation")]
	[InlineData(@"C:\Temp\Mutation\")]
	public void EnsureSettings_LegacyDefault_IsRewrittenToProfileDefault(string legacy)
	{
		var settings = EnsureSettingsOn(legacy);
		Assert.Equal(SettingsDefaults.Speech.TempDirectory, settings.SpeechToTextSettings!.TempDirectory);
	}

	[Fact]
	public void EnsureSettings_CustomTempDirectory_IsKept()
	{
		var settings = EnsureSettingsOn(@"D:\MyRecordings");
		Assert.Equal(@"D:\MyRecordings", settings.SpeechToTextSettings!.TempDirectory);
	}

	[Fact]
	public void MigrateSessions_MovesRecordingsAndRemovesEmptyLegacyDirs()
	{
		string legacy = Path.Combine(_root, "legacy");
		string target = Path.Combine(_root, "new");
		string legacySessions = Path.Combine(legacy, "Sessions");
		Directory.CreateDirectory(legacySessions);
		File.WriteAllText(Path.Combine(legacySessions, "session_2026-01-01_10-00-00.ogg"), "audio");

		SessionRecordingsMigrator.MigrateSessions(legacy, target);

		Assert.True(File.Exists(Path.Combine(target, "Sessions", "session_2026-01-01_10-00-00.ogg")));
		Assert.False(Directory.Exists(legacy));
	}

	[Fact]
	public void MigrateSessions_DoesNotOverwriteExistingRecording()
	{
		string legacy = Path.Combine(_root, "legacy");
		string target = Path.Combine(_root, "new");
		Directory.CreateDirectory(Path.Combine(legacy, "Sessions"));
		Directory.CreateDirectory(Path.Combine(target, "Sessions"));
		File.WriteAllText(Path.Combine(legacy, "Sessions", "session_a.ogg"), "old");
		File.WriteAllText(Path.Combine(target, "Sessions", "session_a.ogg"), "new");

		SessionRecordingsMigrator.MigrateSessions(legacy, target);

		Assert.Equal("new", File.ReadAllText(Path.Combine(target, "Sessions", "session_a.ogg")));
	}

	[Fact]
	public void MigrateSessions_MissingLegacyDirectory_IsANoOp()
	{
		string target = Path.Combine(_root, "new");
		SessionRecordingsMigrator.MigrateSessions(Path.Combine(_root, "absent"), target);
		Assert.False(Directory.Exists(Path.Combine(target, "Sessions")));
	}

	[Fact]
	public void MigrateSessions_LeavesNonSessionFilesBehind()
	{
		string legacy = Path.Combine(_root, "legacy");
		string target = Path.Combine(_root, "new");
		string legacySessions = Path.Combine(legacy, "Sessions");
		Directory.CreateDirectory(legacySessions);
		File.WriteAllText(Path.Combine(legacySessions, "notes.txt"), "keep");

		SessionRecordingsMigrator.MigrateSessions(legacy, target);

		Assert.True(File.Exists(Path.Combine(legacySessions, "notes.txt")));
	}
}
