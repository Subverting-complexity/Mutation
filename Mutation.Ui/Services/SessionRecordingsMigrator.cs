using CognitiveSupport;
using Mutation.Ui.Core;
using System;
using System.IO;

namespace Mutation.Ui.Services;

// Moves retained dictation recordings from the old world-readable default
// (C:\Temp\Mutation) into the new user-profile temp directory the first time
// settings are upgraded. Best-effort: a file that cannot be moved is left
// behind rather than failing startup — the recordings are convenience data,
// not something worth blocking the app over.
internal static class SessionRecordingsMigrator
{
	internal static void MigrateSessions(string legacyTempDirectory, string newTempDirectory)
	{
		try
		{
			string legacySessions = Path.Combine(legacyTempDirectory, Constants.SessionsDirectoryName);
			string newSessions = Path.Combine(newTempDirectory, Constants.SessionsDirectoryName);

			if (!Directory.Exists(legacySessions))
				return;
			if (PathEquality.SamePath(legacySessions, newSessions))
				return;

			Directory.CreateDirectory(newSessions);

			foreach (string file in Directory.GetFiles(legacySessions, "session_*"))
			{
				string destination = Path.Combine(newSessions, Path.GetFileName(file));
				try
				{
					if (!File.Exists(destination))
						File.Move(file, destination);
				}
				catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
				{
					ErrorLogger.LogInfo("SessionMigration", $"Could not move '{file}' to '{destination}'");
					ErrorLogger.LogError("SessionMigration", ex);
				}
			}

			TryDeleteIfEmpty(legacySessions);
			TryDeleteIfEmpty(legacyTempDirectory);
		}
		catch (Exception ex)
		{
			ErrorLogger.LogError("SessionMigration", ex);
		}
	}

	private static void TryDeleteIfEmpty(string directory)
	{
		try
		{
			if (Directory.Exists(directory)
				&& Directory.GetFiles(directory).Length == 0
				&& Directory.GetDirectories(directory).Length == 0)
			{
				Directory.Delete(directory);
			}
		}
		catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
		{
			ErrorLogger.LogInfo("SessionMigration", $"Could not remove empty legacy directory '{directory}'");
			ErrorLogger.LogError("SessionMigration", ex);
		}
	}
}
