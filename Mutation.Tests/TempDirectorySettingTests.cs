using System.IO;
using Mutation.Ui.Views.SettingsUi;
using Xunit;

namespace Mutation.Tests;

// The temp directory is the only free-text path on the settings pages. A blank one
// used to be stored verbatim, and Path.Combine("", "Sessions") then put recordings
// next to the executable — or threw under Program Files (issue #230).
public class TempDirectorySettingTests
{
	[Theory]
	[InlineData("")]
	[InlineData("   ")]
	[InlineData("\t")]
	[InlineData(null)]
	public void Normalize_BlankValue_FallsBackToTheDefault(string? value)
	{
		var result = TempDirectorySetting.Normalize(value);

		Assert.Equal(SettingsDefaults.Speech.TempDirectory, result.Path);
		Assert.True(result.WasRepaired);
		Assert.Contains("cannot be blank", result.Problem);
	}

	[Theory]
	[InlineData("Sessions")]
	[InlineData(@"recordings\mutation")]
	[InlineData(@".\Sessions")]
	[InlineData(@"..\Sessions")]
	public void Normalize_RelativePath_FallsBackToTheDefault(string value)
	{
		var result = TempDirectorySetting.Normalize(value);

		Assert.Equal(SettingsDefaults.Speech.TempDirectory, result.Path);
		Assert.True(result.WasRepaired);
		Assert.Contains("not a full path", result.Problem);
	}

	[Fact]
	public void Normalize_FullPath_IsKept()
	{
		var result = TempDirectorySetting.Normalize(@"C:\Recordings\Mutation");

		Assert.Equal(@"C:\Recordings\Mutation", result.Path);
		Assert.False(result.WasRepaired);
		Assert.Null(result.Problem);
	}

	[Fact]
	public void Normalize_SurroundingWhitespace_IsTrimmed()
	{
		var result = TempDirectorySetting.Normalize("  C:\\Recordings  ");

		Assert.Equal(@"C:\Recordings", result.Path);
		Assert.False(result.WasRepaired);
	}

	// What is stored is what is used, so '..' segments are resolved rather than
	// carried into every Path.Combine downstream.
	[Fact]
	public void Normalize_FullPathWithParentSegments_IsResolved()
	{
		var result = TempDirectorySetting.Normalize(@"C:\Recordings\Old\..\Mutation");

		Assert.Equal(@"C:\Recordings\Mutation", result.Path);
		Assert.False(result.WasRepaired);
	}

	[Fact]
	public void Normalize_UncPath_IsKept()
	{
		var result = TempDirectorySetting.Normalize(@"\\server\share\Mutation");

		Assert.Equal(@"\\server\share\Mutation", result.Path);
		Assert.False(result.WasRepaired);
	}

	[Fact]
	public void Normalize_TheDefault_IsUnchanged()
	{
		var result = TempDirectorySetting.Normalize(SettingsDefaults.Speech.TempDirectory);

		Assert.Equal(SettingsDefaults.Speech.TempDirectory, result.Path);
		Assert.False(result.WasRepaired);
	}

	[Fact]
	public void Normalize_PathWithIllegalCharacters_FallsBackToTheDefault()
	{
		var result = TempDirectorySetting.Normalize("C:\\Record\0ings");

		Assert.Equal(SettingsDefaults.Speech.TempDirectory, result.Path);
		Assert.True(result.WasRepaired);
	}

	// The message has to say where the recordings are going, or "that path was no
	// good" leaves the user with no idea what happened to them.
	[Fact]
	public void ComposeMessage_NamesTheReplacementPath()
	{
		string message = TempDirectorySetting.ComposeMessage(
			"The temp directory cannot be blank.", @"C:\Fallback");

		Assert.StartsWith("The temp directory cannot be blank.", message);
		Assert.Contains(@"Recordings will be stored in C:\Fallback instead.", message);
	}

	// The whole point of the repair: the stored path can be combined into a real
	// absolute Sessions folder, which a blank one could not.
	[Theory]
	[InlineData("")]
	[InlineData("Sessions")]
	public void Normalize_ThenCombine_ProducesAnAbsoluteSessionsPath(string badValue)
	{
		var result = TempDirectorySetting.Normalize(badValue);

		string sessions = Path.Combine(result.Path, "Sessions");

		Assert.True(Path.IsPathFullyQualified(sessions));
	}
}
