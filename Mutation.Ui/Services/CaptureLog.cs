using System;
using System.IO;
using System.Threading.Channels;
using System.Threading.Tasks;

namespace Mutation.Ui.Services;

/// <summary>
/// Writes one line per screen capture to <c>Mutation.Capture.log</c> in the temp folder.
///
/// <para>
/// It exists because issue #393 could not be diagnosed by reading the code. Captures were
/// coming back short, and the sum that produced the crop rectangle depended on four
/// measurements that all agreed with each other on paper. One of them was wrong on the real
/// desktop and nothing recorded enough to say which. A round trip of guess, ship, wait, guess
/// again is an expensive way to find that out, so the capture now writes down what it did.
/// </para>
///
/// <para>
/// The same shape as the hotkey log: a channel the caller drops a line into and a single
/// background reader that does the file work, so a capture is never waiting on a disk. The
/// file is rolled at 100 KB, and a failure to write is swallowed — a log that cannot be
/// written must not take a capture down with it.
/// </para>
/// </summary>
internal static class CaptureLog
{
	private static readonly string LogFile = Path.Combine(Path.GetTempPath(), "Mutation.Capture.log");
	private const long MaxLogFileSize = 100 * 1024;

	private static readonly Channel<string> s_lines = Channel.CreateUnbounded<string>(
		new UnboundedChannelOptions
		{
			SingleReader = true,
			SingleWriter = false,
			AllowSynchronousContinuations = false,
		});

	private static readonly Task s_writer = Task.Run(WriteLoopAsync);

	/// <summary>Where the file is, so a support question can name it.</summary>
	public static string FilePath => LogFile;

	public static void Write(string message)
	{
		try
		{
			s_lines.Writer.TryWrite($"{DateTime.Now:HH:mm:ss.fff} {message}{Environment.NewLine}");
		}
		catch (Exception ex)
		{
			System.Diagnostics.Debug.WriteLine($"CaptureLog.Write failed: {ex.Message}");
		}
	}

	private static async Task WriteLoopAsync()
	{
		await foreach (var line in s_lines.Reader.ReadAllAsync().ConfigureAwait(false))
		{
			try
			{
				RollIfTooBig();
				File.AppendAllText(LogFile, line);
			}
			catch (Exception ex)
			{
				System.Diagnostics.Debug.WriteLine($"CaptureLog writer failed: {ex.Message}");
			}
		}
	}

	private static void RollIfTooBig()
	{
		if (!File.Exists(LogFile))
			return;

		if (new FileInfo(LogFile).Length <= MaxLogFileSize)
			return;

		string previous = LogFile + ".old";
		if (File.Exists(previous))
			File.Delete(previous);
		File.Move(LogFile, previous);
	}
}
