using System;
using System.Collections.Generic;
using Mutation.Ui.Core;
using Xunit;

namespace Mutation.Tests;

public class MuteStateControllerTests
{
	// Stands in for a real audio endpoint without any COM hardware. It can be
	// made to throw (a stale proxy) or to silently drop the write (read-back
	// mismatch) so the verify-and-retry logic can be exercised deterministically.
	private sealed class FakeMuteEndpoint : IMuteEndpoint
	{
		private bool _mute;

		public bool ThrowOnSet { get; set; }
		public bool ThrowOnGet { get; set; }
		// When true, the write is accepted but the read-back reports the opposite
		// state — i.e. the mute did not actually take effect.
		public bool ReadBackMismatch { get; set; }
		public int SetCount { get; private set; }
		public bool CurrentMute => _mute;

		public FakeMuteEndpoint(bool initial = false) => _mute = initial;

		public void SetMute(bool mute)
		{
			SetCount++;
			if (ThrowOnSet)
				throw new InvalidOperationException("stale proxy");
			_mute = mute;
		}

		public bool GetMute()
		{
			if (ThrowOnGet)
				throw new InvalidOperationException("stale proxy");
			return ReadBackMismatch ? !_mute : _mute;
		}
	}

	private sealed class FakeProvider : IMuteEndpointProvider
	{
		private readonly IReadOnlyList<IMuteEndpoint> _initial;
		private readonly IReadOnlyList<IMuteEndpoint> _refreshed;

		public int RefreshCount { get; private set; }

		public FakeProvider(IReadOnlyList<IMuteEndpoint> initial, IReadOnlyList<IMuteEndpoint>? refreshed = null)
		{
			_initial = initial;
			_refreshed = refreshed ?? initial;
		}

		public IReadOnlyList<IMuteEndpoint> GetEndpoints() => _initial;

		public IReadOnlyList<IMuteEndpoint> RefreshEndpoints()
		{
			RefreshCount++;
			return _refreshed;
		}
	}

	[Fact]
	public void Toggle_AllEndpointsHealthy_ConfirmsAndAdvancesState_WithoutRefresh()
	{
		var a = new FakeMuteEndpoint();
		var b = new FakeMuteEndpoint();
		var provider = new FakeProvider(new IMuteEndpoint[] { a, b });
		var controller = new MuteStateController(provider);

		var result = controller.Toggle();

		Assert.True(result.Success);
		Assert.True(result.IsMuted);
		Assert.True(controller.IsMuted);
		Assert.True(a.CurrentMute);
		Assert.True(b.CurrentMute);
		Assert.Equal(0, provider.RefreshCount);
	}

	[Fact]
	public void Toggle_Twice_UnmutesBackToLive()
	{
		var a = new FakeMuteEndpoint();
		var provider = new FakeProvider(new IMuteEndpoint[] { a });
		var controller = new MuteStateController(provider);

		controller.Toggle();
		var result = controller.Toggle();

		Assert.True(result.Success);
		Assert.False(result.IsMuted);
		Assert.False(controller.IsMuted);
		Assert.False(a.CurrentMute);
	}

	[Fact]
	public void Toggle_ReadBackMismatch_RetriesOnceWithFreshEndpoints_ThenSucceeds()
	{
		// The stale endpoint accepts the write but never actually mutes.
		var stale = new FakeMuteEndpoint { ReadBackMismatch = true };
		var fresh = new FakeMuteEndpoint();
		var provider = new FakeProvider(new IMuteEndpoint[] { stale }, new IMuteEndpoint[] { fresh });
		var controller = new MuteStateController(provider);

		var result = controller.Toggle();

		Assert.True(result.Success);
		Assert.True(result.IsMuted);
		Assert.Equal(1, provider.RefreshCount);
		Assert.True(fresh.CurrentMute); // the fresh reference is what actually muted
	}

	[Fact]
	public void Toggle_ExceptionOnFirstAttempt_RecoversWithFreshEndpoints()
	{
		var stale = new FakeMuteEndpoint { ThrowOnSet = true };
		var fresh = new FakeMuteEndpoint();
		var provider = new FakeProvider(new IMuteEndpoint[] { stale }, new IMuteEndpoint[] { fresh });
		var controller = new MuteStateController(provider);

		var result = controller.Toggle();

		Assert.True(result.Success);
		Assert.True(controller.IsMuted);
		Assert.Equal(1, provider.RefreshCount);
		Assert.True(fresh.CurrentMute);
	}

	[Fact]
	public void Toggle_PartialFailure_OneEndpointBad_FailsFirstAttempt_ThenRetries()
	{
		var good = new FakeMuteEndpoint();
		var bad = new FakeMuteEndpoint { ThrowOnGet = true };
		var freshA = new FakeMuteEndpoint();
		var freshB = new FakeMuteEndpoint();
		var provider = new FakeProvider(
			new IMuteEndpoint[] { good, bad },
			new IMuteEndpoint[] { freshA, freshB });
		var controller = new MuteStateController(provider);

		var result = controller.Toggle();

		Assert.True(result.Success);
		Assert.Equal(1, provider.RefreshCount);
		Assert.True(freshA.CurrentMute);
		Assert.True(freshB.CurrentMute);
	}

