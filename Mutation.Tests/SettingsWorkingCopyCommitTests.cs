using System.Collections;
using System.Reflection;
using CognitiveSupport;
using Mutation.Ui.Views.SettingsUi;

namespace Mutation.Tests;

// Issues #218 and #219: Save committed the working copy through a hand-maintained field
// list that had drifted away from the model twice, silently discarding the seven speech
// preprocessing toggles and orphaning the prompt list.
public class SettingsWorkingCopyCommitTests
{
	private static Settings BuildLiveSettings()
	{
		var settings = new Settings
		{
			AudioSettings = new AudioSettings("ALT+Q")
			{
				CustomBeepSettings = new AudioSettings.CustomBeepSettingsData(),
			},
			AzureComputerVisionSettings = new AzureComputerVisionSettings(),
			SpeechToTextSettings = new SpeechToTextSettings(),
			ApiKeys = new ApiKeys(),
			LlmSettings = new LlmSettings(),
			TextToSpeechSettings = new TextToSpeechSettings(),
		};
		settings.LlmSettings.Prompts.Add(new LlmSettings.LlmPrompt { Id = 1, Name = "Tidy", Content = "tidy this" });
		settings.LlmSettings.Prompts.Add(new LlmSettings.LlmPrompt { Id = 2, Name = "Summarise", Content = "summarise this" });
		settings.LlmSettings.Models.Add(new LlmModelConfig { Name = "gpt-4.1" });
		settings.TranscriptFormatRules.Add(new TranscriptFormatRule("a", "b", false, TranscriptFormatRule.MatchTypeEnum.Plain));
		return settings;
	}

	// --- Issue #218: the speech-preprocessing toggles ------------------------------

	public static TheoryData<string> PreprocessingToggleNames() =>
	[
		nameof(TextToSpeechSettings.PreprocessRemoveCodeBlocks),
		nameof(TextToSpeechSettings.PreprocessStripBoldItalicCode),
		nameof(TextToSpeechSettings.PreprocessStripHeadingMarks),
		nameof(TextToSpeechSettings.PreprocessShortenWebLinks),
		nameof(TextToSpeechSettings.PreprocessStripBulletMarkers),
		nameof(TextToSpeechSettings.PreprocessExpandAbbreviations),
		nameof(TextToSpeechSettings.PreprocessNormaliseWhitespace),
	];

	[Theory]
	[MemberData(nameof(PreprocessingToggleNames))]
	public void CommitInto_TransfersEverySpeechPreprocessingToggle(string propertyName)
	{
		var live = BuildLiveSettings();
		var working = SettingsWorkingCopy.Clone(live);
		var property = typeof(TextToSpeechSettings).GetProperty(propertyName)!;

		property.SetValue(working.TextToSpeechSettings, false);
		SettingsWorkingCopy.CommitInto(live, working);

		Assert.False((bool)property.GetValue(live.TextToSpeechSettings!)!, $"{propertyName} was discarded on Save.");
	}

	// --- Issue #219: prompt-list identity -----------------------------------------

	[Fact]
	public void CommitInto_KeepsTheLivePromptListInstance()
	{
		var live = BuildLiveSettings();
		var originalList = live.LlmSettings!.Prompts;
		var working = SettingsWorkingCopy.Clone(live);

		SettingsWorkingCopy.CommitInto(live, working);

		Assert.Same(originalList, live.LlmSettings.Prompts);
	}

	[Fact]
	public void CommitInto_KeepsTheLivePromptInstances()
	{
		var live = BuildLiveSettings();
		var originalPrompt = live.LlmSettings!.Prompts[0];
		var working = SettingsWorkingCopy.Clone(live);

		SettingsWorkingCopy.CommitInto(live, working);

		Assert.Same(originalPrompt, live.LlmSettings.Prompts[0]);
	}

