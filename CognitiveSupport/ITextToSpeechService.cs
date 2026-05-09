namespace CognitiveSupport;

public interface ITextToSpeechService : IDisposable
{
	bool IsSpeaking { get; }
	string? CurrentText { get; }
	IReadOnlyList<string> GetVoiceNames();
	void Speak(string text, int rate, string? voiceName);
	void Stop();
}
