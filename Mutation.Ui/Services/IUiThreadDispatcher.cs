using System;
using System.Threading.Tasks;

namespace Mutation.Ui.Services;

/// <summary>
/// A way to run a piece of work on the UI thread and await its result.
/// <para>
/// This exists for APIs that only work when called from the thread that owns the window.
/// The Windows clipboard is one: <c>Clipboard.GetContent</c> and <c>Clipboard.SetContent</c>
/// fail with <c>RPC_E_WRONG_THREAD</c> anywhere else, and no amount of retrying gets past
/// that. A caller that must touch such an API repeatedly cannot rely on where the previous
/// <c>await</c> happened to leave it, so it asks for the thread it needs each time.
/// </para>
/// </summary>
public interface IUiThreadDispatcher
{
	/// <summary>
	/// Runs <paramref name="operation"/> on the UI thread and completes with its result, or
	/// faults with whatever it threw.
	/// <para>
	/// Implementations run the operation in place when the caller is already on the UI
	/// thread, so a caller never needs to ask first.
	/// </para>
	/// </summary>
	Task<T> RunAsync<T>(Func<Task<T>> operation);
}