	[Fact]
	public void EditingAPromptAfterASave_SurvivesTheNextWrite()
	{
		// The failure scenario from issue #219: open Settings, Save with no changes,
		// then edit a prompt from the main window. The edit used to land on an object
		// no longer in the live collection.
		var live = BuildLiveSettings();
		var working = SettingsWorkingCopy.Clone(live);
		var promptTheUiIsBoundTo = live.LlmSettings!.Prompts[0];

		SettingsWorkingCopy.CommitInto(live, working);
		promptTheUiIsBoundTo.Content = "edited after save";

		Assert.Equal("edited after save", live.LlmSettings.Prompts[0].Content);
	}

	[Fact]
	public void DeletingAPromptAfterASave_ActuallyRemovesIt()
	{
		var live = BuildLiveSettings();
		var working = SettingsWorkingCopy.Clone(live);
		var promptTheUiIsBoundTo = live.LlmSettings!.Prompts[1];

		SettingsWorkingCopy.CommitInto(live, working);

		Assert.True(live.LlmSettings.Prompts.Remove(promptTheUiIsBoundTo));
		Assert.Single(live.LlmSettings.Prompts);
	}

	[Fact]
	public void CommitInto_AppliesPromptsAddedInTheWorkingCopy()
	{
		var live = BuildLiveSettings();
		var working = SettingsWorkingCopy.Clone(live);
		working.LlmSettings!.Prompts.Add(new LlmSettings.LlmPrompt { Id = 3, Name = "New" });

		SettingsWorkingCopy.CommitInto(live, working);

		Assert.Equal(3, live.LlmSettings!.Prompts.Count);
		Assert.Equal("New", live.LlmSettings.Prompts[2].Name);
	}

	[Fact]
	public void CommitInto_AppliesPromptsRemovedInTheWorkingCopy()
	{
		var live = BuildLiveSettings();
		var working = SettingsWorkingCopy.Clone(live);
		working.LlmSettings!.Prompts.RemoveAt(1);

		SettingsWorkingCopy.CommitInto(live, working);

		Assert.Single(live.LlmSettings!.Prompts);
		Assert.Equal("Tidy", live.LlmSettings.Prompts[0].Name);
	}

	[Fact]
	public void CommitInto_AppliesPromptContentChanges()
	{
		var live = BuildLiveSettings();
		var working = SettingsWorkingCopy.Clone(live);
		working.LlmSettings!.Prompts[0].Content = "changed in settings";

		SettingsWorkingCopy.CommitInto(live, working);

		Assert.Equal("changed in settings", live.LlmSettings!.Prompts[0].Content);
	}

	[Fact]
	public void CommitInto_KeepsSubObjectAndListInstances()
	{
		var live = BuildLiveSettings();
		var audio = live.AudioSettings!;
		var tts = live.TextToSpeechSettings!;
		var rules = live.TranscriptFormatRules;
		var models = live.LlmSettings!.Models;
		var working = SettingsWorkingCopy.Clone(live);

		SettingsWorkingCopy.CommitInto(live, working);

		Assert.Same(audio, live.AudioSettings);
		Assert.Same(tts, live.TextToSpeechSettings);
		Assert.Same(rules, live.TranscriptFormatRules);
		Assert.Same(models, live.LlmSettings.Models);
	}

	// --- The drift guard: every settable property must round-trip -------------------

	[Fact]
	public void CommitInto_TransfersEverySettableProperty()
	{
		var live = BuildLiveSettings();
		var working = SettingsWorkingCopy.Clone(live);

		var mutated = new List<string>();
		MutateEveryValueProperty(working, "Settings", mutated);
		Assert.True(mutated.Count > 50, $"Expected the settings graph to expose many properties, saw {mutated.Count}.");

		SettingsWorkingCopy.CommitInto(live, working);

		var mismatches = new List<string>();
		CompareEveryValueProperty(live, working, "Settings", mismatches);
		Assert.True(mismatches.Count == 0,
			"CommitInto dropped: " + string.Join(", ", mismatches));
	}

