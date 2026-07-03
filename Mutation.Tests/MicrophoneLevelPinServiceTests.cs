using System;
using Mutation.Ui.Core;
using Xunit;

namespace Mutation.Tests;

public class MicrophoneLevelPinServiceTests
{
	// Records every interaction so tests can assert the pin logic's behavior
	// without real audio hardware. Crucially, this fake has no way to change mute
	// state, mirroring ICaptureLevelController — the pin service can never touch it.
	private sealed class FakeCaptureLevelController : ICaptureLevelController
	{
		public bool Supported { get; set; } = true;
		public float? Level { get; set; }
		public int SetCount { get; private set; }
		public float? LastSet { get; private set; }
		// Stands in for the device's mute flag; the pin service must never alter it.
		public bool Mute { get; set; }

		public bool IsLevelControlSupported => Supported;

		public float? GetLevelScalar() => Level;

		public void SetLevelScalar(float scalar)
		{
			SetCount++;
			LastSet = scalar;
			Level = scalar; // reflect the write so later reads see it
		}
	}

	private static MicrophoneLevelPinService NewService(FakeCaptureLevelController controller) =>
		new(controller);

	[Fact]
	public void ReassertPinnedLevel_AppliesPin_WhenEnabledAndDifferent()
	{
		var controller = new FakeCaptureLevelController { Level = 0.20f };
		var service = NewService(controller);

		bool changed = service.ReassertPinnedLevel(80);

		Assert.True(changed);
		Assert.Equal(1, controller.SetCount);
		Assert.NotNull(controller.LastSet);
		Assert.Equal(0.80f, controller.LastSet!.Value, 3);
	}

	[Fact]
	public void ReassertPinnedLevel_IsNoOp_WhenDisabled()
	{
		var controller = new FakeCaptureLevelController { Level = 0.20f };
		var service = NewService(controller);

		bool changed = service.ReassertPinnedLevel(null);

		Assert.False(changed);
		Assert.Equal(0, controller.SetCount);
	}

	[Fact]
	public void ReassertPinnedLevel_SkipsWrite_WhenWithinEpsilon()
	{
		// Current 50, pin 51 — one unit apart, which is at the epsilon boundary.
		var controller = new FakeCaptureLevelController { Level = 0.50f };
		var service = NewService(controller);

		bool changed = service.ReassertPinnedLevel(51);

		Assert.False(changed);
		Assert.Equal(0, controller.SetCount);
	}

	[Fact]
	public void ReassertPinnedLevel_Writes_WhenBeyondEpsilon()
	{
		// Current 50, pin 52 — two units apart, beyond the 1-unit epsilon.
		var controller = new FakeCaptureLevelController { Level = 0.50f };
		var service = NewService(controller);

		bool changed = service.ReassertPinnedLevel(52);

		Assert.True(changed);
		Assert.Equal(1, controller.SetCount);
		Assert.Equal(0.52f, controller.LastSet!.Value, 3);
	}

	[Fact]
	public void Pinning_NeverChangesMuteState()
	{
		var controller = new FakeCaptureLevelController { Level = 0.10f, Mute = true };
		var service = NewService(controller);

		service.ReassertPinnedLevel(90);
		service.ApplyLevel(40);

		Assert.True(controller.Mute); // mute left exactly as it was
	}

	[Fact]
	public void ReassertPinnedLevel_IsNoOpAndDoesNotThrow_OnUnsupportedDevice()
	{
		var controller = new FakeCaptureLevelController { Supported = false, Level = 0.20f };
		var service = NewService(controller);

		bool changed = service.ReassertPinnedLevel(80);

		Assert.False(changed);
		Assert.Equal(0, controller.SetCount);
		Assert.False(service.IsLevelControlSupported);
	}

	[Fact]
	public void ApplyLevel_IsNoOp_OnUnsupportedDevice()
	{
		var controller = new FakeCaptureLevelController { Supported = false, Level = 0.20f };
		var service = NewService(controller);

		bool changed = service.ApplyLevel(30);

		Assert.False(changed);
		Assert.Equal(0, controller.SetCount);
	}

	[Fact]
	public void ApplyLevel_WritesImmediately_ForLiveDrag()
	{
		var controller = new FakeCaptureLevelController { Level = 0.90f };
		var service = NewService(controller);

		bool changed = service.ApplyLevel(30);

		Assert.True(changed);
		Assert.Equal(1, controller.SetCount);
		Assert.Equal(0.30f, controller.LastSet!.Value, 3);
	}

	[Fact]
	public void WriteLevel_ClampsAboveMaximum()
	{
		var controller = new FakeCaptureLevelController { Level = 0.0f };
		var service = NewService(controller);

		bool changed = service.ApplyLevel(150);

		Assert.True(changed);
		Assert.Equal(1.0f, controller.LastSet!.Value, 3);
	}

	[Fact]
	public void WriteLevel_ClampsBelowMinimum()
	{
		var controller = new FakeCaptureLevelController { Level = 1.0f };
		var service = NewService(controller);

		bool changed = service.ReassertPinnedLevel(-10);

		Assert.True(changed);
		Assert.Equal(0.0f, controller.LastSet!.Value, 3);
	}

	[Fact]
	public void WriteLevel_WritesWhenCurrentLevelUnknown()
	{
		// A supported device whose current level can't be read still gets written.
		var controller = new FakeCaptureLevelController { Supported = true, Level = null };
		var service = NewService(controller);

		bool changed = service.ReassertPinnedLevel(60);

		Assert.True(changed);
		Assert.Equal(0.60f, controller.LastSet!.Value, 3);
	}

	[Fact]
	public void ReadCurrentLevel_ReturnsScaledValue_WhenReadable()
	{
		var controller = new FakeCaptureLevelController { Level = 0.37f };
		var service = NewService(controller);

		int? level = service.ReadCurrentLevel();

		Assert.Equal(37, level);
		Assert.Equal(0, controller.SetCount); // a pure read, never a write
	}

	[Fact]
	public void ReadCurrentLevel_ReturnsNull_OnUnsupportedDevice()
	{
		// Supported is false but a stale Level lingers — the support gate must win so
		// callers get "unknown" rather than a misleading value.
		var controller = new FakeCaptureLevelController { Supported = false, Level = 0.42f };
		var service = NewService(controller);

		Assert.Null(service.ReadCurrentLevel());
	}

	[Fact]
	public void ReadCurrentLevel_ReturnsNull_WhenLevelUnreadable()
	{
		// Supported device whose current level can't be read (transient failure): the
		// caller must be able to leave its display untouched rather than reset it.
		var controller = new FakeCaptureLevelController { Supported = true, Level = null };
		var service = NewService(controller);

		Assert.Null(service.ReadCurrentLevel());
	}

	[Fact]
	public void ReadCurrentLevel_ClampsOutOfRangeScalar()
	{
		var controller = new FakeCaptureLevelController { Level = 1.5f };
		var service = NewService(controller);

		Assert.Equal(100, service.ReadCurrentLevel());
	}

	[Fact]
	public void Constructor_RejectsNullController()
	{
		Assert.Throws<ArgumentNullException>(() => new MicrophoneLevelPinService(null!));
	}
}
