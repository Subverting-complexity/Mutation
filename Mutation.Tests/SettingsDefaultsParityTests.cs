using System;
using System.IO;
using CognitiveSupport;
using Mutation.Ui.Services;
using Mutation.Ui.Views.SettingsUi;

namespace Mutation.Tests;

// Verifies that the values exposed in SettingsDefaults match what
// SettingsManager.EnsureSettings actually writes onto a fresh Settings object.
// Drift between the two would let the Settings dialog's per-field reset write a
// value that EnsureSettings would immediately overwrite on next load.
public class SettingsDefaultsParityTests : IDisposable
{
	private readonly string _tempPath;

	public SettingsDefaultsParityTests()
	{
		_tempPath = Path.Combine(Path.GetTempPath(), $"mutation-defaults-{Guid.NewGuid():N}.json");
		// Pre-create the file so EnsureSettings doesn't take the new-file branch
		// (which spawns notepad.exe). EnsureSettings will still apply all defaults.
		File.WriteAllText(_tempPath, "{}");
	}

	public void Dispose()
	{
		if (File.Exists(_tempPath))
			File.Delete(_tempPath);
	}

	private Settings ApplyDefaults()
	{
		var manager = new SettingsManager(_tempPath);
		var settings = new Settings();
		manager.EnsureSettings(settings, isNewFile: false);
		return settings;
	}

	[Fact]
	public void Audio_Defaults_Match()
	{
		var s = ApplyDefaults();
		Assert.Equal(SettingsDefaults.Audio.MicrophoneToggleMuteHotKey, s.AudioSettings!.MicrophoneToggleMuteHotKey);
		Assert.Equal(SettingsDefaults.Audio.EnableMicrophoneVisualization, s.AudioSettings!.EnableMicrophoneVisualization);
	}

	[Fact]
	public void Ocr_Defaults_Match()
	{
		var s = ApplyDefaults();
		var ocr = s.AzureComputerVisionSettings!;
		Assert.Equal(SettingsDefaults.Ocr.ScreenshotHotKey, ocr.ScreenshotHotKey);
		Assert.Equal(SettingsDefaults.Ocr.ScreenshotOcrHotKey, ocr.ScreenshotOcrHotKey);
		Assert.Equal(SettingsDefaults.Ocr.OcrHotKey, ocr.OcrHotKey);
		Assert.Equal(SettingsDefaults.Ocr.OcrLeftToRightTopToBottomHotKey, ocr.OcrLeftToRightTopToBottomHotKey);
		Assert.Equal(SettingsDefaults.Ocr.ScreenshotLeftToRightTopToBottomOcrHotKey, ocr.ScreenshotLeftToRightTopToBottomOcrHotKey);
		Assert.Equal(SettingsDefaults.Ocr.TimeoutSeconds, ocr.TimeoutSeconds);
		Assert.Equal(SettingsDefaults.Ocr.UseFreeTier, ocr.UseFreeTier);
		Assert.Equal(SettingsDefaults.Ocr.FreeTierPageLimit, ocr.FreeTierPageLimit);
		Assert.Equal(SettingsDefaults.Ocr.MaxParallelDocuments, ocr.MaxParallelDocuments);
		Assert.Equal(SettingsDefaults.Ocr.MaxParallelRequests, ocr.MaxParallelRequests);
	}

	[Fact]
	public void Speech_Defaults_Match()
	{
		var s = ApplyDefaults();
		var stt = s.SpeechToTextSettings!;
		Assert.Equal(SettingsDefaults.Speech.SpeechToTextHotKey, stt.SpeechToTextHotKey);
		Assert.Equal(SettingsDefaults.Speech.SpeechToTextWithLlmFormattingHotKey, stt.SpeechToTextWithLlmFormattingHotKey);
		Assert.Equal(SettingsDefaults.Speech.TempDirectory, stt.TempDirectory);
		Assert.Equal(SettingsDefaults.Speech.FileTranscriptionTimeoutSeconds, stt.FileTranscriptionTimeoutSeconds);

		Assert.NotNull(stt.Services);
		Assert.NotEmpty(stt.Services!);
		Assert.Equal(SettingsDefaults.Speech.DefaultServiceName, stt.ActiveSpeechToTextService);
		var first = stt.Services![0];
		Assert.Equal(SettingsDefaults.Speech.DefaultServiceModelId, first.ModelId);
		Assert.Equal(SettingsDefaults.Speech.DefaultServiceBaseDomain, first.BaseDomain);
		Assert.Equal(SettingsDefaults.Speech.DefaultServicePrompt, first.SpeechToTextPrompt);
		Assert.Equal(SettingsDefaults.Speech.ServiceTimeoutSeconds, first.TimeoutSeconds);
	}

	[Fact]
	public void Tts_Defaults_Match()
	{
		var s = ApplyDefaults();
		var tts = s.TextToSpeechSettings!;
		Assert.Equal(SettingsDefaults.Tts.TextToSpeechHotKey, tts.TextToSpeechHotKey);
		Assert.Equal(SettingsDefaults.Tts.SpeakSelectionHotKey, tts.SpeakSelectionHotKey);
		Assert.Equal(SettingsDefaults.Tts.RestartFromBeginningHotKey, tts.RestartFromBeginningHotKey);
		Assert.Equal(SettingsDefaults.Tts.SkipSentenceBackwardHotKey, tts.SkipSentenceBackwardHotKey);
		Assert.Equal(SettingsDefaults.Tts.SkipSentenceForwardHotKey, tts.SkipSentenceForwardHotKey);
		Assert.Equal(SettingsDefaults.Tts.Rate, tts.Rate);
		Assert.Equal(SettingsDefaults.Tts.Volume, tts.Volume);
		Assert.Equal(SettingsDefaults.Tts.EnableSpeechPreprocessing, tts.EnableSpeechPreprocessing);
	}

	[Fact]
	public void MainWindowUi_Defaults_Match()
	{
		var s = ApplyDefaults();
		var ui = s.MainWindowUiSettings!;
		Assert.Equal(SettingsDefaults.MainWindowUi.MaxTextBoxLineCount, ui.MaxTextBoxLineCount);
		Assert.Equal(SettingsDefaults.MainWindowUi.DictationInsertPreference, ui.DictationInsertPreference);
	}

	[Fact]
	public void UserInstructions_Default_Match()
	{
		var s = ApplyDefaults();
		Assert.Equal(SettingsDefaults.UserInstructions, s.UserInstructions);
	}
}
