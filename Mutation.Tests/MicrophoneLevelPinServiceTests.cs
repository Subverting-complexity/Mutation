using System;
using Mutation.Ui.Core;
using Xunit;

namespace Mutation.Tests;

public class MicrophoneLevelPinServiceTests
{
	// A single level endpoint that records every interaction so tests can assert
	// the pin logic's behavior without real audio hardware. It can be made to
	// throw on set/get (a stale-proxy failure) or to ignore writes (a device that
	// silently rejects them, caught by read-back verification). Crucially, it has
	// no way to change mute state, mirroring the real endpoint — the pin service
	// can never touch mute.
	private sealed class FakeCaptureLevelEndpoint : ICaptureLevelEndpoint
	{
		public bool Supported { get; set; } = true;
		public float Level { get; set; }
		public bool ThrowOnSet { get; set; }
		public bool ThrowOnGet { get; set; }
		// When set, writes are accepted but do not change the level, so read-back
		// verification sees a mismatch — a "silently rejected write".
		public bool IgnoreWrites { get; set; }
		public int SetCount { get; private set; }
		public float? LastSet { get; private set; }
		// Stands in for the device's mute flag; the pin service must never alter it.
		public bool Mute { get; set; }

		public bool IsLevelControlSupported => Supported;

		public float GetLevelScalar()
		{
			if (ThrowOnGet)
				throw new InvalidOperationException("stale proxy");
			return Level;
		}

		public void SetLevelScalar(float scalar)
		{
			SetCount++;
			LastSet = scalar;
			if (ThrowOnSet)
				throw new InvalidOperationException("stale proxy");
			if (!IgnoreWrites)
				Level = scalar; // reflect the write so later reads see it
		}
	}

	// A provider whose refresh swaps in a second, independently-configured endpoint,
	// so tests can model a stale endpoint recovered by a fresh reference.
	private sealed class FakeProvider : ICaptureLevelEndpointProvider
	{
		private readonly FakeCaptureLevelEndpoint _current;
		private readonly FakeCaptureLevelEndpoint _refreshed;

		public FakeProvider(FakeCaptureLevelEndpoint current, FakeCaptureLevelEndpoint? refreshed = null)
		{
			_current = current;
			_refreshed = refreshed ?? current;
		}

		public int RefreshCount { get; private set; }

		public ICaptureLevelEndpoint GetEndpoint() => _current;

		public ICaptureLevelEndpoint RefreshEndpoint()
		{
			RefreshCount++;
			return _refreshed;
		}
	}

	private static MicrophoneLevelPinService NewService(FakeCaptureLevelEndpoint endpoint) =>
		new(new FakeProvider(endpoint));

	[Fact]
	public void ReassertPinnedLevel_AppliesPin_WhenEnabledAndDifferent()
	{
		var endpoint = new FakeCaptureLevelEndpoint { Level = 0.20f };
		var service = NewService(endpoint);

		var result = service.ReassertPinnedLevel(80);

		Assert.Equal(CaptureLevelOutcome.Applied, result.Outcome);
		Assert.False(result.Failed);
		Assert.Equal(1, endpoint.SetCount);
		Assert.NotNull(endpoint.LastSet);
		Assert.Equal(0.80f, endpoint.LastSet!.Value, 3);
	}

	[Fact]
	public void ReassertPinnedLevel_IsUnchanged_WhenDisabled()
	{
		var endpoint = new FakeCaptureLevelEndpoint { Level = 0.20f };
		var service = NewService(endpoint);

		var result = service.ReassertPinnedLevel(null);

		Assert.Equal(CaptureLevelOutcome.Unchanged, result.Outcome);
		Assert.Equal(0, endpoint.SetCount);
	}

	[Fact]
	public void ReassertPinnedLevel_SkipsWrite_WhenWithinEpsilon()
	{
		// Current 50, pin 51 — one unit apart, which is at the epsilon boundary.
		var endpoint = new FakeCaptureLevelEndpoint { Level = 0.50f };
		var service = NewService(endpoint);

		var result = service.ReassertPinnedLevel(51);

		Assert.Equal(CaptureLevelOutcome.Unchanged, result.Outcome);
		Assert.Equal(0, endpoint.SetCount);
	}

	[Fact]
	public void ReassertPinnedLevel_Writes_WhenBeyondEpsilon()
	{
		// Current 50, pin 52 — two units apart, beyond the 1-unit epsilon.
		var endpoint = new FakeCaptureLevelEndpoint { Level = 0.50f };
		var service = NewService(endpoint);

		var result = service.ReassertPinnedLevel(52);

		Assert.Equal(CaptureLevelOutcome.Applied, result.Outcome);
		Assert.Equal(1, endpoint.SetCount);
		Assert.Equal(0.52f, endpoint.LastSet!.Value, 3);
	}

