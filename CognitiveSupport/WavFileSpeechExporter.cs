using System.Speech.Synthesis;

namespace CognitiveSupport;

// Synthesizes text to a WAV file with a dedicated, single-use SpeechSynthesizer.
// It deliberately shares no state with TextToSpeechService: a file export runs on
// its own synthesizer that writes to the file sink instead of the audio device, so
// exporting never disturbs an in-progress live read.
public class WavFileSpeechExporter : IWavFileSpeechExporter
{
	public void ExportToWavFile(string text, string filePath, int rate, int volume, string? voiceName, bool preprocess, SpeechPreprocessingOptions? options = null)
	{
		if (!TtsFileExport.TryResolveExportText(text, out string resolved, out string error))
			throw new ArgumentException(error, nameof(text));

		string wavPath = TtsFileExport.EnsureWavExtension(filePath);
		string spoken = preprocess
			? TextToSpeechService.PreprocessForSpeech(resolved, options ?? SpeechPreprocessingOptions.All)
			: resolved;
		if (string.IsNullOrWhiteSpace(spoken))
			throw new ArgumentException("Nothing to speak after preprocessing.", nameof(text));

		using var synth = new SpeechSynthesizer();
		ApplyVoiceParams(synth, rate, volume, voiceName);
		synth.SetOutputToWaveFile(wavPath);
		synth.Speak(spoken);
		// Detach the file sink before disposal so the WAV is finalized and released.
		synth.SetOutputToNull();
	}

	private static void ApplyVoiceParams(SpeechSynthesizer synth, int rate, int volume, string? voiceName)
	{
		synth.Rate = Math.Clamp(rate, -10, 10);
		synth.Volume = Math.Clamp(volume, 0, 100);

		if (!string.IsNullOrWhiteSpace(voiceName))
		{
			try { synth.SelectVoice(voiceName); }
			catch { /* fall back to the default voice */ }
		}
	}
}
