using System.IO;
using CognitiveSupport;

namespace Mutation.Tests;

/// <summary>
/// Covers <see cref="SilenceRemovalLog"/>, which carries the silence-removal points from the
/// preprocessing pass through to whoever transcribes the file.
/// <para>
/// It used to compare the two paths as raw strings, so a caller that spelled the recording
/// differently from the way preprocessing recorded it got no points back and chunking fell
/// back to cutting at the furthest safe position — quietly, and differently from every other
/// path compare in the app (issue #324).
/// </para>
/// </summary>
public class SilenceRemovalLogTests
{
	private static string Temp(params string[] parts) =>
		Path.Combine(Path.GetTempPath(), Path.Combine(parts));

	private static SilenceRemovalPoint[] OnePoint() =>
		new[] { new SilenceRemovalPoint(TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(2)) };

	[Fact]
	public void The_file_just_recorded_gets_its_points_back()
	{
		var log = new SilenceRemovalLog();
		var points = OnePoint();

		log.Record(Temp("session.ogg"), points);

		Assert.Equal(points, log.PointsFor(Temp("session.ogg")));
	}

	[Theory]
	[InlineData("SESSION.ogg")]
	[InlineData("sub/../session.ogg")]
	public void The_same_file_spelled_another_way_is_still_that_file(string spelling)
	{
		var log = new SilenceRemovalLog();
		var points = OnePoint();
		log.Record(Temp("session.ogg"), points);

		Assert.Equal(points, log.PointsFor(Temp(spelling)));
	}

	[Fact]
	public void Another_file_gets_nothing()
	{
		var log = new SilenceRemovalLog();
		log.Record(Temp("session.ogg"), OnePoint());

		Assert.Empty(log.PointsFor(Temp("other.ogg")));
	}

	[Fact]
	public void The_same_name_in_another_folder_gets_nothing()
	{
		var log = new SilenceRemovalLog();
		log.Record(Temp("a", "session.ogg"), OnePoint());

		Assert.Empty(log.PointsFor(Temp("b", "session.ogg")));
	}

	[Theory]
	[InlineData("")]
	[InlineData("   ")]
	public void A_blank_path_gets_nothing(string path)
	{
		var log = new SilenceRemovalLog();
		log.Record(Temp("session.ogg"), OnePoint());

		Assert.Empty(log.PointsFor(path));
	}

	[Fact]
	public void Nothing_recorded_yet_means_no_points_for_anyone()
	{
		Assert.Empty(new SilenceRemovalLog().PointsFor(Temp("session.ogg")));
	}

	[Fact]
	public void A_later_recording_replaces_the_earlier_one()
	{
		var log = new SilenceRemovalLog();
		var second = OnePoint();

		log.Record(Temp("first.ogg"), OnePoint());
		log.Record(Temp("second.ogg"), second);

		Assert.Equal(second, log.PointsFor(Temp("second.ogg")));
		Assert.Empty(log.PointsFor(Temp("first.ogg")));
	}
}
