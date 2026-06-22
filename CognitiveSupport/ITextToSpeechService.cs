namespace CognitiveSupport;

public interface ITextToSpeechService : IDisposable
{
	bool IsSpeaking { get; }
	string? CurrentText { get; }
	IReadOnlyList<string> GetVoiceNames();
	void Speak(string text, int rate, int volume, string? voiceName, bool resumeIfSame, bool preprocess, int resumeRewindWordCount = 0);
	void SkipSentence(int direction, int rate, int volume, string? voiceName, int graceWindowMs);
	void SpeakAnnouncement(string text, int rate, int volume, string? voiceName);
	void Stop();
}
