namespace CognitiveSupport;

/// <summary>
/// How hard a model is asked to think before it answers. One enum covers both
/// services, because a prompt stores one level whichever model it runs on; each
/// service accepts only its own subset, listed by <see cref="ReasoningLevelCatalog"/>.
/// <para>
/// Settings store these by name, so renaming a member breaks every saved file.
/// The numeric values are fixed for the same reason and carry no ordering meaning:
/// the catalog, not the enum, says which level is lighter.
/// </para>
/// </summary>
public enum ReasoningLevel
{
	/// <summary>Anthropic: thinking switched off.</summary>
	Off = 1,

	/// <summary>OpenAI: no reasoning effort.</summary>
	None = 2,

	/// <summary>OpenAI only.</summary>
	Minimal = 3,

	Low = 4,
	Medium = 5,
	High = 6,
	ExtraHigh = 7,

	/// <summary>Anthropic only.</summary>
	Max = 8,
}
