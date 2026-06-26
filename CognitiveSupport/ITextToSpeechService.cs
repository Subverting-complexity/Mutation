namespace CognitiveSupport;

public interface ITextToSpeechService : IDisposable
{
	bool IsSpeaking { get; }

	// True only while a read is frozen by Pause(); false when stopped or actively speaking.
	// Lets the UI decide whether a toggle press should Pause (speaking) or Resume (paused).
	bool IsPaused { get; }

	string? CurrentText { get; }
	IReadOnlyList<string> GetVoiceNames();
	void Speak(string text, int rate, int volume, string? voiceName, bool resumeIfSame, bool preprocess, int resumeRewindWordCount = 0, SpeechPreprocessingOptions? options = null);
	void SkipSentence(int direction, int rate, int volume, string? voiceName, int graceWindowMs);
	void SpeakAnnouncement(string text, int rate, int volume, string? voiceName);

	// Freeze the current read in place using the synthesizer's native pause, holding the
	// exact word position for a later Resume. Speaks a brief "Paused" cue (rate/volume/voice
	// drive that cue). No-op when not actively speaking.
	void Pause(int rate, int volume, string? voiceName);

	// Resume a paused read. When the pause was at or below rewindAfterPauseSeconds, continues
	// from the exact word with no rewind; when it exceeded the threshold (or the threshold is
	// 0), rewinds resumeRewindWordCount words for context. No-op when not paused.
	void Resume(int rate, int volume, string? voiceName, int resumeRewindWordCount, int rewindAfterPauseSeconds);

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
