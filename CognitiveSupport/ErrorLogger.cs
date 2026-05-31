using System.Text;
using System.Text.RegularExpressions;

namespace CognitiveSupport;

/// <summary>
/// Shared, never-throwing file logger that appends redacted, timestamped entries to
/// <c>Mutation_Errors.log</c> in BOTH a user-writable location
/// (<c>%LOCALAPPDATA%\Mutation\logs</c>) and next to the EXE
/// (<see cref="AppContext.BaseDirectory"/>).
///
/// Writing to both locations (each best-effort, in its own try/catch) means a crash
/// log still lands somewhere even when the EXE folder is not writable (e.g. an install
/// under Program Files) — the historical reason cold-start crash logs silently vanished.
///
/// This is the single source of truth for error-file logging so that non-UI code
/// (e.g. the LLM services and call sites) can write to the SAME log the global
/// crash handlers in App.xaml.cs use, without divergent copies of the redaction logic.
/// Philosophy (matching commit 07019f6): log and keep the app alive; never throw from logging.
/// </summary>
public static class ErrorLogger
{
	private const string ErrorLogFileName = "Mutation_Errors.log";

	/// <summary>
	/// The user-writable log path (<c>%LOCALAPPDATA%\Mutation\logs\Mutation_Errors.log</c>),
	/// suitable for surfacing in dialogs so the user knows where to find the log.
	/// Never throws; falls back to the EXE-folder path if the local-app-data path
	/// cannot be computed.
	/// </summary>
	public static string PrimaryLogPath
	{
		get
		{
			try
			{
				string localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
				return Path.Combine(localAppData, "Mutation", "logs", ErrorLogFileName);
			}
			catch
			{
				try
				{
					return Path.Combine(AppContext.BaseDirectory, ErrorLogFileName);
				}
				catch
				{
					return ErrorLogFileName;
				}
			}
		}
	}

	/// <summary>
	/// Appends a redacted, timestamped entry describing an exception. Never throws.
	/// </summary>
	public static void LogError(string source, Exception? exception)
	{
		Write(source, SanitizeException(exception));
	}

	/// <summary>
	/// Appends a redacted, timestamped free-text breadcrumb (e.g. "LLM processing starting").
	/// Never throws.
	/// </summary>
	public static void LogInfo(string source, string message)
	{
		Write(source, RedactSecrets(message ?? string.Empty));
	}

	private static void Write(string source, string body)
	{
		string timestamp = DateTimeOffset.Now.ToString("yyyy-MM-dd HH:mm:ss.fff zzz");
		string entry = $"[{timestamp}] [{source}]{Environment.NewLine}{body}{Environment.NewLine}{Environment.NewLine}";

		// (a) User-writable location: %LOCALAPPDATA%\Mutation\logs. Created if missing.
		try
		{
			string localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
			string logDir = Path.Combine(localAppData, "Mutation", "logs");
			Directory.CreateDirectory(logDir);
			string logPath = Path.Combine(logDir, ErrorLogFileName);
			File.AppendAllText(logPath, entry, Encoding.UTF8);
		}
		catch
		{
			// Never let logging throw — callers may already be in a failure path.
		}

		// (b) EXE folder. May fail if installed under a non-writable directory
		// (e.g. Program Files); that must not stop the user-writable write above.
		try
		{
			string logPath = Path.Combine(AppContext.BaseDirectory, ErrorLogFileName);
			File.AppendAllText(logPath, entry, Encoding.UTF8);
		}
		catch
		{
			// Never let logging throw — callers may already be in a failure path.
		}
	}

	public static string SanitizeException(Exception? exception)
	{
		if (exception is null)
		{
			return "(no exception object)";
		}

		return RedactSecrets(exception.ToString());
	}

	public static string RedactSecrets(string text)
	{
		if (string.IsNullOrEmpty(text))
		{
			return text;
		}

		// Redact common secret patterns (API keys) so they never land in the log file.
		text = Regex.Replace(
			text,
			@"sk-[A-Za-z0-9_\-]{10,}",
			"sk-***REDACTED***");

		text = Regex.Replace(
			text,
			@"Bearer\s+[A-Za-z0-9_\-\.]{10,}",
			"Bearer ***REDACTED***");

		return text;
	}
}