	[Fact]
	public void Pinning_NeverChangesMuteState()
	{
		var endpoint = new FakeCaptureLevelEndpoint { Level = 0.10f, Mute = true };
		var service = NewService(endpoint);

		service.ReassertPinnedLevel(90);
		service.ApplyLevel(40);

		Assert.True(endpoint.Mute); // mute left exactly as it was
	}

	[Fact]
	public void ReassertPinnedLevel_IsUnsupported_OnHardwareFixedDevice()
	{
		var endpoint = new FakeCaptureLevelEndpoint { Supported = false, Level = 0.20f };
		var service = NewService(endpoint);

		var result = service.ReassertPinnedLevel(80);

		// A hardware-fixed device is a distinct, non-failure outcome — no beep/warning.
		Assert.Equal(CaptureLevelOutcome.Unsupported, result.Outcome);
		Assert.False(result.Failed);
		Assert.Equal(0, endpoint.SetCount);
		Assert.False(service.ReadLevelState().IsSupported);
	}

	// ----- Combined level probe (issue #263) -----

	[Fact]
	public void ReadLevelState_ReportsSupportAndLevelTogether()
	{
		var endpoint = new FakeCaptureLevelEndpoint { Level = 0.42f };
		var service = NewService(endpoint);

		Assert.Equal(new CaptureLevelState(IsSupported: true, Level: 42), service.ReadLevelState());
	}

	[Fact]
	public void ReadLevelState_ReportsUnsupported_WithNoLevel_OnHardwareFixedDevice()
	{
		var endpoint = new FakeCaptureLevelEndpoint { Supported = false, Level = 0.20f };
		var service = NewService(endpoint);

		Assert.Equal(new CaptureLevelState(IsSupported: false, Level: null), service.ReadLevelState());
	}

	[Fact]
	public void ReadLevelState_ReportsSupported_WithUnknownLevel_WhenTheReadFails()
	{
		// A supported device whose level cannot be read right now is not the same as an
		// unsupported one: the controls stay enabled, the display is left alone.
		var endpoint = new FakeCaptureLevelEndpoint { Level = 0.30f, ThrowOnGet = true };
		var service = NewService(endpoint);

		Assert.Equal(new CaptureLevelState(IsSupported: true, Level: null), service.ReadLevelState());
	}

	[Fact]
	public void ReadLevelState_DoesNotWriteTheLevelOrTouchMute()
	{
		var endpoint = new FakeCaptureLevelEndpoint { Level = 0.30f, Mute = true };
		var service = NewService(endpoint);

		service.ReadLevelState();

		Assert.Equal(0, endpoint.SetCount);
		Assert.True(endpoint.Mute);
	}

	[Fact]
	public void ApplyLevel_IsUnsupported_OnHardwareFixedDevice()
	{
		var endpoint = new FakeCaptureLevelEndpoint { Supported = false, Level = 0.20f };
		var service = NewService(endpoint);

		var result = service.ApplyLevel(30);

		Assert.Equal(CaptureLevelOutcome.Unsupported, result.Outcome);
		Assert.Equal(0, endpoint.SetCount);
	}

	[Fact]
	public void ApplyLevel_WritesImmediately_ForLiveDrag()
	{
		var endpoint = new FakeCaptureLevelEndpoint { Level = 0.90f };
		var service = NewService(endpoint);

		var result = service.ApplyLevel(30);

		Assert.Equal(CaptureLevelOutcome.Applied, result.Outcome);
		Assert.Equal(1, endpoint.SetCount);
		Assert.Equal(0.30f, endpoint.LastSet!.Value, 3);
	}

	[Fact]
	public void WriteLevel_ClampsAboveMaximum()
	{
		var endpoint = new FakeCaptureLevelEndpoint { Level = 0.0f };
		var service = NewService(endpoint);

		var result = service.ApplyLevel(150);

		Assert.Equal(CaptureLevelOutcome.Applied, result.Outcome);
		Assert.Equal(1.0f, endpoint.LastSet!.Value, 3);
	}

	[Fact]
	public void WriteLevel_ClampsBelowMinimum()
	{
		var endpoint = new FakeCaptureLevelEndpoint { Level = 1.0f };
		var service = NewService(endpoint);

		var result = service.ReassertPinnedLevel(-10);

		Assert.Equal(CaptureLevelOutcome.Applied, result.Outcome);
		Assert.Equal(0.0f, endpoint.LastSet!.Value, 3);
	}

	[Fact]
	public void WriteLevel_AttemptsWrite_ThenFails_WhenLevelUnreadable()
	{
		// A supported device whose level can't be read: the unreadable current level
		// bypasses the redundant-write skip so the write is still attempted, but the
		// verifying read-back also throws, so the pin cannot be confirmed.
		var endpoint = new FakeCaptureLevelEndpoint { Supported = true, ThrowOnGet = true };
		var service = NewService(endpoint);

		var result = service.ApplyLevel(60);

		Assert.True(endpoint.SetCount >= 1);          // the write was attempted
		Assert.Equal(0.60f, endpoint.LastSet!.Value, 3);
		// Read-back cannot confirm (get throws) so the outcome is a failure, not a
		// false success — an unconfirmable write is treated as failed per the spec.
		Assert.Equal(CaptureLevelOutcome.Failed, result.Outcome);
	}

