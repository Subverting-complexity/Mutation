namespace CognitiveSupport;

public interface ITextToSpeechService : IDisposable
{
	bool IsSpeaking { get; }
	string? CurrentText { get; }
	IReadOnlyList<string> GetVoiceNames();
	void Speak(string text, int rate, int volume, string? voiceName, bool resumeIfSame, bool preprocess, int resumeRewindWordCount = 0);
	void SkipSentence(int direction, int rate, int volume, string? voiceName, int graceWindowMs);
	void SpeakAnnouncement(string text, int rate, int volume, string? voiceName);

	// A snapshot of where the current read is, for the speak-position hotkey. Returns
	// ReadingPosition.NotReading when nothing is being read.
	ReadingPosition GetReadingPosition();

	// Push the configurable reading-time and periodic-progress announcement settings
	// into the service. Safe to call at any time; takes effect on the next read.
	void SetAnnouncementOptions(
		bool announceReadingTimeAtStart,
		int readingTimeMinimumMinutes,
		bool announceProgressEnabled,
		int announceProgressEveryPercent,
		int announceProgressMinimumMinutes);

	void Stop();
}
