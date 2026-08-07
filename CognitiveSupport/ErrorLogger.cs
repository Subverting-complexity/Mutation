using System.Linq;
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

	// Rotate a log file once it exceeds this size, mirroring HotkeyManager's
	// 100 KB cap, so neither log file can grow without bound.
	private const long MaxLogFileSizeBytes = 100 * 1024;

	// Configured key values shorter than this are ignored by the exact-match
	// redactor — a very short "secret" would redact common substrings and do
	// more harm than good. Real provider keys are far longer than this.
	private const int MinRedactableSecretLength = 8;

	// The placeholder the settings layer writes for an unconfigured key. It is
	// not a real secret, so it must never be registered for redaction.
	private const string SettingsPlaceholderValue = "<placeholder>";

	// Snapshot of configured secret values to redact by exact match, ordered
	// longest-first so an overlapping shorter value cannot mask a longer one.
	// Replaced wholesale under the lock; readers take a local reference (array
	// reference reads are atomic) so redaction never sees a torn update.
	private static string[] s_secretValues = Array.Empty<string>();

	// Serialises both the secret-snapshot swap and each file rotate+append so a
	// concurrent writer cannot race the rotation move.
	private static readonly object s_gate = new();

	// When set, the only place entries are written. See RedirectTo.
	private static volatile string? s_logDirectoryOverride;

	/// <summary>
	/// Sends every subsequent entry to <paramref name="directory"/> alone, instead of
	/// the two default locations, and points <see cref="PrimaryLogPath"/> there. Pass
	/// <c>null</c> to restore the defaults.
	///
	/// The app itself never calls this. It exists for hosts that must not append to the
	/// user's real log — above all the test suite, whose fixtures ("no audio device",
	/// "your account is not approved for the fast-mode beta") are indistinguishable from
	/// genuine runtime failures once they are sitting in the same file the user is asked
	/// to read when something goes wrong.
	/// </summary>
	public static void RedirectTo(string? directory)
	{
		s_logDirectoryOverride = string.IsNullOrWhiteSpace(directory) ? null : directory;
	}

	/// <summary>
	/// The user-writable log path (<c>%LOCALAPPDATA%\Mutation\logs\Mutation_Errors.log</c>),
	/// suitable for surfacing in dialogs so the user knows where to find the log.
	/// Never throws; falls back to the EXE-folder path if the local-app-data path
	/// cannot be computed. Follows <see cref="RedirectTo"/> when one is in effect.
	/// </summary>
	public static string PrimaryLogPath => ResolveLogPath(s_logDirectoryOverride);

	// Split out so the path rules can be tested without touching the process-wide
	// override — reading the default path by switching the redirect off would let
	// concurrent tests log into the user's real file, which is the thing being avoided.
	internal static string ResolveLogPath(string? overrideDirectory)
	{
		if (!string.IsNullOrWhiteSpace(overrideDirectory))
		{
			try { return Path.Combine(overrideDirectory, ErrorLogFileName); }
			catch { return ErrorLogFileName; }
		}

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

	/// <summary>
	/// Registers the current set of configured provider key values so they are
	/// redacted from every subsequent log entry by exact match, regardless of
	/// their format. This catches keys (e.g. Deepgram/Azure) that no pattern
	/// matches. Call it once after settings load and again whenever the keys
	/// change (e.g. a settings save) — it replaces the previous snapshot, so a
	/// removed key stops being tracked. Null/whitespace, the settings
	/// placeholder, and values shorter than <see cref="MinRedactableSecretLength"/>
	/// are ignored. Never throws.
	/// </summary>
	public static void RegisterSecretValues(IEnumerable<string?>? secrets)
	{
		string[] snapshot;
		if (secrets is null)
		{
			snapshot = Array.Empty<string>();
		}
		else
		{
			snapshot = secrets
				.Where(static s => !string.IsNullOrWhiteSpace(s))
				.Select(static s => s!.Trim())
				.Where(static s => s.Length >= MinRedactableSecretLength
					&& !string.Equals(s, SettingsPlaceholderValue, StringComparison.Ordinal))
				.Distinct(StringComparer.Ordinal)
				// Longest first: redacting a longer key before a shorter one it
				// contains prevents a partial, still-revealing replacement.
				.OrderByDescending(static s => s.Length)
				.ToArray();
		}

		lock (s_gate)
		{
			s_secretValues = snapshot;
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

		// A redirect replaces both default locations rather than adding a third: its
		// whole purpose is that nothing lands in the user's log.
		string? redirected = s_logDirectoryOverride;
		if (redirected is not null)
		{
			// A redirect replaces both default locations rather than adding a third: its
			// whole purpose is that nothing lands in the user's log.
			AppendQuietly(redirected, entry, createDirectory: true);
			return;
		}

		// (a) User-writable location: %LOCALAPPDATA%\Mutation\logs. Created if missing.
		try
		{
			string localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
			AppendQuietly(Path.Combine(localAppData, "Mutation", "logs"), entry, createDirectory: true);
		}
		catch
		{
			// Never let logging throw — callers may already be in a failure path.
		}

		// (b) EXE folder. May fail if installed under a non-writable directory
		// (e.g. Program Files); that must not stop the user-writable write above. It
		// already exists by definition, so it is not created — that would put a
		// filesystem call on every entry for nothing.
		AppendQuietly(AppContext.BaseDirectory, entry, createDirectory: false);
	}

	// Appends to <directory>\Mutation_Errors.log, swallowing every failure — a caller of
	// the logger is usually already in a failure path and must not acquire a second one.
	private static void AppendQuietly(string directory, string entry, bool createDirectory)
	{
		try
		{
			if (createDirectory)
				Directory.CreateDirectory(directory);
			AppendWithRotation(Path.Combine(directory, ErrorLogFileName), entry, MaxLogFileSizeBytes);
		}
		catch
		{
			// Never let logging throw.
		}
	}

	/// <summary>
	/// Appends <paramref name="entry"/> to <paramref name="logPath"/>, first
	/// rotating the file to <c>&lt;path&gt;.old</c> when it already exceeds
	/// <paramref name="maxLogFileSizeBytes"/>, so the log cannot grow without
	/// bound. Mirrors HotkeyManager's rotation. The rotate + append run under
	/// the shared lock so two threads cannot both move the file at once.
	///
	/// Rotation is best-effort and cannot take the entry down with it: if the
	/// caller has <c>.old</c> open in an editor, <c>File.Delete</c> throws on every
	/// subsequent write and logging would be permanently dead for both locations,
	/// exactly when a crash log matters most. An over-sized log still tells the
	/// user what went wrong; a missing entry does not (issue #244).
	/// </summary>
	internal static void AppendWithRotation(string logPath, string entry, long maxLogFileSizeBytes)
	{
		lock (s_gate)
		{
			try
			{
				if (maxLogFileSizeBytes > 0 && File.Exists(logPath))
				{
					var fileInfo = new FileInfo(logPath);
					if (fileInfo.Length > maxLogFileSizeBytes)
					{
						string backupPath = logPath + ".old";
						if (File.Exists(backupPath))
							File.Delete(backupPath);
						File.Move(logPath, backupPath);
					}
				}
			}
			catch
			{
				// Rotation failed; keep the over-sized log and append to it anyway.
			}

			File.AppendAllText(logPath, entry, Encoding.UTF8);
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

		// First, redact any registered key value by exact match. This is the
		// reliable layer: it catches keys of any format (e.g. Deepgram's 40-char
		// hex and Azure's 32-char hex) that the patterns below do not recognise,
		// wherever they appear in the text.
		string[] secrets = s_secretValues; // atomic reference read; snapshot is immutable
		foreach (string secret in secrets)
		{
			text = text.Replace(secret, "***REDACTED***", StringComparison.Ordinal);
		}

		// Then redact common secret patterns (API keys) so a key that was never
		// registered (e.g. logged before settings loaded) is still caught.
		text = Regex.Replace(
			text,
			@"sk-[A-Za-z0-9_\-]{10,}",
			"sk-***REDACTED***");

		text = Regex.Replace(
			text,
			@"Bearer\s+[A-Za-z0-9_\-\.]{10,}",
			"Bearer ***REDACTED***");

		// Deepgram keys are sent as `Authorization: Token <key>`; mirror the
		// Bearer rule so the token never lands in the log even when unregistered.
		text = Regex.Replace(
			text,
			@"Token\s+[A-Za-z0-9_\-\.]{10,}",
			"Token ***REDACTED***");

		return text;
	}
}
