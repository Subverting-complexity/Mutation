using CognitiveSupport;
using Mutation.Ui.Services;
using Newtonsoft.Json;

namespace Mutation.Tests;

// The pointer-nudge settings contract (issues #373 and #375): off by default with sensible
// timings, backward compatible both with a settings file written before the feature existed and
// with one written before the setting was renamed, and clamped on load to the same range the
// Settings dialog offers — so a hand-edited file cannot produce a nudge nobody can stop.
//
// Loading a real settings file replaces ErrorLogger's process-wide secret snapshot, so this
// class shares the collection that serialises those statics.
[Collection(ErrorLoggerCollection.Name)]
public class PointerNudgeSettingsTests
{
	[Fact]
	public void OffByDefault_WithFiftyAndTwoHundred()
	{
		// It moves the pointer on purpose. Nobody gets that uninvited.
		var ocr = new AzureComputerVisionSettings();

		Assert.False(ocr.NudgePointerDuringCapture);
		Assert.Equal(50, ocr.PointerNudgeIntervalMilliseconds);
		Assert.Equal(200, ocr.PointerNudgeDurationMilliseconds);
		Assert.Equal(1, ocr.PointerNudgeDistancePixels);
	}

	[Fact]
	public void SettingsFileWrittenBeforeTheFeature_KeepsTheDefaults()
	{
		// No keys in the file, so the deserialiser leaves the property initializers alone.
		var ocr = JsonConvert.DeserializeObject<AzureComputerVisionSettings>("{\"TimeoutSeconds\":10}");

		Assert.NotNull(ocr);
		Assert.False(ocr!.NudgePointerDuringCapture);
		Assert.Equal(50, ocr.PointerNudgeIntervalMilliseconds);
		Assert.Equal(200, ocr.PointerNudgeDurationMilliseconds);
		Assert.Equal(1, ocr.PointerNudgeDistancePixels);
		Assert.Equal(1500, ocr.PointerHoldMilliseconds);
	}

	[Theory]
	[InlineData(0, 50)]      // never configured, or hand-edited to nothing
	[InlineData(-10, 50)]
	[InlineData(1, 10)]      // below the floor
	[InlineData(10, 10)]
	[InlineData(50, 50)]
	[InlineData(500, 500)]
	[InlineData(9999, 500)]  // above the ceiling
	public void IntervalIsClampedOnLoad(int stored, int expected)
	{
		Assert.Equal(expected, Load(intervalMs: stored).PointerNudgeIntervalMilliseconds);
	}

	[Theory]
	[InlineData(0, 200)]
	[InlineData(-10, 200)]
	[InlineData(10, 50)]
	[InlineData(50, 50)]
	[InlineData(500, 500)]
	[InlineData(5000, 5000)]
	[InlineData(60000, 5000)]
	public void DurationIsClampedOnLoad(int stored, int expected)
	{
		Assert.Equal(expected, Load(durationMs: stored).PointerNudgeDurationMilliseconds);
	}

	[Theory]
	[InlineData(0, 1)]      // never configured, or hand-edited to nothing
	[InlineData(-4, 1)]
	[InlineData(1, 1)]
	[InlineData(8, 8)]
	[InlineData(64, 64)]
	[InlineData(4096, 64)]  // a wiggle that threw the pointer across the screen
	public void DistanceIsClampedOnLoad(int stored, int expected)
	{
		Assert.Equal(expected, Load(distancePx: stored).PointerNudgeDistancePixels);
	}

	[Theory]
	[InlineData(0, 0)]          // switched off on purpose, and left switched off
	[InlineData(-100, 0)]       // a negative is nonsense; off is the nearest sense it makes
	[InlineData(250, 250)]
	[InlineData(1500, 1500)]
	[InlineData(10000, 10000)]
	[InlineData(99999, 10000)]  // a watch nobody could wait out
	public void PointerHoldIsClampedOnLoad(int stored, int expected)
	{
		// Zero is a real answer here, unlike the wiggle timings, so it must survive rather than
		// be repaired to the default.
		Assert.Equal(expected, Load(holdMs: stored).PointerHoldMilliseconds);
	}

	[Fact]
	public void ValuesAlreadyInRange_AreLeftExactlyAsTheyAre()
	{
		// Whatever the user picked in the dialog has to survive a reload untouched.
		var ocr = Load(intervalMs: 120, durationMs: 3000, distancePx: 12, holdMs: 900);

		Assert.Equal(120, ocr.PointerNudgeIntervalMilliseconds);
		Assert.Equal(3000, ocr.PointerNudgeDurationMilliseconds);
		Assert.Equal(12, ocr.PointerNudgeDistancePixels);
		Assert.Equal(900, ocr.PointerHoldMilliseconds);
	}

	[Fact]
	public void TheToggleIsNeverTurnedOnForAnybody()
	{
		Assert.False(Load().NudgePointerDuringCapture);
	}

	[Theory]
	[InlineData(true)]
	[InlineData(false)]
	public void OldKeyFromBeforeTheRename_CarriesItsValueAcross(bool wasOn)
	{
		// The wiggle grew a second moment — the overlay opening, as well as the capture ending —
		// so NudgePointerAfterCapture stopped being a true name. Anyone who had switched it on
		// must not find it silently off after an update, which is exactly what would happen if
		// the renamed property fell back to its initializer.
		var ocr = LoadFile($$"""
		{
			"AzureComputerVisionSettings": { "NudgePointerAfterCapture": {{(wasOn ? "true" : "false")}} }
		}
		""");

		Assert.Equal(wasOn, ocr.NudgePointerDuringCapture);
	}

	[Fact]
	public void NewKeyWins_WhenAFileSomehowCarriesBoth()
	{
		var ocr = LoadFile("""
		{
			"AzureComputerVisionSettings": {
				"NudgePointerAfterCapture": false,
				"NudgePointerDuringCapture": true
			}
		}
		""");

		Assert.True(ocr.NudgePointerDuringCapture);
	}

	private static AzureComputerVisionSettings LoadFile(string json)
	{
		using var settingsFile = new TempSettingsFile("nudge-rename", json);
		var settings = new SettingsManager(settingsFile.FilePath).LoadAndEnsureSettings();
		return settings.AzureComputerVisionSettings!;
	}

	private static AzureComputerVisionSettings Load(int intervalMs = 50, int durationMs = 200, int distancePx = 1, int holdMs = 1500)
	{
		using var settingsFile = new TempSettingsFile("nudge", "{}");

		var manager = new SettingsManager(settingsFile.FilePath);
		var settings = new Settings
		{
			AzureComputerVisionSettings = new AzureComputerVisionSettings
			{
				PointerNudgeIntervalMilliseconds = intervalMs,
				PointerNudgeDurationMilliseconds = durationMs,
				PointerNudgeDistancePixels = distancePx,
				PointerHoldMilliseconds = holdMs,
			},
		};
		manager.EnsureSettings(settings, isNewFile: false);
		return settings.AzureComputerVisionSettings!;
	}
}
