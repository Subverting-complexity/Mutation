using System.IO;
using CognitiveSupport;

namespace Mutation.Tests;

/// <summary>
/// Covers <see cref="PathEquality"/>, the one answer to "do these two strings name the same
/// file?". There used to be three, and one of them — the private helper in
/// <c>SpeechToTextManager</c> — skipped the <see cref="Path.GetFullPath(string)"/> step, so
/// two spellings of one recording compared equal in the session list and unequal in the
/// manager that built it (issue #306).
/// <para>
/// Getting this wrong is quiet either way: too strict and a recording appears twice in the
/// list, too loose and the wrong one is played.
/// </para>
/// </summary>
public class PathEqualityTests
{
	private static string Temp(params string[] parts) =>
		Path.Combine(Path.GetTempPath(), Path.Combine(parts));

	[Fact]
	public void The_same_file_written_two_ways_is_one_file()
	{
		Assert.True(PathEquality.SamePath(Temp("recording.ogg"), Temp("sub", "..", "RECORDING.ogg")));
	}

	[Fact]
	public void Case_alone_does_not_make_two_files()
	{
		// Windows file names are case-insensitive, and the path a caller remembered need not
		// match the casing a directory scan hands back.
		Assert.True(PathEquality.SamePath(Temp("Recording.ogg"), Temp("recording.ogg")));
	}

	[Fact]
	public void A_relative_path_matches_the_absolute_one_it_resolves_to()
	{
		string fileName = "relative-recording.ogg";

		Assert.True(PathEquality.SamePath(fileName, Path.Combine(Directory.GetCurrentDirectory(), fileName)));
	}

	[Fact]
	public void A_forward_slash_spelling_matches_a_backslash_one()
	{
		Assert.True(PathEquality.SamePath(@"C:\Temp\Mutation\a.ogg", "C:/Temp/Mutation/a.ogg"));
	}

	[Fact]
	public void Different_files_stay_different()
	{
		Assert.False(PathEquality.SamePath(Temp("one.ogg"), Temp("two.ogg")));
	}

	[Fact]
	public void The_same_name_in_two_folders_stays_two_files()
	{
		Assert.False(PathEquality.SamePath(Temp("a", "session.ogg"), Temp("b", "session.ogg")));
	}

	[Theory]
	[InlineData(null, "c:/a.ogg")]
	[InlineData("c:/a.ogg", null)]
	[InlineData(null, null)]
	[InlineData("", "")]
	[InlineData("   ", "   ")]
	public void A_missing_path_matches_nothing(string? first, string? second)
	{
		// Two sessions with no path are not "the same recording" — treating them as equal
		// would make an unsaved session match whatever else has no path.
		Assert.False(PathEquality.SamePath(first, second));
	}

	[Fact]
	public void A_path_Windows_will_not_resolve_is_compared_as_written_rather_than_throwing()
	{
		// A settings file holds whatever the user typed. Session refresh runs this over every
		// file it lists, so a throw here would empty the list rather than skip one bad entry.
		const string unresolvable = "C:\\Temp\\bad\0name.ogg";

		Assert.Throws<System.ArgumentException>(() => Path.GetFullPath(unresolvable));

		Assert.True(PathEquality.SamePath(unresolvable, unresolvable));
		Assert.False(PathEquality.SamePath(unresolvable, "C:\\Temp\\other\0name.ogg"));
	}
}
