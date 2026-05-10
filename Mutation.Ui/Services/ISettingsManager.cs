using CognitiveSupport;

namespace Mutation.Ui.Services;

public interface ISettingsManager
{
	string SettingsFilePath { get; }
	void SaveSettingsToFile(Settings settings);
	void UpgradeSettings();
	Settings LoadAndEnsureSettings();
}