using OpenAI.Chat;

namespace CognitiveSupport;

/// <summary>
/// How a resolved reasoning level becomes the reasoning effort on an OpenAI chat request.
/// Kept apart from the request code so the mapping can be read, and tested, as one table.
/// <list type="bullet">
/// <item>No level: no reasoning effort at all, and the model's temperature as before.</item>
/// <item>None: reasoning effort <c>none</c>, temperature kept, because the model does not
/// reason.</item>
/// <item>Minimal to Extra high: the matching reasoning effort, and no temperature, because
/// OpenAI refuses a custom temperature while a model reasons.</item>
/// </list>
/// </summary>
// OPENAI001: the library marks chat reasoning effort as evaluation-only. It is the only way
// to send a reasoning level, and this file and one line in LlmService.BuildChatOptions are
// all that has to move if the library renames it.
#pragma warning disable OPENAI001
public static class OpenAiReasoningFields
{
	/// <summary>
	/// The text OpenAI uses for Extra high. The library has no named value for it on chat
	/// requests, so it is sent as plain text, the same way Fast mode's service tier is.
	/// </summary>
	internal const string ExtraHighEffort = "xhigh";

	/// <summary>The reasoning effort to send, or null to send none.</summary>
	/// <param name="level">The resolved level, or null for none.</param>
	public static ChatReasoningEffortLevel? Effort(ReasoningLevel? level) =>
		level switch
		{
			ReasoningLevel.None => ChatReasoningEffortLevel.None,
			ReasoningLevel.Minimal => ChatReasoningEffortLevel.Minimal,
			ReasoningLevel.Low => ChatReasoningEffortLevel.Low,
			ReasoningLevel.Medium => ChatReasoningEffortLevel.Medium,
			ReasoningLevel.High => ChatReasoningEffortLevel.High,
			ReasoningLevel.ExtraHigh => new ChatReasoningEffortLevel(ExtraHighEffort),
			// Off and Max are Anthropic levels, which the resolver never hands an OpenAI model.
			// The cast matters: a bare null takes the arms' type, and the library's implicit
			// conversion from text would turn it into an effort with no value and throw.
			_ => (ChatReasoningEffortLevel?)null,
		};

	/// <summary>
	/// Whether the model's own temperature may go on the request: always, except while a
	/// level that makes the model reason is sent.
	/// </summary>
	/// <param name="level">The resolved level, or null for none.</param>
	public static bool SendsTemperature(ReasoningLevel? level) =>
		level is ReasoningLevel.None || Effort(level) is null;
}
#pragma warning restore OPENAI001
