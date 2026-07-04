using System;
using System.IO;
using Mutation.Ui.Views.SettingsUi;
using Xunit;

namespace Mutation.Tests;

public class SettingsFailureFeedbackTests
{
	[Fact]
	public void ComposeMessage_IncludesExceptionMessageAndRetryGuidance()
	{
		string message = SettingsFailureFeedback.ComposeMessage(
			new IOException("The file is locked by another process."));

		Assert.StartsWith("The file is locked by another process.", message);
		Assert.Contains("Your changes were not saved.", message);
		Assert.Contains("press Save again", message);
	}

	[Fact]
	public void ComposeMessage_UnwrapsToInnermostException()
	{
		var wrapped = new InvalidOperationException(
			"Serialization failed.",
			new UnauthorizedAccessException("Access to the path is denied."));

		string message = SettingsFailureFeedback.ComposeMessage(wrapped);

		Assert.StartsWith("Access to the path is denied.", message);
		Assert.DoesNotContain("Serialization failed.", message);
	}

	[Fact]
	public void ComposeMessage_NullException_UsesFallbackText()
	{
		string message = SettingsFailureFeedback.ComposeMessage(null);

		Assert.StartsWith("An unknown error occurred.", message);
		Assert.Contains("Your changes were not saved.", message);
	}

	[Fact]
	public void ComposeMessage_BlankExceptionMessage_UsesFallbackText()
	{
		string message = SettingsFailureFeedback.ComposeMessage(new Exception("   "));

		Assert.StartsWith("An unknown error occurred.", message);
	}

	[Fact]
	public void ComposeOpenJsonMessage_ReturnsInnermostDetailOnly()
	{
		string message = SettingsFailureFeedback.ComposeOpenJsonMessage(
			new Exception("outer", new Exception("No application is associated.")));

		Assert.Equal("No application is associated.", message);
	}
}
