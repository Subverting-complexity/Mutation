using System.IO;
using CognitiveSupport;
using Mutation.Ui.Services;
using Newtonsoft.Json;

namespace Mutation.Tests;

// Covers the retained-session count contract: a sensible default, clamping to the
// supported [1, 500] range, and backward compatibility for settings files written
// before the field existed.
public class RetainedSessionsClampTests
{
	[Fact]
	public void Default_Is_Ten()
	{
		Assert.Equal(10, new SpeechToTextSettings().MaxRetainedSessions);
		Assert.Equal(10, SpeechToTextSettings.DefaultMaxRetainedSessions);
	}

	[Theory]
	[InlineData(0, 1)]
	[InlineData(-5, 1)]
	[InlineData(1, 1)]
	[InlineData(10, 10)]
	[InlineData(500, 500)]
	[InlineData(501, 500)]
	[InlineData(100000, 500)]
	public void Clamp_Bounds_To_1_Through_500(int input, int expected)
	{
		Assert.Equal(expected, SpeechToTextSettings.ClampRetainedSessions(input));
	}

	[Fact]
	public void MissingField_DeserializesAs_Ten()
	{
		// A settings file written before this field existed has no MaxRetainedSessions key.
		var stt = JsonConvert.DeserializeObject<SpeechToTextSettings>("{\"TempDirectory\":\"C:\\\\Temp\"}");
		Assert.NotNull(stt);
		Assert.Equal(10, stt!.MaxRetainedSessions);
	}

	[Fact]
	public void EnsureSettings_ClampsOutOfRangeValueOnLoad()
	{
		Assert.Equal(1, ApplyEnsure(0).SpeechToTextSettings!.MaxRetainedSessions);
		Assert.Equal(500, ApplyEnsure(9999).SpeechToTextSettings!.MaxRetainedSessions);
		Assert.Equal(42, ApplyEnsure(42).SpeechToTextSettings!.MaxRetainedSessions);
	}

	private static Settings ApplyEnsure(int storedValue)
	{
		string tempPath = Path.Combine(Path.GetTempPath(), $"mutation-retain-{System.Guid.NewGuid():N}.json");
		File.WriteAllText(tempPath, "{}");
		try
		{
			var manager = new SettingsManager(tempPath);
			var settings = new Settings
			{
				SpeechToTextSettings = new SpeechToTextSettings { MaxRetainedSessions = storedValue },
			};
			manager.EnsureSettings(settings, isNewFile: false);
			return settings;
		}
		finally
		{
			if (File.Exists(tempPath))
				File.Delete(tempPath);
		}
	}
}
