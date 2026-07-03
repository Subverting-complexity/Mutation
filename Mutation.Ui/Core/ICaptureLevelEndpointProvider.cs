namespace Mutation.Ui.Core;

/// <summary>
/// Supplies the active microphone's level endpoint, and can re-resolve a fresh
/// one. The refresh exists so a level write that fails on a stale COM proxy can
/// be retried against a newly-acquired device reference — the same recovery a
/// manual app restart performed by hand, and the mirror of
/// <see cref="IMuteEndpointProvider"/>'s <c>RefreshEndpoints</c>.
/// </summary>
public interface ICaptureLevelEndpointProvider
{
	/// <summary>The active microphone's level endpoint, using the currently-held device reference.</summary>
	ICaptureLevelEndpoint GetEndpoint();

	/// <summary>Re-resolves the active microphone to a fresh device reference and returns its endpoint.</summary>
	ICaptureLevelEndpoint RefreshEndpoint();
}
