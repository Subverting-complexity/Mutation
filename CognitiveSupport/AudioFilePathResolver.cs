using System;
using System.IO;

namespace CognitiveSupport;

/// <summary>
/// Turns a sound file path from the settings into a full local path. Shared by the custom
/// beeps and the interval timers, so both accept exactly the same paths.
/// </summary>
public static class AudioFilePathResolver
{
	// Relative paths are resolved against the executable directory; absolute local paths (any
	// drive/folder) are allowed. Constraint: rejects UNC/network paths, returning string.Empty
	// so callers' existing File.Exists check fails gracefully into their "could not load" path.
	public static string Resolve(string path)
	{
		if (string.IsNullOrWhiteSpace(path))
			return path;

		if (path.StartsWith(@"\\", StringComparison.Ordinal) ||
			path.StartsWith("//", StringComparison.Ordinal))
		{
			System.Diagnostics.Debug.WriteLine($"ResolveAudioFilePath rejected UNC path: {path}");
			return string.Empty;
		}

		string baseDir = Path.GetFullPath(AppContext.BaseDirectory);
		if (!baseDir.EndsWith(Path.DirectorySeparatorChar))
			baseDir += Path.DirectorySeparatorChar;

		string combined = Path.IsPathRooted(path) ? path : Path.Combine(baseDir, path);

		try { return Path.GetFullPath(combined); }
		catch (Exception ex)
		{
			System.Diagnostics.Debug.WriteLine($"ResolveAudioFilePath GetFullPath failed for '{path}': {ex.Message}");
			return string.Empty;
		}
	}
}
