using CognitiveSupport;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;
using System;
using System.Collections.Generic;

namespace Mutation.Ui.Views.SettingsUi;

// Holds an editable snapshot of the live Settings graph for the Settings dialog.
// Save commits the snapshot back into the live instance field-by-field so that
// references held elsewhere in the app (Settings, sub-objects) remain valid.
internal static class SettingsWorkingCopy
{
	private static readonly JsonSerializerSettings _serializer = new()
	{
		Converters = new List<JsonConverter> { new StringEnumConverter() },
	};

	public static Settings Clone(Settings live)
	{
		string json = JsonConvert.SerializeObject(live, _serializer);
		var clone = JsonConvert.DeserializeObject<Settings>(json, _serializer) ?? new Settings();
		return clone;
	}

	// Copy values from the working copy into the live Settings instance, preserving
	// the existing top-level reference so other parts of the app keep observing the
	// same object. Sub-object and list instances are preserved too, which is what keeps
	// the prompt ListView bound to the same objects the app later edits (issue #219).
	//
	// The copy is reflective on purpose. The hand-maintained field list this replaced
	// silently dropped whatever nobody remembered to add to it, and it had already
	// drifted twice (issues #218 and #219).
	public static void CommitInto(Settings live, Settings workingCopy)
	{
		ArgumentNullException.ThrowIfNull(live);
		ArgumentNullException.ThrowIfNull(workingCopy);

		EnsureSections(live);
		EnsureSections(workingCopy);

		SettingsGraphCommitter.Commit(live, workingCopy);
	}

	// Both sides need every section present: the committer copies null over non-null,
	// so a missing section on either side would otherwise leave the live graph with a
	// null the rest of the app does not expect.
	private static void EnsureSections(Settings settings)
	{
		settings.AudioSettings ??= new AudioSettings();
		settings.AzureComputerVisionSettings ??= new AzureComputerVisionSettings();
		settings.SpeechToTextSettings ??= new SpeechToTextSettings();
		settings.ApiKeys ??= new ApiKeys();
		settings.LlmSettings ??= new LlmSettings();
		settings.TextToSpeechSettings ??= new TextToSpeechSettings();
		settings.MainWindowUiSettings ??= new MainWindowUiSettings();
		settings.HotKeyRouterSettings ??= new HotKeyRouterSettings();
		settings.TranscriptFormatRules ??= new List<TranscriptFormatRule>();

		// A settings file with an explicit null for one of these lists deserializes to
		// null, and the committer would then propagate that null onto the live graph.
		settings.LlmSettings.Models ??= new List<LlmModelConfig>();
		settings.LlmSettings.Prompts ??= new List<LlmSettings.LlmPrompt>();
		settings.HotKeyRouterSettings.Mappings ??= new List<HotKeyRouterSettings.HotKeyRouterMap>();
	}
}
