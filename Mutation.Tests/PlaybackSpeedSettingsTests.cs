using System;
using System.IO;
using CognitiveSupport;
using Mutation.Ui.Services;

namespace Mutation.Tests;

// Verifies the playback-speed value is sound after loading: a fresh file keeps the
// normal-speed default, and a hand-edited out-of-range value is snapped to a
// supported speed by SettingsManager.EnsureSettings.
public class PlaybackSpeedSettingsTests : IDisposable
{
	private readonly TempSettingsFile _settingsFile = new("playbackspeed", "{}");

	private string _tempPath => _settingsFile.Path;

	public void Dispose() => _settingsFile.Dispose();

	private Settings Ensure(Settings settings)
	{
		var manager = new SettingsManager(_tempPath);
		manager.EnsureSettings(settings, isNewFile: false);
		return settings;
	}

	[Fact]
	public void Default_PlaybackSpeed_IsNormal()
	{
		var s = Ensure(new Settings());
		Assert.Equal(PlaybackSpeedOptions.Default, s.AudioSettings!.PlaybackSpeed);
	}

	[Fact]
	public void OutOfRange_PlaybackSpeed_IsSnappedToFastest()
	{
		var s = Ensure(new Settings { AudioSettings = new AudioSettings { PlaybackSpeed = 4.7 } });
		Assert.Equal(3.0, s.AudioSettings!.PlaybackSpeed);
	}

	[Fact]
	public void OffGrid_PlaybackSpeed_IsSnappedToNearestSupported()
	{
		var s = Ensure(new Settings { AudioSettings = new AudioSettings { PlaybackSpeed = 1.1 } });
		Assert.Equal(1.0, s.AudioSettings!.PlaybackSpeed);
	}

	[Fact]
	public void SupportedValue_PlaybackSpeed_IsPreserved()
	{
		var s = Ensure(new Settings { AudioSettings = new AudioSettings { PlaybackSpeed = 1.5 } });
		Assert.Equal(1.5, s.AudioSettings!.PlaybackSpeed);
	}
}
