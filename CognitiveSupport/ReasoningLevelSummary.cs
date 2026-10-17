namespace CognitiveSupport;

/// <summary>
/// Describes a model's ticked reasoning levels in a few words, for the button on each
/// model row: "Low to Max", "Off, Low, High", or "none".
/// </summary>
public static class ReasoningLevelSummary
{
	/// <summary>Shortest run of neighbouring levels written as a range.</summary>
	/// <remarks>
	/// Two neighbours stay a list: "Low, Medium" is as short as "Low to Medium" and
	/// does not make the listener work out what lies between.
	/// </remarks>
	private const int ShortestRange = 3;

	/// <summary>The text for no usable level ticked.</summary>
	public const string NoneText = "none";

	/// <summary>
	/// The ticked levels the service accepts, lightest first. Each run of three or more
	/// neighbouring levels in the service's list becomes "first to last"; the rest are
	/// listed with commas. Levels the service does not offer are left out, as the
	/// request code leaves them out.
	/// </summary>
	/// <param name="provider">The model's service, which fixes the list and its order.</param>
	/// <param name="ticked">The ticked levels, in any order. Null counts as none.</param>
	public static string Describe(LlmProvider provider, IEnumerable<ReasoningLevel>? ticked)
	{
		var usable = ReasoningLevelCatalog.InServiceOrder(provider, ticked);
		if (usable.Count == 0)
			return NoneText;

		var fullList = ReasoningLevelCatalog.FullList(provider);
		var parts = new List<string>();
		int runStart = 0;

		for (int i = 1; i <= usable.Count; i++)
		{
			bool runContinues = i < usable.Count
				&& IndexOf(fullList, usable[i]) == IndexOf(fullList, usable[i - 1]) + 1;
			if (runContinues)
				continue;

			AddRun(parts, usable, runStart, i - 1);
			runStart = i;
		}

		return string.Join(", ", parts);
	}

	private static void AddRun(List<string> parts, IReadOnlyList<ReasoningLevel> levels, int first, int last)
	{
		if (last - first + 1 >= ShortestRange)
		{
			parts.Add($"{ReasoningLevelCatalog.DisplayName(levels[first])} to {ReasoningLevelCatalog.DisplayName(levels[last])}");
			return;
		}

		for (int i = first; i <= last; i++)
			parts.Add(ReasoningLevelCatalog.DisplayName(levels[i]));
	}

	private static int IndexOf(IReadOnlyList<ReasoningLevel> list, ReasoningLevel level)
	{
		for (int i = 0; i < list.Count; i++)
		{
			if (list[i] == level)
				return i;
		}

		return -1;
	}
}
