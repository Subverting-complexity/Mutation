using System;
using System.Collections.Generic;

namespace Mutation.Ui.Core;

/// <summary>
/// Tracks the microphone mute state and applies it with verification. A toggle
/// writes the desired state to every endpoint and reads it back to confirm the
/// write took effect; a thrown exception or a read-back mismatch on any endpoint
/// is a failure. On failure it re-acquires fresh device references once and
/// retries — reproducing, automatically, what a manual app restart did. The
/// tracked <see cref="IsMuted"/> advances only on a confirmed success, so the
/// UI never reports "muted" while a microphone is still live.
/// </summary>
public sealed class MuteStateController
{
	private readonly IMuteEndpointProvider _provider;
	private bool _isMuted;

	public MuteStateController(IMuteEndpointProvider provider, bool initialMuted = false)
	{
		_provider = provider ?? throw new ArgumentNullException(nameof(provider));
		_isMuted = initialMuted;
	}

	/// <summary>The last confirmed mute state.</summary>
	public bool IsMuted => _isMuted;

	/// <summary>
	/// Flips the mute state, verifying the write and retrying once with fresh
	/// device references on failure. The tracked state advances only when the
	/// new state is confirmed on every device.
	/// </summary>
	public MuteToggleResult Toggle()
	{
		bool desired = !_isMuted;

		bool success = TryApplyAndVerify(_provider.GetEndpoints(), desired);
		if (!success)
		{
			// Stale-proxy recovery: obtain fresh MMDevice / AudioEndpointVolume
			// references and retry the mute exactly once before declaring failure.
			success = TryApplyAndVerify(_provider.RefreshEndpoints(), desired);
		}

		if (success)
			_isMuted = desired;

		return new MuteToggleResult(success, _isMuted);
	}

	/// <summary>
	/// Re-applies the current confirmed mute state to the current endpoint set.
	/// This is how a capture device that appeared after the last toggle — a
	/// hot-plugged microphone — adopts the same state, so a mic connected while
	/// the app is muted is muted too. It is best-effort: the tracked
	/// <see cref="IsMuted"/> is never changed here (a real toggle owns state
	/// transitions and their verification), and a failure is reported but not
	/// acted on. Returns true only when every current endpoint confirmed the
	/// state; an empty endpoint set returns false.
	/// </summary>
	public bool SynchronizeDevices() => TryApplyAndVerify(_provider.GetEndpoints(), _isMuted);

	/// <summary>
	/// Writes <paramref name="desiredMute"/> to every endpoint and confirms it by
	/// read-back. Returns true only if all endpoints ended in the desired state.
	/// An empty endpoint set is a failure: with nothing to write, the mute cannot
	/// be confirmed to have taken effect.
	/// </summary>
	private static bool TryApplyAndVerify(IReadOnlyList<IMuteEndpoint> endpoints, bool desiredMute)
	{
		if (endpoints.Count == 0)
			return false;

		bool allConfirmed = true;
		foreach (var endpoint in endpoints)
		{
			try
			{
				endpoint.SetMute(desiredMute);
				if (endpoint.GetMute() != desiredMute)
					allConfirmed = false;
			}
			catch
			{
				allConfirmed = false;
			}
		}

		return allConfirmed;
	}
}
