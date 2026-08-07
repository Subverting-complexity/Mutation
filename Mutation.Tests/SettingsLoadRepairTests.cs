using System;
using System.IO;
using System.Linq;
using CognitiveSupport;
using Mutation.Ui.Services;
using Mutation.Ui.Views.SettingsUi;
using Xunit;

namespace Mutation.Tests;

// Loading a real settings file end to end, for the two faults that used to survive
// the load: an unusable temp directory (#230) and a router mapping with a missing
// hotkey, which took the whole file down with it (#247).
//
// LoadAndEnsureSettings feeds the loaded keys to ErrorLogger's process-wide redactor,
// so this class shares the collection that serialises those statics.
[Collection(ErrorLoggerCollection.Name)]
public class SettingsLoadRepairTests
{
	private static Settings Load(string json)
	{
		using var file = new TempSettingsFile("load-repair", json);
		return new SettingsManager(file.FilePath).LoadAndEnsureSettings();
	}

	// A settings file carrying only the section under test. Services is spelled out
	// because UpgradeSettings treats a missing one as a pre-Services legacy file and
	// rewrites the section.
	private static string SpeechFileWithTempDirectory(string jsonValue) => $$"""
	{
		"SpeechToTextSettings": { "Services": [], "TempDirectory": {{jsonValue}} }
	}
	""";

	// Blank and null were already handled before this change and are kept as
	// regression cover; the relative path is the case that used to get through.
	[Theory]
	[InlineData("\"\"")]
	[InlineData("\"   \"")]
	[InlineData("\"Sessions\"")]
	[InlineData("null")]
	public void Load_UnusableTempDirectory_FallsBackToTheDefault(string jsonValue)
	{
		var settings = Load(SpeechFileWithTempDirectory(jsonValue));

		Assert.Equal(SettingsDefaults.Speech.TempDirectory, settings.SpeechToTextSettings!.TempDirectory);
	}

	[Fact]
	public void Load_UnusableTempDirectory_IsReported()
	{
		using var file = new TempSettingsFile("load-repair", SpeechFileWithTempDirectory("\"Sessions\""));
		var manager = new SettingsManager(file.FilePath);

		manager.LoadAndEnsureSettings();

		Assert.NotNull(manager.TempDirectoryIssue);
		Assert.Contains("not a full path", manager.TempDirectoryIssue);
		Assert.Contains(SettingsDefaults.Speech.TempDirectory, manager.TempDirectoryIssue);
	}

	[Fact]
	public void Load_UsableTempDirectory_ReportsNothing()
	{
		using var file = new TempSettingsFile("load-repair", SpeechFileWithTempDirectory("\"D:\\\\MyRecordings\""));
		var manager = new SettingsManager(file.FilePath);

		manager.LoadAndEnsureSettings();

		Assert.Null(manager.TempDirectoryIssue);
	}

	// The migration off the old world-readable default is not a fault of the user's,
	// and it already moves the recordings across — nothing to raise at startup.
	[Fact]
	public void Load_LegacyTempDirectory_ReportsNothing()
	{
		using var file = new TempSettingsFile("load-repair", SpeechFileWithTempDirectory("\"C:\\\\Temp\\\\Mutation\""));
		var manager = new SettingsManager(file.FilePath);

		var settings = manager.LoadAndEnsureSettings();

		Assert.Equal(SettingsDefaults.Speech.TempDirectory, settings.SpeechToTextSettings!.TempDirectory);
		Assert.Null(manager.TempDirectoryIssue);
	}