	// Walks the graph and gives every simple property a value distinct from its current
	// one, so a property CommitInto forgets shows up as a mismatch afterwards.
	private static void MutateEveryValueProperty(object target, string path, List<string> mutated)
	{
		foreach (var property in target.GetType().GetProperties(BindingFlags.Public | BindingFlags.Instance))
		{
			if (property.GetIndexParameters().Length > 0 || !property.CanRead)
				continue;

			object? value = property.GetValue(target);
			string childPath = $"{path}.{property.Name}";

			if (value is IList list)
			{
				foreach (var item in list)
				{
					if (item is not null && !IsSimple(item.GetType()))
						MutateEveryValueProperty(item, childPath, mutated);
				}
				continue;
			}

			if (value is not null && !IsSimple(property.PropertyType))
			{
				MutateEveryValueProperty(value, childPath, mutated);
				continue;
			}

			if (!property.CanWrite || property.SetMethod is null || !property.SetMethod.IsPublic)
				continue;

			object? mutatedValue = NextValue(property.PropertyType, value);
			if (mutatedValue is null && value is null)
				continue;

			property.SetValue(target, mutatedValue);
			mutated.Add(childPath);
		}
	}

	private static void CompareEveryValueProperty(object actual, object expected, string path, List<string> mismatches)
	{
		foreach (var property in expected.GetType().GetProperties(BindingFlags.Public | BindingFlags.Instance))
		{
			if (property.GetIndexParameters().Length > 0 || !property.CanRead)
				continue;

			object? expectedValue = property.GetValue(expected);
			object? actualValue = property.GetValue(actual);
			string childPath = $"{path}.{property.Name}";

			if (expectedValue is IList expectedList)
			{
				if (actualValue is not IList actualList || actualList.Count != expectedList.Count)
				{
					mismatches.Add(childPath);
					continue;
				}
				for (int i = 0; i < expectedList.Count; i++)
				{
					var expectedItem = expectedList[i];
					var actualItem = actualList[i];
					if (expectedItem is null || IsSimple(expectedItem.GetType()))
					{
						if (!Equals(expectedItem, actualItem))
							mismatches.Add($"{childPath}[{i}]");
						continue;
					}
					if (actualItem is null)
					{
						mismatches.Add($"{childPath}[{i}]");
						continue;
					}
					CompareEveryValueProperty(actualItem, expectedItem, $"{childPath}[{i}]", mismatches);
				}
				continue;
			}

			if (expectedValue is not null && !IsSimple(property.PropertyType))
			{
				if (actualValue is null)
					mismatches.Add(childPath);
				else
					CompareEveryValueProperty(actualValue, expectedValue, childPath, mismatches);
				continue;
			}

			if (!Equals(expectedValue, actualValue))
				mismatches.Add(childPath);
		}
	}

	private static bool IsSimple(Type type)
	{
		var underlying = Nullable.GetUnderlyingType(type) ?? type;
		return underlying.IsValueType || underlying == typeof(string) || underlying.IsArray;
	}

	private static object? NextValue(Type type, object? current)
	{
		var underlying = Nullable.GetUnderlyingType(type) ?? type;

		if (underlying == typeof(bool))
			return !(current as bool? ?? false);
		if (underlying == typeof(string))
			return (current as string) + "-committed";
		if (underlying == typeof(int))
			return (current as int? ?? 0) + 7;
		if (underlying == typeof(long))
			return (current as long? ?? 0L) + 11L;
		if (underlying == typeof(double))
			return (current as double? ?? 0d) + 1.5d;
		if (underlying == typeof(decimal))
			return (current as decimal? ?? 0m) + 3m;
		if (underlying.IsEnum)
		{
			var values = Enum.GetValues(underlying);
			foreach (var candidate in values)
			{
				if (!Equals(candidate, current))
					return candidate;
			}
			return current;
		}
		// Structs without a natural "next" (Point, Size) and arrays are left alone;
		// the comparison pass still checks that they were carried over unchanged.
		return current;
	}
}
