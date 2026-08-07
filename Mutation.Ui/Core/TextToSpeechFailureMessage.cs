using System;
using System.Collections.Generic;

namespace Mutation.Ui.Core;

/// <summary>
/// Composes what the user is told when a text-to-speech action fails.
/// <para>
/// The common cause is a voice that is no longer there — a Windows update removed it,
/// or a settings file was copied from another machine — and the raw exception from the
/// speech engine says nothing a reader can act on. When the configured voice is missing
/// from the installed list, the message says so and says where to pick another one.
/// </para>
/// Pure so the wording can be tested without a synthesizer (issue #235).
/// </summary>
public static class TextToSpeechFailureMessage
{
	/// <param name="failureDetail">The engine's own message; may be blank.</param>
	/// <param name="configuredVoiceName">The voice the app was asked to use; blank or null means the system default.</param>
	/// <param name="installedVoices">Voices currently installed. Pass an empty list when enumeration itself failed.</param>
	public static string Compose(
		string? failureDetail,
		string? configuredVoiceName,
		IReadOnlyList<string>? installedVoices)
	{
		string detail = string.IsNullOrWhiteSpace(failureDetail)
			? "Speech failed."
			: failureDetail!.Trim();

		if (!IsVoiceMissing(configuredVoiceName, installedVoices))
			return detail;

		return $"{detail} The voice \"{configuredVoiceName!.Trim()}\" is not installed. " +
			"Choose another one in the Voice list on the Voice & Speech card.";
	}

	/// <summary>
	/// True when a specific voice is configured and it is not among the installed ones.
	/// An empty installed list is treated as "cannot tell" rather than "none installed":
	/// enumeration failing is its own problem and must not produce a wrong explanation.
	/// </summary>
	public static bool IsVoiceMissing(string? configuredVoiceName, IReadOnlyList<string>? installedVoices)
	{
		if (string.IsNullOrWhiteSpace(configuredVoiceName)) return false;
		if (installedVoices is null || installedVoices.Count == 0) return false;

		string wanted = configuredVoiceName!.Trim();
		foreach (string installed in installedVoices)
		{
			if (string.Equals(installed?.Trim(), wanted, StringComparison.OrdinalIgnoreCase))
				return false;
		}
		return true;
	}
}