	// EnsureSettings runs on every launch. A path it rewrites to something it would
	// rewrite again means a settings file churned on each start, and a startup notice
	// the user can never clear.
	[Theory]
	[InlineData(@"D:\MyRecordings")]
	[InlineData(@"D:\MyRecordings\")]
	[InlineData(@"d:\myrecordings")]
	[InlineData(@"\\server\share\Mutation")]
	[InlineData("Sessions")]
	[InlineData("")]
	public void EnsureSettings_RunTwice_SettlesOnTheSameTempDirectory(string tempDirectory)
	{
		var settings = new Settings
		{
			SpeechToTextSettings = new SpeechToTextSettings { TempDirectory = tempDirectory },
		};
		var manager = new SettingsManager("unused.json");

		manager.EnsureSettings(settings, isNewFile: false);
		string afterFirst = settings.SpeechToTextSettings.TempDirectory!;
		manager.EnsureSettings(settings, isNewFile: false);

		Assert.Equal(afterFirst, settings.SpeechToTextSettings.TempDirectory);
		Assert.Null(manager.TempDirectoryIssue);
	}

	[Fact]
	public void Load_UnusableTempDirectory_IsWrittenBackToTheFile()
	{
		using var file = new TempSettingsFile("load-repair", SpeechFileWithTempDirectory("\"\""));

		new SettingsManager(file.FilePath).LoadAndEnsureSettings();

		// A repair that is not persisted has to be redone every launch, and the user
		// never sees the corrected path in the Settings dialog.
		Assert.Contains(SettingsDefaults.Speech.TempDirectory.Replace(@"\", @"\\"), File.ReadAllText(file.FilePath));
	}

	[Fact]
	public void Load_CustomTempDirectory_IsKeptAsIs()
	{
		var settings = Load(SpeechFileWithTempDirectory("\"D:\\\\MyRecordings\""));

		Assert.Equal(@"D:\MyRecordings", settings.SpeechToTextSettings!.TempDirectory);
	}

	[Fact]
	public void Load_RouterMappingWithNullHotkey_LoadsTheRestOfTheFile()
	{
		var settings = Load("""
		{
			"HotKeyRouterSettings": {
				"Mappings": [ { "FromHotKey": null, "ToHotKey": "CONTROL+SHIFT+ALT+9" } ]
			},
			"MainWindowUiSettings": { "MaxTextBoxLineCount": 7 }
		}
		""");

		// The point of the fix: one unfinished mapping no longer costs every other setting.
		Assert.Equal(7, settings.MainWindowUiSettings!.MaxTextBoxLineCount);
		Assert.Equal(string.Empty, settings.HotKeyRouterSettings!.Mappings.Single().FromHotKey);
		Assert.Equal("CONTROL+SHIFT+ALT+9", settings.HotKeyRouterSettings.Mappings.Single().ToHotKey);
	}

	[Fact]
	public void Load_RouterMappingMissingAHotkeyKeyEntirely_IsRepaired()
	{
		var settings = Load("""
		{
			"HotKeyRouterSettings": { "Mappings": [ { "ToHotKey": "CONTROL+9" } ] }
		}
		""");

		Assert.Equal(string.Empty, settings.HotKeyRouterSettings!.Mappings.Single().FromHotKey);
	}

	[Fact]
	public void Load_RouterMappingWithNullHotkey_IsReported()
	{
		using var file = new TempSettingsFile("load-repair", """
		{
			"HotKeyRouterSettings": { "Mappings": [ { "FromHotKey": null, "ToHotKey": null } ] }
		}
		""");
		var manager = new SettingsManager(file.FilePath);

		manager.LoadAndEnsureSettings();

		Assert.Equal(2, manager.HotKeyRouterIssues.Count);
		Assert.All(manager.HotKeyRouterIssues, issue => Assert.Contains("mapping 1", issue));
	}

	// Issue #283. A SpeechToTextSettings section with no Services array was read as a
	// pre-Services legacy file and collapsed into one synthesized service — and with no
	// legacy Service name to carry over, the synthesized Provider was "". The very next
	// deserialize threw on that empty enum value, so a file the upgrade had just written
	// could not be loaded at all. Reachable from the dialog's own "Open JSON" button.
	[Fact]
	public void Load_SpeechSectionWithNoServicesArray_StillLoads()
	{
		var settings = Load("""
		{
			"SpeechToTextSettings": { "TempDirectory": "D:\\MyRecordings" },
			"MainWindowUiSettings": { "MaxTextBoxLineCount": 7 }
		}
		""");

		// Nothing legacy to migrate, so the section is left for EnsureSettings to seed
		// with the ordinary OpenAI Whisper default — and the rest of the file survives.
		Assert.Equal(7, settings.MainWindowUiSettings!.MaxTextBoxLineCount);
		Assert.Equal(@"D:\MyRecordings", settings.SpeechToTextSettings!.TempDirectory);
		var service = Assert.Single(settings.SpeechToTextSettings.Services!);
		Assert.Equal(SpeechToTextProviders.OpenAi, service.Provider);
		Assert.Equal(SettingsDefaults.Speech.DefaultServiceName, service.Name);
	}

	// The other half of #283: a Provider the enum does not recognise, however it got
	// there, must cost that one service its stored value — not the whole settings file.
	[Theory]
	[InlineData("\"\"")]
	[InlineData("\"   \"")]
	[InlineData("\"Whisper\"")]
	[InlineData("\"None\"")]
	[InlineData("null")]
	public void Load_UnusableProvider_IsRepairedRatherThanFatal(string jsonValue)
	{
		var settings = Load($$"""
		{
			"SpeechToTextSettings": {
				"Services": [ { "Name": "My service", "Provider": {{jsonValue}}, "ModelId": "whisper-1" } ]
			}
		}
		""");

		var service = Assert.Single(settings.SpeechToTextSettings!.Services!);
		Assert.Equal(SpeechToTextProviders.OpenAi, service.Provider);
		// Only the provider is repaired; everything the user did configure is kept.
		Assert.Equal("My service", service.Name);
		Assert.Equal("whisper-1", service.ModelId);
	}

	[Fact]
	public void Load_RecognisedProvider_IsKeptAsIs()
	{
		var settings = Load("""
		{
			"SpeechToTextSettings": {
				"Services": [ { "Name": "Deepgram Nova", "Provider": "Deepgram" } ]
			}
		}
		""");

		Assert.Equal(SpeechToTextProviders.Deepgram, settings.SpeechToTextSettings!.Services!.Single().Provider);
	}

	[Fact]
	public void Load_WellFormedRouterMappings_ReportNothing()
	{
		using var file = new TempSettingsFile("load-repair", """
		{
			"HotKeyRouterSettings": {
				"Mappings": [ { "FromHotKey": "CONTROL+8", "ToHotKey": "CONTROL+9" } ]
			}
		}
		""");
		var manager = new SettingsManager(file.FilePath);

		var settings = manager.LoadAndEnsureSettings();

		Assert.Empty(manager.HotKeyRouterIssues);
		Assert.Equal("CONTROL+8", settings.HotKeyRouterSettings!.Mappings.Single().FromHotKey);
	}
}
