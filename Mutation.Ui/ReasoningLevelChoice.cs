using CognitiveSupport;

namespace Mutation.Ui;

/// <summary>
/// One check box in the reasoning levels picker: the level, the name the user sees and
/// hears for it, and whether it starts ticked.
/// </summary>
/// <param name="Level">The level the check box stands for.</param>
/// <param name="DisplayName">The check box's text and accessible name, for example "Extra high".</param>
/// <param name="IsTicked">Whether the level is ticked for the model now.</param>
public sealed record ReasoningLevelChoice(ReasoningLevel Level, string DisplayName, bool IsTicked);
