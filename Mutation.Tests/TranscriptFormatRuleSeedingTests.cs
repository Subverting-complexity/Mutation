using System.Linq;
using CognitiveSupport;
using Mutation.Ui.Services;
using Newtonsoft.Json.Linq;
using Xunit;

namespace Mutation.Tests;

// Turning transcript formatting off has to stick. The starter rules used to be
// re-seeded in memory whenever the list was empty, without marking the settings as
// changed — so the file kept saying [] while the app kept applying all 16 rules, and
// the Settings page kept showing them back (issue #220).
//
// LoadAndEnsureSettings feeds the loaded keys to ErrorLogger's process-wide redactor,
// so this class shares the collection that serialises those statics.
[Collection(ErrorLoggerCollection.Name)]
public class TranscriptFormatRuleSeedingTests
{
	private static JArray? RulesInFile(string path) =>
		JObject.Parse(System.IO.File.ReadAllText(path))["TranscriptFormatRules"] as JArray;

	[Fact]
	public void Load_EmptyRuleList_StaysEmptyInMemory()
	{
		using var file = new TempSettingsFile("rule-seed", """{ "TranscriptFormatRules": [] }""");

		var settings = new SettingsManager(file.FilePath).LoadAndEnsureSettings();

		Assert.Empty(settings.TranscriptFormatRules);
	}

	[Fact]
	public void Load_EmptyRuleList_StaysEmptyOnDisk()
	{
		using var file = new TempSettingsFile("rule-seed", """{ "TranscriptFormatRules": [] }""");

		new SettingsManager(file.FilePath).LoadAndEnsureSettings();

		// Other repairs may rewrite the file; what must not happen is the defaults
		// coming back with it.
		Assert.Empty(RulesInFile(file.FilePath)!);
	}

	// The state used to never converge: every launch re-seeded in memory and left the
	// file alone, so the two could not agree no matter how many times it ran.
	[Fact]
	public void Load_EmptyRuleList_ConvergesAcrossLaunches()
	{
		using var file = new TempSettingsFile("rule-seed", """{ "TranscriptFormatRules": [] }""");

		new SettingsManager(file.FilePath).LoadAndEnsureSettings();
		var settings = new SettingsManager(file.FilePath).LoadAndEnsureSettings();

		Assert.Empty(settings.TranscriptFormatRules);
		Assert.Empty(RulesInFile(file.FilePath)!);
	}

	// An empty list is a user choice, not a gap — nothing to repair, nothing to save.
	[Fact]
	public void EnsureSettings_ExistingFileWithNoRules_ReportsNothingMissing()
	{
		var settings = new Settings();
		var manager = new SettingsManager("unused.json");
		manager.EnsureSettings(settings, isNewFile: true);

		settings.TranscriptFormatRules!.Clear();
		bool somethingWasMissing = manager.EnsureSettings(settings, isNewFile: false);

		Assert.False(somethingWasMissing);
		Assert.Empty(settings.TranscriptFormatRules);
	}

	// A first run still gets the starter rules, and gets them written to disk so the
	// Settings page and the file agree from the very beginning.
	[Fact]
	public void FirstRun_SeedsTheStarterRulesAndPersistsThem()
	{
		using var file = new TempSettingsFile("rule-seed");

		var settings = new SettingsManager(file.FilePath).LoadAndEnsureSettings();

		Assert.NotEmpty(settings.TranscriptFormatRules);
		Assert.Contains(settings.TranscriptFormatRules, r => r.Find == "full stop");
		Assert.Equal(settings.TranscriptFormatRules.Count, RulesInFile(file.FilePath)!.Count);
	}

	// An explicit null is not a choice the UI can produce — the rest of the app expects
	// a list, so it is still repaired.
	[Fact]
	public void Load_NullRuleList_IsRepaired()
	{
		using var file = new TempSettingsFile("rule-seed", """{ "TranscriptFormatRules": null }""");

		var settings = new SettingsManager(file.FilePath).LoadAndEnsureSettings();

		Assert.NotEmpty(settings.TranscriptFormatRules);
		Assert.NotEmpty(RulesInFile(file.FilePath)!);
	}

	// The other half of the issue's acceptance criterion. Once the file has settled, an
	// empty rule list is not a gap, so a launch that finds one has nothing to repair and
	// must leave the file alone. A re-seed — or any other needless rewrite — shows up
	// here as a changed timestamp.
	[Fact]
	public void Load_SettledFileWithNoRules_IsNotRewrittenOnTheNextLaunch()
	{
		using var file = new TempSettingsFile("rule-seed");

		var manager = new SettingsManager(file.FilePath);
		var settings = manager.LoadAndEnsureSettings();
		settings.TranscriptFormatRules.Clear();
		manager.SaveSettingsToFile(settings);
		var beforeMtime = System.IO.File.GetLastWriteTimeUtc(file.FilePath);

		new SettingsManager(file.FilePath).LoadAndEnsureSettings();

		Assert.Equal(beforeMtime, System.IO.File.GetLastWriteTimeUtc(file.FilePath));
		Assert.Empty(RulesInFile(file.FilePath)!);
	}

	// The upgrade path. Settings.TranscriptFormatRules is declared with a
	// `= new List<>()` initialiser, so a file written before the feature existed — one
	// with no TranscriptFormatRules key at all — deserialises to an EMPTY list, not a
	// null one. Left to the in-memory check alone, those users would silently lose the
	// starter rules on the upgrade, which is the same disappearing-choice fault as #220
	// pointing the other way.
	[Fact]
	public void Load_FileWithNoRuleKeyAtAll_StillGetsTheStarterRules()
	{
		using var file = new TempSettingsFile("rule-seed", """{ "LlmSettings": { "Prompts": [] } }""");

		var settings = new SettingsManager(file.FilePath).LoadAndEnsureSettings();

		Assert.NotEmpty(settings.TranscriptFormatRules);
		Assert.Contains(settings.TranscriptFormatRules, r => r.Find == "full stop");
		Assert.NotEmpty(RulesInFile(file.FilePath)!);
	}

	// ...and having been seeded once, the key is now present, so deleting every rule
	// still sticks. The upgrade repair must not become a permanent re-seed.
	[Fact]
	public void Load_AfterTheUpgradeSeed_DeletingEveryRuleStillSticks()
	{
		using var file = new TempSettingsFile("rule-seed", """{ "LlmSettings": { "Prompts": [] } }""");

		var manager = new SettingsManager(file.FilePath);
		var seeded = manager.LoadAndEnsureSettings();
		seeded.TranscriptFormatRules.Clear();
		manager.SaveSettingsToFile(seeded);

		var reloaded = new SettingsManager(file.FilePath).LoadAndEnsureSettings();

		Assert.Empty(reloaded.TranscriptFormatRules);
		Assert.Empty(RulesInFile(file.FilePath)!);
	}

	[Fact]
	public void Load_CustomRules_AreLeftAlone()
	{
		using var file = new TempSettingsFile("rule-seed", """
		{
			"TranscriptFormatRules": [
				{ "Find": "widget", "ReplaceWith": "Widget", "CaseSensitive": false, "MatchType": "Plain" }
			]
		}
		""");

		var settings = new SettingsManager(file.FilePath).LoadAndEnsureSettings();

		Assert.Single(settings.TranscriptFormatRules);
		Assert.Equal("widget", settings.TranscriptFormatRules.Single().Find);
	}
}
