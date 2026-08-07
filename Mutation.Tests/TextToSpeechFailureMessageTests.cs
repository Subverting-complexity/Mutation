using System;
using Mutation.Ui.Core;
using Xunit;

namespace Mutation.Tests;

// What a text-to-speech failure says to the user. The speak actions are reachable from
// global hotkeys, where a failure otherwise produces nothing at all, so the wording is
// the whole feedback the reader gets (issue #235). The usual cause — a voice that is no
// longer installed — is named explicitly, because the engine's own message never is.
public class TextToSpeechFailureMessageTests
{
	private static readonly string[] Installed = { "Microsoft David", "Microsoft Zira" };

	[Fact]
	public void InstalledVoice_KeepsTheEngineMessageAsIs()
	{
		string message = TextToSpeechFailureMessage.Compose(
			"The audio device is unavailable.", "Microsoft Zira", Installed);

		Assert.Equal("The audio device is unavailable.", message);
	}

	[Fact]
	public void MissingVoice_NamesItAndSaysWhereToChangeIt()
	{
		string message = TextToSpeechFailureMessage.Compose(
			"Speech failed.", "Microsoft Hazel", Installed);

		Assert.Contains("Microsoft Hazel", message);
		Assert.Contains("not installed", message);
		Assert.Contains("Voice", message);
	}

	[Fact]
	public void DefaultVoice_IsNeverReportedAsMissing()
	{
		// No voice configured means "use whatever Windows defaults to", which cannot be
		// the missing one.
		Assert.False(TextToSpeechFailureMessage.IsVoiceMissing(null, Installed));
		Assert.False(TextToSpeechFailureMessage.IsVoiceMissing("", Installed));
		Assert.False(TextToSpeechFailureMessage.IsVoiceMissing("   ", Installed));
	}

	[Fact]
	public void VoiceMatchIgnoresCaseAndSurroundingSpace()
	{
		Assert.False(TextToSpeechFailureMessage.IsVoiceMissing(" microsoft david ", Installed));
	}

	[Fact]
	public void UnknownVoiceList_DoesNotClaimTheVoiceIsMissing()
	{
		// Enumeration failing is its own problem. Guessing "your voice is gone" from an
		// empty list would send the reader to change a setting that was never at fault.
		Assert.False(TextToSpeechFailureMessage.IsVoiceMissing("Microsoft Hazel", Array.Empty<string>()));
		Assert.False(TextToSpeechFailureMessage.IsVoiceMissing("Microsoft Hazel", null));
	}

	[Fact]
	public void BlankEngineMessage_StillSaysSomething()
	{
		// Some speech exceptions carry no message at all; an empty status line reads as
		// "nothing happened", which is exactly the failure mode being fixed.
		string message = TextToSpeechFailureMessage.Compose("   ", null, Installed);

		Assert.False(string.IsNullOrWhiteSpace(message));
	}

	[Fact]
	public void BlankEngineMessage_WithMissingVoice_StillNamesTheVoice()
	{
		string message = TextToSpeechFailureMessage.Compose(null, "Microsoft Hazel", Installed);

		Assert.False(string.IsNullOrWhiteSpace(message));
		Assert.Contains("Microsoft Hazel", message);
	}
}
