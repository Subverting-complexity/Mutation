namespace CognitiveSupport;

// Pure, voice-free helpers for the "Speak to file" (WAV export) action. Kept
// separate from the synthesizer so the naming, extension, and input-resolution
// rules can be unit-tested without a system voice.
public static class TtsFileExport
{
	private const string WavExtension = ".wav";

	// Suggested file name (no extension — the file picker appends .wav from its
	// FileTypeChoices, mirroring the OCR-results export). Example: tts-20260623-153012.
	public static string BuildSuggestedFileName(DateTime now) =>
		$"tts-{now:yyyyMMdd-HHmmss}";

	// Guarantee the destination path ends with .wav (case-insensitive). The picker
	// already enforces the extension, but the exporter validates defensively so a
	// hotkey-driven or programmatic path can never write a mis-named file.
	public static string EnsureWavExtension(string path)
	{
		if (string.IsNullOrWhiteSpace(path))
			throw new ArgumentException("Path must not be empty.", nameof(path));

		return path.EndsWith(WavExtension, StringComparison.OrdinalIgnoreCase)
			? path
			: path + WavExtension;
	}

	// Resolve the raw input (clipboard/selection text) into the text to synthesize.
	// Trims surrounding whitespace; empty or whitespace-only input is rejected with a
	// clear message and writes no file.
	public static bool TryResolveExportText(string? rawText, out string resolved, out string error)
	{
		string trimmed = (rawText ?? string.Empty).Trim();
		if (trimmed.Length == 0)
		{
			resolved = string.Empty;
			error = "Nothing to speak — the clipboard has no text to save.";
			return false;
		}

		resolved = trimmed;
		error = string.Empty;
		return true;
	}
}
