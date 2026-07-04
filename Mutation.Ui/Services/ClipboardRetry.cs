using System;
using System.Threading.Tasks;

namespace Mutation.Ui.Services;

/// <summary>
/// Retry policy for clipboard operations. Another process can hold the
/// clipboard open at any moment, making reads and writes fail transiently,
/// so callers wrap them in a short retry loop instead of failing outright.
/// </summary>
public static class ClipboardRetry
{
	public const int DefaultAttempts = 5;
	public const int DefaultDelayMs = 100;

	/// <summary>
	/// Runs <paramref name="operation"/> until it succeeds, retrying up to
	/// <paramref name="attempts"/> times with <paramref name="delayMs"/>
	/// between tries. Returns true on success, false when every try threw.
	/// </summary>
	public static async Task<bool> TryAsync(
		Func<Task> operation, int attempts = DefaultAttempts, int delayMs = DefaultDelayMs)
	{
		if (operation is null)
			throw new ArgumentNullException(nameof(operation));
		if (attempts < 1)
			throw new ArgumentOutOfRangeException(nameof(attempts));

		for (int attempt = 1; attempt <= attempts; attempt++)
		{
			try
			{
				await operation().ConfigureAwait(false);
				return true;
			}
			catch when (attempt < attempts)
			{
				await Task.Delay(delayMs).ConfigureAwait(false);
			}
			catch
			{
				return false;
			}
		}

		return false;
	}

	/// <summary>
	/// Runs <paramref name="operation"/> until it succeeds, retrying up to
	/// <paramref name="attempts"/> times with <paramref name="delayMs"/>
	/// between tries. Returns the operation's result with Success true, or
	/// <c>default</c> with Success false when every try threw.
	/// </summary>
	public static async Task<(bool Success, T? Value)> TryAsync<T>(
		Func<Task<T>> operation, int attempts = DefaultAttempts, int delayMs = DefaultDelayMs)
	{
		if (operation is null)
			throw new ArgumentNullException(nameof(operation));
		if (attempts < 1)
			throw new ArgumentOutOfRangeException(nameof(attempts));

		for (int attempt = 1; attempt <= attempts; attempt++)
		{
			try
			{
				return (true, await operation().ConfigureAwait(false));
			}
			catch when (attempt < attempts)
			{
				await Task.Delay(delayMs).ConfigureAwait(false);
			}
			catch
			{
				return (false, default);
			}
		}

		return (false, default);
	}
}
