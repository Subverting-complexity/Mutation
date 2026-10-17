using CognitiveSupport;
using Newtonsoft.Json.Linq;
using System.Linq;

namespace Mutation.Ui.Services;

/// <summary>
/// Brings a settings file written before reasoning levels existed up to date, once.
/// Every model gets its service's full list ticked, so nothing a user relied on stops
/// reasoning, and every prompt gets Low.
/// <para>
/// It works on the raw JSON, not on the loaded objects, because only the JSON can tell
/// an older file from a user's choice. A model the user unticked completely is saved as
/// <c>"ReasoningLevels": []</c>, and loads as the same empty list an older file would.
/// Upgrading on the missing key alone means that choice survives every later load.
/// </para>
/// </summary>
internal static class ReasoningLevelsUpgrade
{
	private const string ModelLevelsKey = nameof(LlmModelConfig.ReasoningLevels);
	private const string PromptLevelKey = nameof(LlmSettings.LlmPrompt.ReasoningLevel);

	/// <summary>Adds the missing keys. True when anything changed and the file needs saving.</summary>
	/// <param name="llmSettings">The <c>LlmSettings</c> section of the settings file.</param>
	public static bool Apply(JObject llmSettings)
	{
		bool changed = false;

		if (llmSettings["Models"] is JArray models)
		{
			foreach (var model in models.OfType<JObject>())
			{
				if (model.ContainsKey(ModelLevelsKey))
					continue;

				var fullList = ReasoningLevelCatalog.FullList(ReadProvider(model["Provider"]));
				model[ModelLevelsKey] = new JArray(fullList.Select(level => level.ToString()));
				changed = true;
			}
		}

		if (llmSettings["Prompts"] is JArray prompts)
		{
			foreach (var prompt in prompts.OfType<JObject>())
			{
				if (prompt.ContainsKey(PromptLevelKey))
					continue;

				prompt[PromptLevelKey] = nameof(ReasoningLevel.Low);
				changed = true;
			}
		}

		return changed;
	}

	/// <summary>
	/// The model's service as the deserializer will read it: by name in any case, or by
	/// number. Anything else falls back to OpenAI, the <see cref="LlmModelConfig"/> default.
	/// </summary>
	private static LlmProvider ReadProvider(JToken? token)
	{
		if (token is null)
			return LlmProvider.OpenAI;

		if (token.Type == JTokenType.Integer)
		{
			var number = (LlmProvider)token.Value<int>();
			return System.Enum.IsDefined(number) ? number : LlmProvider.OpenAI;
		}

		return System.Enum.TryParse<LlmProvider>(token.ToString(), ignoreCase: true, out var provider)
			&& System.Enum.IsDefined(provider)
				? provider
				: LlmProvider.OpenAI;
	}
}
