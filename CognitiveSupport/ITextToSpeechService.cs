namespace CognitiveSupport;

public interface ITextToSpeechService : IDisposable
{
	bool IsSpeaking { get; }
	string? CurrentText { get; }
	void Speak(string text, int rate, string? voiceName);
	void Stop();
}
