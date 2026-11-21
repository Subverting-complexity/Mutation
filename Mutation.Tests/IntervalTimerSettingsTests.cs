using System.Collections.Generic;
using System.IO;
using CognitiveSupport;
using Mutation.Ui;
using Mutation.Ui.Services;
using Mutation.Ui.Views.SettingsUi;
using Newtonsoft.Json;
using Xunit;

namespace Mutation.Tests;

// Loading, repairing and saving interval timers (issue #456).
[Collection(ErrorLoggerCollection.Name)]
public class IntervalTimerSettingsTests
{
	[Fact]
	public void NewTimer_UsesTheBundledDefaultSound()
	{
		Assert.Equal("CustomAudio/mixkit-mouse-hard-clicking-1111.wav", new IntervalTimerSettings().SoundFile);
	}

	[Fact]
	public void Normalize_GivesBlankNamesADefaultName()
	{
		var timers = new List<IntervalTimerSettings>
		{
			new() { Name = "Timer 1" },
			new() { Name = "  " },
		};

		Assert.True(IntervalTimerSettingsNormalizer.Normalize(timers));

		Assert.Equal("Timer 2", timers[1].Name);
	}

	[Fact]
	public void Normalize_GivesADuplicatedIdANewOne()
	{
		var timers = new List<IntervalTimerSettings>
		{
			new() { Id = "same", Name = "A" },
			new() { Id = "same", Name = "B" },
		};

		Assert.True(IntervalTimerSettingsNormalizer.Normalize(timers));

		Assert.Equal("same", timers[0].Id);
		Assert.NotEqual("same", timers[1].Id);
	}

	[Theory]
	[InlineData(0, 1)]
	[InlineData(-5, 1)]
	[InlineData(5000, 1440)]
	public void Normalize_ClampsTheInterval(int stored, int expected)
	{
		var timers = new List<IntervalTimerSettings> { new() { Name = "A", IntervalMinutes = stored } };

		Assert.True(IntervalTimerSettingsNormalizer.Normalize(timers));

		Assert.Equal(expected, timers[0].IntervalMinutes);
	}

	[Fact]
	public void Normalize_DropsNullEntries()
	{
		var timers = new List<IntervalTimerSettings> { null!, new() { Name = "A" } };

		Assert.True(IntervalTimerSettingsNormalizer.Normalize(timers));

		Assert.Single(timers);
	}

	[Fact]
	public void Normalize_LeavesGoodTimersAlone()
	{
		var timers = new List<IntervalTimerSettings> { new() { Name = "A", IntervalMinutes = 45 } };

		Assert.False(IntervalTimerSettingsNormalizer.Normalize(timers));
	}

	[Fact]
	public void EnsureSettings_RepairsAMissingList_AndSeedsNoTimers()
	{
		using var file = new TempSettingsFile("timers-null", "{}");
		var settings = new Settings { IntervalTimers = null! };

		new SettingsManager(file.FilePath).EnsureSettings(settings, isNewFile: true);

		Assert.NotNull(settings.IntervalTimers);
		Assert.Empty(settings.IntervalTimers);
	}

	[Fact]
	public void SaveAndLoad_KeepsEveryTimerField()
	{
		using var file = new TempSettingsFile("timers-roundtrip", "{}");
		var manager = new SettingsManager(file.FilePath);
		var settings = manager.LoadAndEnsureSettings();
		var due = new System.DateTime(2026, 11, 21, 10, 45, 0, System.DateTimeKind.Utc);
		settings.IntervalTimers.Add(new IntervalTimerSettings
		{
			Id = "abc",
			Name = "Stretch",
			Enabled = true,
			IntervalMinutes = 45,
			SoundFile = "CustomAudio/x.wav",
			NextDueUtc = due,
		});

		manager.SaveSettingsToFile(settings);
		var loaded = new SettingsManager(file.FilePath).LoadAndEnsureSettings();

		var timer = Assert.Single(loaded.IntervalTimers);
		Assert.Equal("abc", timer.Id);
		Assert.Equal("Stretch", timer.Name);
		Assert.True(timer.Enabled);
		Assert.Equal(45, timer.IntervalMinutes);
		Assert.Equal("CustomAudio/x.wav", timer.SoundFile);
		Assert.Equal(due, timer.NextDueUtc!.Value.ToUniversalTime());
	}

	[Fact]
	public void SettingsDialogSave_CommitsAddedReorderedAndRemovedTimers_IntoTheSameList()
	{
		var live = new Settings();
		live.IntervalTimers.Add(new IntervalTimerSettings { Id = "a", Name = "A" });
		live.IntervalTimers.Add(new IntervalTimerSettings { Id = "b", Name = "B" });
		var liveList = live.IntervalTimers;

		var copy = SettingsWorkingCopy.Clone(live);
		copy.IntervalTimers.Reverse();
		copy.IntervalTimers.RemoveAt(1);
		copy.IntervalTimers.Add(new IntervalTimerSettings { Id = "c", Name = "C", IntervalMinutes = 5 });

		SettingsWorkingCopy.CommitInto(live, copy);

		Assert.Same(liveList, live.IntervalTimers);
		Assert.Equal(new[] { "b", "c" }, live.IntervalTimers.ConvertAll(t => t.Id));
		Assert.Equal(5, live.IntervalTimers[1].IntervalMinutes);
	}

	[Fact]
	public void AudioFilePathResolver_RejectsNetworkPaths()
	{
		Assert.Equal(string.Empty, AudioFilePathResolver.Resolve(@"\\server\share\a.wav"));
	}

	[Fact]
	public void AudioFilePathResolver_ResolvesRelativePathsAgainstTheAppFolder()
	{
		string resolved = AudioFilePathResolver.Resolve("CustomAudio/a.wav");

		Assert.Equal(Path.GetFullPath(Path.Combine(System.AppContext.BaseDirectory, "CustomAudio", "a.wav")), resolved);
	}
}
