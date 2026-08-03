#nullable enable
using System;
using System.Threading.Tasks;

namespace Mutation.Ui.Core;

/// <summary>
/// Runs the window-close steps in the only order that cannot lose user data, and
/// publishes a signal the app-level shutdown handler can wait on.
///
/// Two <c>Window.Closed</c> handlers are attached: the window's own (which persists
/// state and tears the audio session down) and the app's (which stops the host and
/// ends the process with <c>Environment.Exit</c>). The window's runs first, but only
/// until it awaits — from that moment the app's handler is free to run and terminate
/// the process. Stopping a live recording takes real time, so a sequence that stopped
/// the recorder before saving silently lost the window position, the edited prompt,
/// and any debounced slider values whenever the user closed the window mid-recording
/// (issue #223).
///
/// Hence the contract here:
/// <list type="bullet">
/// <item>persistence runs to completion synchronously, before the first await;</item>
/// <item>resource release runs in a <c>finally</c>, so a failure while stopping the
/// recorder cannot strand the audio session undisposed;</item>
/// <item><see cref="Completion"/> is signalled last, giving the app-level handler
/// something to await before terminating the process.</item>
/// </list>
///
/// The steps are injected, so the ordering guarantees are unit-testable without a
/// window, audio hardware, or a settings file.
/// </summary>
public sealed class ApplicationCloseSequence
{
	private readonly Action _persistState;
	private readonly Func<Task> _stopAudioAsync;
	private readonly Action _releaseResources;
	private readonly Action<string, Exception>? _onStepFailed;

	private readonly TaskCompletionSource _completion =
		new(TaskCreationOptions.RunContinuationsAsynchronously);

	public ApplicationCloseSequence(
		Action persistState,
		Func<Task> stopAudioAsync,
		Action releaseResources,
		Action<string, Exception>? onStepFailed = null)
	{
		_persistState = persistState ?? throw new ArgumentNullException(nameof(persistState));
		_stopAudioAsync = stopAudioAsync ?? throw new ArgumentNullException(nameof(stopAudioAsync));
		_releaseResources = releaseResources ?? throw new ArgumentNullException(nameof(releaseResources));
		_onStepFailed = onStepFailed;
	}

	/// <summary>
	/// Completes once every step has run — including the release step — whether or not
	/// an individual step failed. Never faults, so a waiter can await it unguarded.
	/// </summary>
	public Task Completion => _completion.Task;

	/// <summary>
	/// Runs the close sequence. The persistence step has finished by the time this
	/// method first yields, so a caller that abandons the returned task still gets its
	/// settings and window state written to disk.
	/// </summary>
	public async Task RunAsync()
	{
		try
		{
			// Before any await: once this method yields, the app-level Closed handler
			// runs and its shutdown path ends in Environment.Exit(0).
			RunGuarded(_persistState, "persist state");

			try
			{
				await _stopAudioAsync();
			}
			catch (Exception ex)
			{
				_onStepFailed?.Invoke("stop audio", ex);
			}
		}
		finally
		{
			RunGuarded(_releaseResources, "release resources");
			_completion.TrySetResult();
		}
	}

	// A failing step is reported and stepped over rather than aborting the sequence:
	// each remaining step still protects something the user would otherwise lose.
	private void RunGuarded(Action step, string description)
	{
		try
		{
			step();
		}
		catch (Exception ex)
		{
			_onStepFailed?.Invoke(description, ex);
		}
	}
}
