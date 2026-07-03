namespace Mutation.Ui.Core;

/// <summary>
/// A single audio endpoint whose mute state can be written and read back.
/// Both operations may throw (for example, a stale CoreAudio COM proxy on a
/// cached device), which callers treat as a failed mute.
/// </summary>
public interface IMuteEndpoint
{
	/// <summary>Writes the mute state. May throw on a stale/disconnected device.</summary>
	void SetMute(bool mute);

	/// <summary>Reads the current mute state back. May throw on a stale/disconnected device.</summary>
	bool GetMute();
}
