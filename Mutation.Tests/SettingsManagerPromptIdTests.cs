using System;
using System.IO;
using System.Linq;
using CognitiveSupport;
using Mutation.Ui.Services;

namespace Mutation.Tests;

/// <summary>
/// Loading is where the repair has to happen: the app must never hold two prompts that
/// look like the same prompt, whatever a hand-edited Mutation.json says.
/// </summary>
public class SettingsManagerPromptIdTests : IDisposable
{
	private readonly string _tempPath;

	public SettingsManagerPromptIdTests()
	{
		_tempPath = Path.Combine(Path.GetTempPath(), $"mutation-promptid-{Guid.NewGuid():N}.json");
	}

	public void Dispose()
	{
		if (File.Exists(_tempPath))
			File.Delete(_tempPath);
	}

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
	public void BackfilledIdsArePersisted_SoTheySurviveARestart()
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

		var first = Load().LlmSettings!.Prompts.ToDictionary(p => p.Name, p => p.Id);
		var second = Load().LlmSettings!.Prompts.ToDictionary(p => p.Name, p => p.Id);

		Assert.Equal(first, second);
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