	[Fact]
	public void Toggle_PersistentFailure_ReturnsFailure_RetriesExactlyOnce_DoesNotAdvanceState()
	{
		var stale = new FakeMuteEndpoint { ThrowOnSet = true };
		var stillStale = new FakeMuteEndpoint { ThrowOnSet = true };
		var provider = new FakeProvider(new IMuteEndpoint[] { stale }, new IMuteEndpoint[] { stillStale });
		var controller = new MuteStateController(provider);

		var result = controller.Toggle();

		Assert.False(result.Success);
		Assert.False(result.IsMuted);   // reported state is the previous verified state
		Assert.False(controller.IsMuted); // tracked state never advanced on failure
		Assert.Equal(1, provider.RefreshCount); // retried once, not in a loop
	}

	[Fact]
	public void Toggle_FailureWhileMuted_KeepsMutedState_NeverFalselyReportsLive()
	{
		// Starts muted; an unmute attempt that cannot be confirmed must leave the
		// state muted rather than falsely reporting the mic is live.
		var stale = new FakeMuteEndpoint(initial: true) { ThrowOnSet = true };
		var provider = new FakeProvider(new IMuteEndpoint[] { stale });
		var controller = new MuteStateController(provider, initialMuted: true);

		var result = controller.Toggle();

		Assert.False(result.Success);
		Assert.True(result.IsMuted);
		Assert.True(controller.IsMuted);
	}

	[Fact]
	public void Toggle_NoEndpoints_ReturnsFailure_BecauseMuteCannotBeConfirmed()
	{
		var provider = new FakeProvider(Array.Empty<IMuteEndpoint>(), Array.Empty<IMuteEndpoint>());
		var controller = new MuteStateController(provider);

		var result = controller.Toggle();

		Assert.False(result.Success);
		Assert.False(result.IsMuted);
		Assert.Equal(1, provider.RefreshCount);
	}

	[Fact]
	public void Constructor_NullProvider_Throws()
	{
		Assert.Throws<ArgumentNullException>(() => new MuteStateController(null!));
	}

	// A provider whose current endpoint set can grow, standing in for a
	// microphone hot-plugged after the app started. GetEndpoints() reflects the
	// latest set, exactly as AudioDeviceManager does after re-enumerating.
	private sealed class MutableProvider : IMuteEndpointProvider
	{
		private IReadOnlyList<IMuteEndpoint> _current;

		public int RefreshCount { get; private set; }

		public MutableProvider(IReadOnlyList<IMuteEndpoint> initial) => _current = initial;

		public void SetEndpoints(IReadOnlyList<IMuteEndpoint> endpoints) => _current = endpoints;

		public IReadOnlyList<IMuteEndpoint> GetEndpoints() => _current;

		public IReadOnlyList<IMuteEndpoint> RefreshEndpoints()
		{
			RefreshCount++;
			return _current;
		}
	}

	[Fact]
	public void SynchronizeDevices_WhenMuted_AppliesMutedToNewlyAddedEndpoint()
	{
		var existing = new FakeMuteEndpoint();
		var provider = new MutableProvider(new IMuteEndpoint[] { existing });
		var controller = new MuteStateController(provider);

		controller.Toggle(); // now muted; the existing mic is muted
		Assert.True(controller.IsMuted);

		// A mic is plugged in: it joins the current set, initially live.
		var added = new FakeMuteEndpoint();
		provider.SetEndpoints(new IMuteEndpoint[] { existing, added });

		bool synced = controller.SynchronizeDevices();

		Assert.True(synced);
		Assert.True(added.CurrentMute);       // the new mic adopted the muted state
		Assert.True(existing.CurrentMute);    // the existing mic stays muted
		Assert.True(controller.IsMuted);      // tracked state is unchanged
	}

	[Fact]
	public void SynchronizeDevices_WhenStartedMuted_MutesNewMic_WithoutAToggle()
	{
		var added = new FakeMuteEndpoint();
		var provider = new MutableProvider(new IMuteEndpoint[] { added });
		var controller = new MuteStateController(provider, initialMuted: true);

		bool synced = controller.SynchronizeDevices();

		Assert.True(synced);
		Assert.True(added.CurrentMute);
	}

	[Fact]
	public void SynchronizeDevices_WhenUnmuted_LeavesNewMicLive_AndDoesNotChangeState()
	{
		var added = new FakeMuteEndpoint(initial: true); // arrives muted at the OS level
		var provider = new MutableProvider(new IMuteEndpoint[] { added });
		var controller = new MuteStateController(provider); // unmuted

		bool synced = controller.SynchronizeDevices();

		Assert.True(synced);
		Assert.False(added.CurrentMute);   // brought in line with the app's live state
		Assert.False(controller.IsMuted);  // tracked state is unchanged
	}

	[Fact]
	public void SynchronizeDevices_PartialFailure_ReportsFailure_DoesNotChangeState()
	{
		var good = new FakeMuteEndpoint();
		var bad = new FakeMuteEndpoint { ThrowOnSet = true };
		var provider = new MutableProvider(new IMuteEndpoint[] { good, bad });
		var controller = new MuteStateController(provider, initialMuted: true);

		bool synced = controller.SynchronizeDevices();

		Assert.False(synced);
		Assert.True(controller.IsMuted);           // state never advanced or flipped
		Assert.Equal(0, provider.RefreshCount);    // synchronize does not re-enumerate itself
	}

	[Fact]
	public void SynchronizeDevices_NoEndpoints_ReturnsFalse_WithoutThrowing()
	{
		var provider = new MutableProvider(Array.Empty<IMuteEndpoint>());
		var controller = new MuteStateController(provider);

		bool synced = controller.SynchronizeDevices();

		Assert.False(synced);
	}
}
