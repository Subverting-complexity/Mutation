using System;
using System.IO;
using System.Linq;
using CognitiveSupport;
using Mutation.Ui.Services;
using Newtonsoft.Json.Linq;

namespace Mutation.Tests;

/// <summary>
/// Loading is where the repair has to happen: the app must never hold two prompts that
/// look like the same prompt, whatever a hand-edited Mutation.json says.
/// </summary>
// Loading and saving settings replaces ErrorLogger's process-wide secret snapshot, so
// this class shares the collection that serialises those statics (issue #250).
[Collection(ErrorLoggerCollection.Name)]
public class SettingsManagerPromptIdTests : IDisposable
{
	private readonly TempSettingsFile _settingsFile = new("promptid");

	private string _tempPath => _settingsFile.FilePath;

	public void Dispose() => _settingsFile.Dispose();

	private Settings Load() => new SettingsManager(_tempPath).LoadAndEnsureSettings();

	[Fact]
	public void HandWrittenPromptsWithoutIds_LoadWithDistinctIds()
	{
		File.WriteAllText(_tempPath, """
			{
				"LlmSettings": {
					"Prompts": [
						{ "Name": "Fix grammar", "Content": "a", "ModelName": "gpt-4.1" },
						{ "Name": "Summarize",   "Content": "b", "ModelName": "gpt-4.1" },
						{ "Name": "Translate",   "Content": "c", "ModelName": "gpt-4.1" }
					]
				}
			}
			""");

		var prompts = Load().LlmSettings!.Prompts;

		Assert.Equal(3, prompts.Count);
		Assert.All(prompts, p => Assert.True(p.Id > 0));
		Assert.Equal(3, prompts.Select(p => p.Id).Distinct().Count());
	}

	[Fact]
	public void BackfilledIdsAreWrittenBackToTheFile()
	{
		File.WriteAllText(_tempPath, """
			{
				"LlmSettings": {
					"Prompts": [
						{ "Name": "Fix grammar", "Content": "a", "ModelName": "gpt-4.1" },
						{ "Name": "Summarize",   "Content": "b", "ModelName": "gpt-4.1" }
					]
				}
			}
			""");

		var loaded = Load().LlmSettings!.Prompts.ToDictionary(p => p.Name!, p => p.Id);

		// Asserted against the file, not against a second load: assignment is
		// deterministic on list order, so comparing two loads would pass even if
		// nothing were ever persisted.
		var written = JObject.Parse(File.ReadAllText(_tempPath));
		var prompts = (JArray)written["LlmSettings"]!["Prompts"]!;
		foreach (var prompt in prompts)
		{
			string name = prompt["Name"]!.Value<string>()!;
			Assert.Equal(loaded[name], prompt["Id"]!.Value<int>());
		}
		Assert.Equal(2, prompts.Select(p => p["Id"]!.Value<int>()).Distinct().Count());
	}

	[Fact]
	public void ASettledFileIsNotRewrittenOnTheNextStart()
	{
		// The backfill must be idempotent end-to-end, not just in the helper: a settings
		// file rewritten on every launch would be a regression in its own right.
		File.WriteAllText(_tempPath, """
			{
				"LlmSettings": {
					"Prompts": [
						{ "Name": "Fix grammar", "Content": "a", "ModelName": "gpt-4.1" },
						{ "Name": "Summarize",   "Content": "b", "ModelName": "gpt-4.1" }
					]
				}
			}
			""");

		Load(); // First start fills in the Ids (and any other defaults) and saves.
		byte[] settled = File.ReadAllBytes(_tempPath);

		Load();

		Assert.Equal(settled, File.ReadAllBytes(_tempPath));
	}

	[Fact]
	public void ExistingIdsAreLeftAlone()
	{
		File.WriteAllText(_tempPath, """
			{
				"LlmSettings": {
					"Prompts": [
						{ "Id": 4, "Name": "Fix grammar", "Content": "a", "ModelName": "gpt-4.1" },
						{ "Id": 9, "Name": "Summarize",   "Content": "b", "ModelName": "gpt-4.1" }
					]
				}
			}
			""");

		var prompts = Load().LlmSettings!.Prompts;

		Assert.Equal(4, prompts.Single(p => p.Name == "Fix grammar").Id);
		Assert.Equal(9, prompts.Single(p => p.Name == "Summarize").Id);
	}

	[Fact]
	public void DuplicateIdsAreSplitApart()
	{
		File.WriteAllText(_tempPath, """
			{
				"LlmSettings": {
					"Prompts": [
						{ "Id": 1, "Name": "Fix grammar", "Content": "a", "ModelName": "gpt-4.1" },
						{ "Id": 1, "Name": "Summarize",   "Content": "b", "ModelName": "gpt-4.1" }
					]
				}
			}
			""");

		var prompts = Load().LlmSettings!.Prompts;

		Assert.Equal(2, prompts.Select(p => p.Id).Distinct().Count());
	}

	[Fact]
	public void PerPromptStateIsNoLongerSharedAcrossHandWrittenPrompts()
	{
		// The bug this fixes: FastModeNoticeTracker keys on prompt Id, so with every
		// prompt at 0 the first Fast mode notice silenced the notice for all the others.
		File.WriteAllText(_tempPath, """
			{
				"LlmSettings": {
					"Prompts": [
						{ "Name": "First",  "Content": "a", "ModelName": "claude-opus-5" },
						{ "Name": "Second", "Content": "b", "ModelName": "claude-opus-5" }
					]
				}
			}
			""");
		var prompts = Load().LlmSettings!.Prompts;
		var tracker = new FastModeNoticeTracker();

		Assert.True(tracker.ShouldAnnounce(prompts[0].Id, FastModeFallbackReason.Unavailable));
		Assert.True(tracker.ShouldAnnounce(prompts[1].Id, FastModeFallbackReason.Unavailable));
	}
}
