using System;
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

	// A path that names a variable is kept as typed, so it keeps following the variable
	// — the whole point of typing one.
	[Theory]
	[InlineData(@"%USERPROFILE%\Recordings")]
	[InlineData(@"%LOCALAPPDATA%\Mutation")]
	[InlineData(@"%userprofile%\Recordings\")]
	[InlineData(@"~\Recordings")]
	[InlineData("~/Recordings")]
	[InlineData("~")]
	public void Normalize_PathWithVariables_IsKeptAsTyped(string value)
	{
		var result = TempDirectorySetting.Normalize("  " + value + " ");

		Assert.Equal(value, result.Path);
		Assert.False(result.WasRepaired);
	}

	[Fact]
	public void Resolve_ExpandsTheHomeFolderVariable()
	{
		string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

		Assert.Equal(Path.Combine(home, "Recordings"), TempDirectorySetting.Resolve(@"%USERPROFILE%\Recordings"));
		Assert.Equal(home + @"\Recordings", TempDirectorySetting.Resolve(@"~\Recordings"));
		Assert.Equal(home, TempDirectorySetting.Resolve("~"));
	}

	[Fact]
	public void Resolve_PlainPath_IsUnchanged()
	{
		Assert.Equal(@"D:\Recordings", TempDirectorySetting.Resolve(@"D:\Recordings"));
	}

	// A misspelt variable is left in by Windows and would become a folder literally named
	// "%USERPROFILES%", so it is refused rather than stored.
	[Fact]
	public void Normalize_UnknownVariable_FallsBackToTheDefault()
	{
		var result = TempDirectorySetting.Normalize(@"%MUTATION_NO_SUCH_VARIABLE%\Recordings");

		Assert.Equal(SettingsDefaults.Speech.TempDirectory, result.Path);
		Assert.True(result.WasRepaired);
		Assert.Contains("variable Windows does not know", result.Problem);
	}

	// Once the value relies on one variable, a leftover %NAME% beside it is a typo, not a
	// folder name, and would otherwise become a folder literally called that.
	[Fact]
	public void Normalize_UnknownVariableBesideAKnownOne_FallsBackToTheDefault()
	{
		var result = TempDirectorySetting.Normalize(@"%USERPROFILE%\%MUTATION_NO_SUCH_VARIABLE%");

		Assert.Equal(SettingsDefaults.Speech.TempDirectory, result.Path);
		Assert.Contains("variable Windows does not know", result.Problem);
	}

	// Windows allows '%' in folder names, so a full path holding a pair of them is a real
	// folder, not a misspelt variable, and a setting that worked before is left alone.
	[Fact]
	public void Normalize_FullPathWithPercentSigns_IsKept()
	{
		var result = TempDirectorySetting.Normalize(@"D:\Reports %MUTATION_NO_SUCH_VARIABLE%");

		Assert.Equal(@"D:\Reports %MUTATION_NO_SUCH_VARIABLE%", result.Path);
		Assert.False(result.WasRepaired);
	}

	// A variable that expands to something relative is no better than a relative path:
	// recordings would land next to the executable (issue #230).
	[Fact]
	public void Normalize_VariableThatExpandsToARelativePath_FallsBackToTheDefault()
	{
		const string name = "MUTATION_TEST_RELATIVE_FOLDER";
		Environment.SetEnvironmentVariable(name, "Recordings");
		try
		{
			var result = TempDirectorySetting.Normalize($"%{name}%\\Mutation");

			Assert.Equal(SettingsDefaults.Speech.TempDirectory, result.Path);
			Assert.Contains("not a full path", result.Problem);
		}
		finally
		{
			Environment.SetEnvironmentVariable(name, null);
		}
	}

	// A path with the '~' anywhere but the start is an ordinary folder name, like the
	// short names Windows gives long ones (PROGRA~1), and is left alone.
	[Fact]
	public void Normalize_TildeInsideAPath_IsAnOrdinaryPath()
	{
		var result = TempDirectorySetting.Normalize(@"C:\PROGRA~1\Mutation");

		Assert.Equal(@"C:\PROGRA~1\Mutation", result.Path);
		Assert.False(result.WasRepaired);
	}

	// When the fallback names a variable, the message shows the real folder too, so the
	// user is not left to work out what %USERPROFILE% means on their machine.
	[Fact]
	public void ComposeMessage_ExpandsAReplacementWithVariables()
	{
		string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

		string message = TempDirectorySetting.ComposeMessage("Bad.", @"%USERPROFILE%\Mutation");

		Assert.Contains($@"%USERPROFILE%\Mutation ({Path.Combine(home, "Mutation")})", message);
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

		string sessions = Path.Combine(TempDirectorySetting.Resolve(result.Path), "Sessions");

		Assert.True(Path.IsPathFullyQualified(sessions));
	}
}