	[Fact]
	public void WriteLevel_RetriesOnFreshEndpoint_WhenFirstWriteThrows()
	{
		// The current endpoint's write throws (stale proxy); the refreshed one works.
		var stale = new FakeCaptureLevelEndpoint { Level = 0.20f, ThrowOnSet = true };
		var fresh = new FakeCaptureLevelEndpoint { Level = 0.20f };
		var provider = new FakeProvider(stale, fresh);
		var service = new MicrophoneLevelPinService(provider);

		var result = service.ApplyLevel(80);

		Assert.Equal(CaptureLevelOutcome.Applied, result.Outcome);
		Assert.Equal(1, provider.RefreshCount);   // re-acquired fresh references once
		Assert.Equal(1, stale.SetCount);          // first attempt on the stale endpoint
		Assert.Equal(0.80f, fresh.LastSet!.Value, 3);
	}

	[Fact]
	public void WriteLevel_Fails_WhenBothAttemptsThrow()
	{
		var stale = new FakeCaptureLevelEndpoint { Level = 0.20f, ThrowOnSet = true };
		var alsoStale = new FakeCaptureLevelEndpoint { Level = 0.20f, ThrowOnSet = true };
		var provider = new FakeProvider(stale, alsoStale);
		var service = new MicrophoneLevelPinService(provider);

		var result = service.ApplyLevel(80);

		Assert.Equal(CaptureLevelOutcome.Failed, result.Outcome);
		Assert.True(result.Failed);
		Assert.Equal(1, provider.RefreshCount);   // retried exactly once
	}

	[Fact]
	public void WriteLevel_Fails_OnReadBackMismatch_AfterRetry()
	{
		// Writes are accepted without error but the level never actually moves, so
		// read-back never matches the target — a silently-rejected write on both
		// the current and the refreshed endpoint.
		var stale = new FakeCaptureLevelEndpoint { Level = 0.20f, IgnoreWrites = true };
		var alsoStale = new FakeCaptureLevelEndpoint { Level = 0.20f, IgnoreWrites = true };
		var provider = new FakeProvider(stale, alsoStale);
		var service = new MicrophoneLevelPinService(provider);

		var result = service.ApplyLevel(80);

		Assert.Equal(CaptureLevelOutcome.Failed, result.Outcome);
		Assert.Equal(1, provider.RefreshCount);
		Assert.Equal(1, stale.SetCount);          // attempted the write before verifying
	}

	[Fact]
	public void WriteLevel_Succeeds_WhenReadBackMatchesAfterRetry()
	{
		// The current endpoint silently rejects the write (read-back mismatch); the
		// refreshed one applies it correctly.
		var stale = new FakeCaptureLevelEndpoint { Level = 0.20f, IgnoreWrites = true };
		var fresh = new FakeCaptureLevelEndpoint { Level = 0.20f };
		var provider = new FakeProvider(stale, fresh);
		var service = new MicrophoneLevelPinService(provider);

		var result = service.ApplyLevel(80);

		Assert.Equal(CaptureLevelOutcome.Applied, result.Outcome);
		Assert.Equal(1, provider.RefreshCount);
		Assert.Equal(0.80f, fresh.LastSet!.Value, 3);
	}

	[Fact]
	public void ReadLevelState_ScalesTheLevel_AndNeverWrites()
	{
		var endpoint = new FakeCaptureLevelEndpoint { Level = 0.37f };
		var service = NewService(endpoint);

		Assert.Equal(37, service.ReadLevelState().Level);
		Assert.Equal(0, endpoint.SetCount); // a pure read, never a write
	}

	[Fact]
	public void ReadLevelState_ReportsNoLevel_OnUnsupportedDevice()
	{
		// Supported is false but a stale Level lingers — the support gate must win so
		// callers get "unknown" rather than a misleading value.
		var endpoint = new FakeCaptureLevelEndpoint { Supported = false, Level = 0.42f };
		var service = NewService(endpoint);

		Assert.Null(service.ReadLevelState().Level);
	}

	[Fact]
	public void ReadLevelState_ClampsOutOfRangeScalar()
	{
		var endpoint = new FakeCaptureLevelEndpoint { Level = 1.5f };
		var service = NewService(endpoint);

		Assert.Equal(100, service.ReadLevelState().Level);
	}

	[Fact]
	public void Constructor_RejectsNullProvider()
	{
		Assert.Throws<ArgumentNullException>(() => new MicrophoneLevelPinService(null!));
	}
}
