namespace CognitiveSupport;

/// <summary>
/// The one place that decides what a configured LLM model name means, so the OpenAI and
/// Anthropic services behind <see cref="ILlmService"/> cannot answer that question
/// differently.
/// <para>
/// They used to. OpenAI keyed its lookup ordinally and Anthropic case-insensitively, so
/// hand-editing <c>Mutation.json</c> to <c>"GPT-4.1"</c> against a configured
/// <c>"gpt-4.1"</c> failed while the identical typo against an Anthropic model worked.
/// A repeated name diverged the same way: OpenAI silently kept the last copy, Anthropic
/// threw an unhelpful framework message (issue #240).
/// </para>
/// </summary>
public static class LlmModelIndex
{
	/// <summary>
	/// Model names come from a hand-edited settings file, where a difference in casing is a
	/// typo rather than a different model, so both providers match them case-insensitively.
	/// </summary>
	public static StringComparer NameComparer => StringComparer.OrdinalIgnoreCase;

	/// <summary>
	/// Materialises the configured models and rejects the two sets a lookup cannot represent:
	/// no models at all, and a name that repeats once casing is ignored.
	/// </summary>
	/// <param name="models">The models as configured, in file order.</param>
	/// <param name="parameterName">The caller's parameter name, for the thrown exception.</param>
	/// <exception cref="ArgumentNullException"><paramref name="models"/> is null.</exception>
	/// <exception cref="ArgumentException">The set is empty, or a name is configured twice.</exception>
	public static List<LlmModelConfig> Validate(
		IEnumerable<LlmModelConfig> models,
		string parameterName)
	{
		if (models is null) throw new ArgumentNullException(parameterName);

		var modelList = models.ToList();
		if (modelList.Count == 0)
			throw new ArgumentException("At least one model must be configured.", parameterName);

		var seen = new HashSet<string>(NameComparer);
		foreach (var model in modelList)
		{
			string name = model?.Name ?? string.Empty;
			if (!seen.Add(name))
				throw new ArgumentException(
					$"The model name '{name}' is configured more than once. Model names must be unique, ignoring case.",
					parameterName);
		}

		return modelList;
	}
}
