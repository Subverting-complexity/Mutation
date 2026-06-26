using System;
using System.Collections.Generic;
using System.Linq;
using Mutation.Ui;
using Mutation.Ui.Core;

namespace Mutation.Tests;

// Verifies the pure retention-cleanup decision logic: which session files get deleted
// for a chosen retention count, oldest first, never touching excluded (active) sessions.
public class SessionRetentionPlannerTests
{
	private static List<SpeechSession> MakeSessions(int count)
	{
		// Ascending timestamps so index 0 is the oldest and index count-1 the newest.
		var baseTime = new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc);
		return Enumerable.Range(0, count)
			.Select(i => new SpeechSession($@"C:\sessions\session_{i:000}.wav", baseTime.AddMinutes(i)))
			.ToList();
	}

	private static HashSet<string> NoExclusions() => new(StringComparer.OrdinalIgnoreCase);

	[Theory]
	[InlineData(15, 5)]
	[InlineData(11, 10)]
	[InlineData(500, 1)]
	public void RetainsExactlyN_DeletingOldestFirst(int total, int maxRetained)
	{
		var sessions = MakeSessions(total);

		var toDelete = SessionRetentionPlanner.SelectSessionsToDelete(sessions, NoExclusions(), maxRetained);

		// Exactly the surplus is deleted, and the survivors are the N most-recent.
		Assert.Equal(total - maxRetained, toDelete.Count);
		var deletedPaths = toDelete.Select(s => s.FilePath).ToHashSet();
		var survivors = sessions.Where(s => !deletedPaths.Contains(s.FilePath)).ToList();
		Assert.Equal(maxRetained, survivors.Count);
		var expectedSurvivors = sessions.OrderByDescending(s => s.Timestamp).Take(maxRetained).Select(s => s.FilePath).ToHashSet();
		Assert.True(survivors.All(s => expectedSurvivors.Contains(s.FilePath)));

		// Deletions are ordered oldest first.
		var orderedOldestFirst = toDelete.OrderBy(s => s.Timestamp).Select(s => s.FilePath).ToList();
		Assert.Equal(orderedOldestFirst, toDelete.Select(s => s.FilePath).ToList());
	}

	[Fact]
	public void UnderOrAtCap_DeletesNothing()
	{
		var sessions = MakeSessions(8);

		Assert.Empty(SessionRetentionPlanner.SelectSessionsToDelete(sessions, NoExclusions(), 10));
		Assert.Empty(SessionRetentionPlanner.SelectSessionsToDelete(sessions, NoExclusions(), 8));
	}

	[Fact]
	public void ExcludedActiveSession_IsNeverDeleted_AndStillCountsTowardRetention()
	{
		var sessions = MakeSessions(15);
		// Exclude the oldest, as if it were the active recording.
		var active = sessions[0];
		var exclusions = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { active.FilePath };

		var toDelete = SessionRetentionPlanner.SelectSessionsToDelete(sessions, exclusions, 5);

		Assert.DoesNotContain(active.FilePath, toDelete.Select(s => s.FilePath));
		// 15 sessions, cap 5: ten of the non-excluded oldest are removed; the active one is kept.
		Assert.Equal(10, toDelete.Count);
		var survivors = sessions.Where(s => toDelete.All(d => d.FilePath != s.FilePath)).ToList();
		Assert.Contains(active.FilePath, survivors.Select(s => s.FilePath));
		Assert.Equal(5, survivors.Count);
	}

	[Fact]
	public void EmptyInput_DeletesNothing()
	{
		Assert.Empty(SessionRetentionPlanner.SelectSessionsToDelete(new List<SpeechSession>(), NoExclusions(), 10));
	}
}
