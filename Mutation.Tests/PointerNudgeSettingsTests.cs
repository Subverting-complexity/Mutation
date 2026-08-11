using CognitiveSupport;
using Mutation.Ui.Services;
using Newtonsoft.Json;

namespace Mutation.Tests;

// The pointer-nudge settings contract (issue #373): off by default with sensible timings,
// backward compatible with a settings file written before the feature existed, and clamped on
// load to the same range the Settings dialog offers — so a hand-edited file cannot produce a
// nudge nobody can stop.
public class PointerNudgeSettingsTests
{
	[Fact]
	public void OffByDefault_WithFiftyAndFiveHundred()
	{
		// It moves the pointer on purpose. Nobody gets that uninvited.
		var ocr = new AzureComputerVisionSettings();

		Assert.False(ocr.NudgePointerAfterCapture);
		Assert.Equal(50, ocr.PointerNudgeIntervalMilliseconds);
		Assert.Equal(500, ocr.PointerNudgeDurationMilliseconds);
	}

	[Fact]
	public void SettingsFileWrittenBeforeTheFeature_KeepsTheDefaults()
	{
		// No keys in the file, so the deserialiser leaves the property initializers alone.
		var ocr = JsonConvert.DeserializeObject<AzureComputerVisionSettings>("{\"TimeoutSeconds\":10}");

		Assert.NotNull(ocr);
		Assert.False(ocr!.NudgePointerAfterCapture);
		Assert.Equal(50, ocr.PointerNudgeIntervalMilliseconds);
		Assert.Equal(500, ocr.PointerNudgeDurationMilliseconds);
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
	[InlineData(0, 500)]
	[InlineData(-10, 500)]
	[InlineData(10, 50)]
	[InlineData(50, 50)]
	[InlineData(500, 500)]
	[InlineData(5000, 5000)]
	[InlineData(60000, 5000)]
	public void DurationIsClampedOnLoad(int stored, int expected)
	{
		Assert.Equal(expected, Load(durationMs: stored).PointerNudgeDurationMilliseconds);
	}

	[Fact]
	public void ValuesAlreadyInRange_AreLeftExactlyAsTheyAre()
	{
		// Whatever the user picked in the dialog has to survive a reload untouched.
		var ocr = Load(intervalMs: 120, durationMs: 3000);

		Assert.Equal(120, ocr.PointerNudgeIntervalMilliseconds);
		Assert.Equal(3000, ocr.PointerNudgeDurationMilliseconds);
	}

	[Fact]
	public void TheToggleIsNeverTurnedOnForAnybody()
	{
		Assert.False(Load().NudgePointerAfterCapture);
	}

	private static AzureComputerVisionSettings Load(int intervalMs = 50, int durationMs = 500)
	{
		using var settingsFile = new TempSettingsFile("nudge", "{}");

		var manager = new SettingsManager(settingsFile.FilePath);
		var settings = new Settings
		{
			AzureComputerVisionSettings = new AzureComputerVisionSettings
			{
				PointerNudgeIntervalMilliseconds = intervalMs,
				PointerNudgeDurationMilliseconds = durationMs,
			},
		};
		manager.EnsureSettings(settings, isNewFile: false);
		return settings.AzureComputerVisionSettings!;
	}
}
