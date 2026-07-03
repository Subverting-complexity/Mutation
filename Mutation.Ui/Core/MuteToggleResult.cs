namespace Mutation.Ui.Core;

/// <summary>
/// Outcome of a mute toggle. <see cref="Success"/> is <c>true</c> only when the
/// new state was written and confirmed by read-back on every target device.
/// <see cref="IsMuted"/> is the confirmed state to report to the user — on
/// failure it is the previous verified state, never an optimistic guess.
/// </summary>
public readonly record struct MuteToggleResult(bool Success, bool IsMuted);
