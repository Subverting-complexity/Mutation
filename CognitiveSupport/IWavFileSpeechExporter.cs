namespace CognitiveSupport;

// Exports synthesized speech to a WAV file using a dedicated, throwaway
// synthesizer. Kept separate from ITextToSpeechService so a file export can
// never touch the live reader's playback, position, or navigation state.
public interface IWavFileSpeechExporter
{
	// Synthesize <paramref name="text"/> to a .wav file at <paramref name="filePath"/>
	// using the given voice/rate/volume and optional speech preprocessing. Runs
	// synchronously (the caller should invoke it off the UI thread); throws on empty
	// input or synthesis/IO failure so the caller can surface an accessible error.
	void ExportToWavFile(string text, string filePath, int rate, int volume, string? voiceName, bool preprocess);
}
