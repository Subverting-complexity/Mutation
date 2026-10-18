namespace CognitiveSupport;

/// <summary>
/// The names the LLM request log uses for the places a request can start from. Carried
/// in <see cref="LlmRequestOptions.EntryPoint"/>, so the service can say in its own log
/// line which part of the app sent a request, without each caller logging it itself.
/// </summary>
public static class LlmEntryPoint
{
	/// <summary>A dictation whose transcript goes through its prompt.</summary>
	public const string Dictation = "dictation";

	/// <summary>A prompt run on the clipboard from its hotkey.</summary>
	public const string PromptHotkey = "prompt hotkey";

	/// <summary>The prompt editor's test run.</summary>
	public const string PromptEditorTest = "prompt editor test";
}
