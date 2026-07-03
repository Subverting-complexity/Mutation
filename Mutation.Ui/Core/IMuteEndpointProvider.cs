using System.Collections.Generic;

namespace Mutation.Ui.Core;

/// <summary>
/// Supplies the audio endpoints to mute, and can re-enumerate fresh ones. The
/// refresh exists so a mute that fails on a stale COM proxy can be retried
/// against newly-acquired device references — the same recovery a manual app
/// restart performed by hand.
/// </summary>
public interface IMuteEndpointProvider
{
	/// <summary>The endpoints to mute, using the currently-held device references.</summary>
	IReadOnlyList<IMuteEndpoint> GetEndpoints();

	/// <summary>Re-enumerates the capture devices and returns fresh endpoints.</summary>
	IReadOnlyList<IMuteEndpoint> RefreshEndpoints();
}
