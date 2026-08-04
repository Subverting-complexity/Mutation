namespace CognitiveSupport;

/// <summary>
/// Checks that every custom beep setting points at a <c>.wav</c> file that exists.
///
/// It reports rather than acts: the caller decides what to do with the issues (switch
/// custom beeps back off, tell the user, both). This is what keeps the check out of
/// <c>SettingsManager</c>, which used to raise its own bare Win32 message box from a
/// point in startup where no window existed yet — a message a screen reader could make
/// nothing of.
/// </summary>
public static class CustomBeepFileValidator
{
	private const string WavExtension = ".wav";

	// The label used in the message for each setting, in the order they are reported.
	private static readonly (string Label, Func<AudioSettings.CustomBeepSettingsData, string?> Read)[] Files =
	{
		("success", static s => s.BeepSuccessFile),
		("failure", static s => s.BeepFailureFile),
		("start", static s => s.BeepStartFile),
		("end", static s => s.BeepEndFile),
		("mute", static s => s.BeepMuteFile),
		("unmute", static s => s.BeepUnmuteFile),
	};

	/// <summary>
	/// One message per unusable custom beep file; empty when all six resolve to an
	/// existing <c>.wav</c>, and empty when custom beeps are switched off (there is
	/// nothing to load, so nothing to complain about).
	/// </summary>
	/// <param name="fileExists">Existence check, injected so the rule is testable
	/// without laying real files on disk.</param>
	public static IReadOnlyList<string> Validate(
		AudioSettings.CustomBeepSettingsData? settings,
		Func<string, bool> fileExists)
	{
		if (fileExists is null) throw new ArgumentNullException(nameof(fileExists));

		if (settings is null || !settings.UseCustomBeeps)
			return Array.Empty<string>();

		var issues = new List<string>();
		foreach (var (label, read) in Files)
		{
			string path = read(settings) ?? string.Empty;
			if (IsUsable(settings, path, fileExists))
				continue;

			// A wrong extension is named separately: "could not load" sends the user
			// looking for a missing file when the file is there and simply not a .wav.
			issues.Add(HasWrongExtension(path)
				? $"Custom {label} beep must be a .wav file: {path}"
				: $"Could not load {label} beep file: {path}");
		}

		return issues;
	}

	private static bool IsUsable(
		AudioSettings.CustomBeepSettingsData settings,
		string path,
		Func<string, bool> fileExists)
	{
		if (string.IsNullOrWhiteSpace(path))
			return false;

		if (!string.Equals(Path.GetExtension(path), WavExtension, StringComparison.OrdinalIgnoreCase))
			return false;

		return fileExists(settings.ResolveAudioFilePath(path));
	}

	private static bool HasWrongExtension(string path) =>
		!string.IsNullOrWhiteSpace(path)
		&& !string.Equals(Path.GetExtension(path), WavExtension, StringComparison.OrdinalIgnoreCase);
}
