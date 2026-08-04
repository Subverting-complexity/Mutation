using System;
using System.Collections.Generic;
using System.Linq;
using CognitiveSupport;
using Xunit;

namespace Mutation.Tests;

public class CustomBeepFileValidatorTests
{
	private static AudioSettings.CustomBeepSettingsData AllValid() => new()
	{
		UseCustomBeeps = true,
		BeepSuccessFile = "./CustomAudio/Success.wav",
		BeepFailureFile = "./CustomAudio/Failure.wav",
		BeepStartFile = "./CustomAudio/Start.wav",
		BeepEndFile = "./CustomAudio/End.wav",
		BeepMuteFile = "./CustomAudio/Mute.wav",
		BeepUnmuteFile = "./CustomAudio/Unmute.wav",
	};

	private static readonly Func<string, bool> EverythingExists = static _ => true;
	private static readonly Func<string, bool> NothingExists = static _ => false;

	[Fact]
	public void AllSixResolveToAnExistingWav_ReportsNothing()
	{
		Assert.Empty(CustomBeepFileValidator.Validate(AllValid(), EverythingExists));
	}

	// Nothing is being loaded, so there is nothing to complain about — a user who has
	// custom beeps switched off should not be nagged about paths they are not using.
	[Fact]
	public void CustomBeepsSwitchedOff_ReportsNothing_EvenWhenEveryFileIsMissing()
	{
		var settings = AllValid();
		settings.UseCustomBeeps = false;

		Assert.Empty(CustomBeepFileValidator.Validate(settings, NothingExists));
	}

	[Fact]
	public void NullSettings_ReportsNothing()
	{
		Assert.Empty(CustomBeepFileValidator.Validate(null, NothingExists));
	}

	[Fact]
	public void EveryFileMissing_ReportsOnePerFile()
	{
		var issues = CustomBeepFileValidator.Validate(AllValid(), NothingExists);

		Assert.Equal(6, issues.Count);
		Assert.All(issues, i => Assert.StartsWith("Could not load ", i));
	}

	// A file that is present but is not a .wav gets its own wording: "could not load"
	// sends the user hunting for a missing file that is sitting right there.
	[Fact]
	public void WrongExtension_IsReportedAsAnExtensionProblemNotAMissingFile()
	{
		var settings = AllValid();
		settings.BeepEndFile = "./CustomAudio/End.mp3";

		var issues = CustomBeepFileValidator.Validate(settings, EverythingExists);

		Assert.Equal(new[] { "Custom end beep must be a .wav file: ./CustomAudio/End.mp3" }, issues);
	}

	[Fact]
	public void UppercaseExtension_IsAccepted()
	{
		var settings = AllValid();
		settings.BeepStartFile = "./CustomAudio/Start.WAV";

		Assert.Empty(CustomBeepFileValidator.Validate(settings, EverythingExists));
	}

	[Theory]
	[InlineData(null)]
	[InlineData("")]
	[InlineData("   ")]
	public void BlankPath_IsReportedAsUnloadable(string? path)
	{
		var settings = AllValid();
		settings.BeepMuteFile = path;

		var issues = CustomBeepFileValidator.Validate(settings, EverythingExists);

		Assert.Single(issues);
		Assert.StartsWith("Could not load mute beep file:", issues[0]);
	}

	// The existence check runs against the resolved path, not the raw setting: a
	// relative path is resolved against the executable directory.
	[Fact]
	public void ExistenceIsCheckedAgainstTheResolvedPath()
	{
		var settings = AllValid();
		var probed = new List<string>();

		CustomBeepFileValidator.Validate(settings, path => { probed.Add(path); return true; });

		Assert.Equal(6, probed.Count);
		Assert.All(probed, p => Assert.True(System.IO.Path.IsPathRooted(p), $"'{p}' was not resolved"));
	}

	// Every label appears, so a user reading the list can tell which sound is broken.
	[Fact]
	public void EachBeepIsNamedInItsOwnMessage()
	{
		var issues = CustomBeepFileValidator.Validate(AllValid(), NothingExists);

		foreach (string label in new[] { "success", "failure", "start", "end", "mute", "unmute" })
			Assert.Contains(issues, i => i.Contains($" {label} beep ", StringComparison.Ordinal));
	}

	[Fact]
	public void MissingExistenceCheck_IsRejected()
	{
		Assert.Throws<ArgumentNullException>(() => CustomBeepFileValidator.Validate(AllValid(), null!));
	}
}
