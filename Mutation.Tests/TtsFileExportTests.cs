using CognitiveSupport;
using Xunit;

namespace Mutation.Tests;

// Verifies the pure, voice-free logic behind the "Speak to file" (WAV export)
// action: suggested-filename generation, extension handling, and input
// resolution. The actual System.Speech WAV write depends on a system voice and
// is verified manually/integration.
public class TtsFileExportTests
{
	[Fact]
	public void BuildSuggestedFileName_UsesTimestampWithoutExtension()
	{
		var when = new DateTime(2026, 6, 23, 15, 30, 12);
		Assert.Equal("tts-20260623-153012", TtsFileExport.BuildSuggestedFileName(when));
	}

	[Fact]
	public void EnsureWavExtension_AppendsWhenMissing()
	{
		Assert.Equal(@"C:\audio\reading.wav", TtsFileExport.EnsureWavExtension(@"C:\audio\reading"));
	}

	[Fact]
	public void EnsureWavExtension_KeepsExistingExtension()
	{
		Assert.Equal(@"C:\audio\reading.wav", TtsFileExport.EnsureWavExtension(@"C:\audio\reading.wav"));
	}

	[Fact]
	public void EnsureWavExtension_IsCaseInsensitive()
	{
		Assert.Equal(@"C:\audio\reading.WAV", TtsFileExport.EnsureWavExtension(@"C:\audio\reading.WAV"));
	}

	[Theory]
	[InlineData(null)]
	[InlineData("")]
	[InlineData("   ")]
	[InlineData("\t\r\n ")]
	public void TryResolveExportText_RejectsEmptyOrWhitespace(string? raw)
	{
		bool ok = TtsFileExport.TryResolveExportText(raw, out string resolved, out string error);

		Assert.False(ok);
		Assert.Equal(string.Empty, resolved);
		Assert.False(string.IsNullOrWhiteSpace(error));
	}

	[Fact]
	public void TryResolveExportText_TrimsAndAcceptsRealText()
	{
		bool ok = TtsFileExport.TryResolveExportText("  hello world  ", out string resolved, out string error);

		Assert.True(ok);
		Assert.Equal("hello world", resolved);
		Assert.Equal(string.Empty, error);
	}

	// The exporter must reject empty input before opening a synthesizer or writing a
	// file — this guard is voice-free and so unit-testable.
	[Fact]
	public void Exporter_RejectsEmptyInput_WithoutWritingFile()
	{
		var exporter = new WavFileSpeechExporter();
		string path = Path.Combine(Path.GetTempPath(), $"tts-export-test-{Guid.NewGuid():N}.wav");

		Assert.Throws<ArgumentException>(() =>
			exporter.ExportToWavFile("   ", path, rate: 0, volume: 100, voiceName: null, preprocess: true));

		Assert.False(File.Exists(path));
	}
}
