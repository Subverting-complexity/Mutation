using System;
using System.IO;
using CognitiveSupport;
using Mutation.Ui.Services;
using Xunit;

namespace Mutation.Tests;

// The SettingsManager half of the custom-beep check: what it does to the settings, and
// what it hands the caller to show the user. The rule itself lives in
// CustomBeepFileValidatorTests.
//
// Loading and saving settings replaces ErrorLogger's process-wide secret snapshot, so
// this class shares the collection that serialises those statics (issue #250).
[Collection(ErrorLoggerCollection.Name)]
public class CustomBeepIssueReportingTests : IDisposable
{
	private readonly string _dir;
	private readonly SettingsManager _manager;

	public CustomBeepIssueReportingTests()
	{
		_dir = Path.Combine(Path.GetTempPath(), "MutationCustomBeepIssueTests_" + Guid.NewGuid().ToString("N"));
		Directory.CreateDirectory(_dir);
		_manager = new SettingsManager(Path.Combine(_dir, "Mutation.json"));
	}

	public void Dispose()
	{
		try { Directory.Delete(_dir, recursive: true); } catch { /* best-effort cleanup */ }
	}

	private static Settings WithCustomBeeps(string endFile)
	{
		var settings = new Settings
		{
			AudioSettings = new AudioSettings
			{
				CustomBeepSettings = new AudioSettings.CustomBeepSettingsData
				{
					UseCustomBeeps = true,
					BeepSuccessFile = "./CustomAudio/Success.wav",
					BeepFailureFile = "./CustomAudio/Failure.wav",
					BeepStartFile = "./CustomAudio/Start.wav",
					BeepEndFile = endFile,
					BeepMuteFile = "./CustomAudio/Mute.wav",
					BeepUnmuteFile = "./CustomAudio/Unmute.wav",
				},
			},
		};
		return settings;
	}

	// The real CustomAudio\*.wav files sit next to the test host, so the settings built
	// above are the healthy case; this name is the one guaranteed not to resolve.
	private const string MissingFile = "./CustomAudio/NoSuchSound.wav";

	[Fact]
	public void HealthyBeepFiles_ReportNothing_AndLeaveTheToggleOn()
	{
		var settings = WithCustomBeeps("./CustomAudio/End.wav");

		_manager.EnsureSettings(settings, isNewFile: false);

		Assert.Empty(_manager.CustomBeepIssues);
		Assert.True(settings.AudioSettings!.CustomBeepSettings!.UseCustomBeeps);
	}

	[Fact]
	public void AMissingBeepFile_IsReported_AndCustomBeepsAreSwitchedOff()
	{
		var settings = WithCustomBeeps(MissingFile);

		_manager.EnsureSettings(settings, isNewFile: false);

		Assert.Contains(_manager.CustomBeepIssues, i => i == $"Could not load end beep file: {MissingFile}");
		Assert.False(settings.AudioSettings!.CustomBeepSettings!.UseCustomBeeps);
	}

	// Non-empty issues must make EnsureSettings report a change, or LoadAndEnsureSettings
	// never writes the file back and custom beeps switch themselves off again on every
	// single launch. Settled first, so the assertion is about the beep file alone and not
	// about the defaults a fresh Settings object always picks up.
	[Fact]
	public void AMissingBeepFile_MarksTheSettingsAsChangedSoTheyAreSavedBack()
	{
		var settings = WithCustomBeeps("./CustomAudio/End.wav");
		_manager.EnsureSettings(settings, isNewFile: false);
		Assert.False(_manager.EnsureSettings(settings, isNewFile: false));

		settings.AudioSettings!.CustomBeepSettings!.BeepEndFile = MissingFile;

		Assert.True(_manager.EnsureSettings(settings, isNewFile: false));
	}

	[Fact]
	public void CustomBeepsSwitchedOff_ReportsNothing_AndLeavesTheToggleAlone()
	{
		var settings = WithCustomBeeps(MissingFile);
		settings.AudioSettings!.CustomBeepSettings!.UseCustomBeeps = false;

		_manager.EnsureSettings(settings, isNewFile: false);

		Assert.Empty(_manager.CustomBeepIssues);
		Assert.False(settings.AudioSettings.CustomBeepSettings.UseCustomBeeps);
	}

	// A later load that finds nothing wrong has to clear the previous run's issues, or
	// startup keeps raising a notice about a problem the user has already fixed.
	[Fact]
	public void ALaterCleanLoad_ClearsThePreviousIssues()
	{
		_manager.EnsureSettings(WithCustomBeeps(MissingFile), isNewFile: false);
		Assert.NotEmpty(_manager.CustomBeepIssues);

		_manager.EnsureSettings(WithCustomBeeps("./CustomAudio/End.wav"), isNewFile: false);

		Assert.Empty(_manager.CustomBeepIssues);
	}

	[Fact]
	public void AWrongExtension_IsReportedAsAnExtensionProblem()
	{
		var settings = WithCustomBeeps("./CustomAudio/End.mp3");

		_manager.EnsureSettings(settings, isNewFile: false);

		Assert.Contains(_manager.CustomBeepIssues, i => i == "Custom end beep must be a .wav file: ./CustomAudio/End.mp3");
	}

	// Nothing reaches the user from here — the notice is raised from startup, once the
	// window can host an accessible dialog (issue #275).
	[Fact]
	public void NoBeepSettingsAtAll_IsHandledWithoutThrowing()
	{
		var settings = new Settings();

		_manager.EnsureSettings(settings, isNewFile: false);

		Assert.Empty(_manager.CustomBeepIssues);
	}
}
