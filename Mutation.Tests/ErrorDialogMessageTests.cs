using System;
using CognitiveSupport;
using Mutation.Ui.Core;
using Xunit;

namespace Mutation.Tests;

// The error dialog used to render the whole exception chain, unredacted, into both the
// dialog body and its HelpText — so a screen reader read out anything the provider SDK
// had put in the message, in a dialog users routinely screenshot into bug reports
// (issue #242).
//
// These tests drive ErrorLogger's process-wide secret registry, so the class shares the
// collection that serialises it.
[Collection(ErrorLoggerCollection.Name)]
public class ErrorDialogMessageTests
{
	private const string LogPath = @"C:\Users\someone\AppData\Local\Mutation\logs\Mutation_Errors.log";

	[Fact]
	public void ForException_RegisteredKey_IsRedacted()
	{
		string deepgramKey = "0123456789abcdef0123456789abcdef01234567"; // 40-char hex, matched by no pattern
		ErrorLogger.RegisterSecretValues(new[] { deepgramKey });

		string result = ErrorDialogMessage.ForException(
			new InvalidOperationException($"Request rejected for key {deepgramKey}."), LogPath);

		Assert.DoesNotContain(deepgramKey, result);
		Assert.Contains("***REDACTED***", result);
	}

	[Fact]
	public void ForException_KeyInsideTheInnerException_IsRedacted()
	{
		string key = "sk-abcdEFGH1234567890";
		ErrorLogger.RegisterSecretValues(null);

		string result = ErrorDialogMessage.ForException(
			new InvalidOperationException("Chat completion failed.", new Exception($"401 for {key}")),
			LogPath);

		Assert.DoesNotContain(key, result);
	}

	[Fact]
	public void ForException_DoesNotIncludeTheStackTrace()
	{
		ErrorLogger.RegisterSecretValues(null);
		Exception thrown;
		try { throw new InvalidOperationException("Chat completion failed."); }
		catch (Exception ex) { thrown = ex; }

		string result = ErrorDialogMessage.ForException(thrown, LogPath);

		Assert.Contains("Chat completion failed.", result);
		Assert.DoesNotContain("   at ", result);
		Assert.DoesNotContain(nameof(ForException_DoesNotIncludeTheStackTrace), result);
	}

	[Fact]
	public void ForException_PointsAtTheLogFile()
	{
		ErrorLogger.RegisterSecretValues(null);

		string result = ErrorDialogMessage.ForException(new Exception("Nope."), LogPath);

		Assert.Contains(ErrorDialogMessage.LogPointerLead, result);
		Assert.Contains(LogPath, result);
	}

	// A wrapper says nothing on its own; without the innermost message the dialog would
	// tell the reader only that something happened.
	[Fact]
	public void ForException_WrappedFailure_KeepsBothMessages()
	{
		ErrorLogger.RegisterSecretValues(null);

		string result = ErrorDialogMessage.ForException(
			new InvalidOperationException("Could not read the recording.", new Exception("The file is in use.")),
			LogPath);

		Assert.Contains("Could not read the recording.", result);
		Assert.Contains("The file is in use.", result);
	}

	[Fact]
	public void ForException_SameMessageBothLevels_IsNotRepeated()
	{
		ErrorLogger.RegisterSecretValues(null);

		string result = ErrorDialogMessage.ForException(
			new InvalidOperationException("Timed out.", new TimeoutException("Timed out.")), LogPath);

		Assert.Equal(result.IndexOf("Timed out.", StringComparison.Ordinal),
			result.LastIndexOf("Timed out.", StringComparison.Ordinal));
	}

	// The outer message names the operation and the innermost is the raw cause, but the
	// sentence telling the reader what to do is often the one in between. Reporting only the
	// ends left the user hearing "connection closed" when the real answer was a bad API key
	// (issue #288).
	[Fact]
	public void ForException_ThreeLevelChain_KeepsTheActionableMiddleMessage()
	{
		ErrorLogger.RegisterSecretValues(null);

		string result = ErrorDialogMessage.ForException(
			new InvalidOperationException("Speech to text failed.",
				new InvalidOperationException("Response status code does not indicate success: 401 (Unauthorized).",
					new Exception("An existing connection was forcibly closed by the remote host."))),
			LogPath);

		Assert.Contains("Speech to text failed.", result);
		Assert.Contains("401 (Unauthorized).", result);
		Assert.Contains("An existing connection was forcibly closed by the remote host.", result);
	}

	// A wrapper restating a message from further out adds a line and no information, and
	// hearing the same sentence twice reads as two failures rather than one.
	[Fact]
	public void ForException_MessageRepeatedNonConsecutively_IsSaidOnce()
	{
		ErrorLogger.RegisterSecretValues(null);

		string result = ErrorDialogMessage.ForException(
			new InvalidOperationException("Upload failed.",
				new Exception("Disk is full.",
					new Exception("Upload failed."))),
			LogPath);

		Assert.Equal(result.IndexOf("Upload failed.", StringComparison.Ordinal),
			result.LastIndexOf("Upload failed.", StringComparison.Ordinal));
	}

	// The dialog is read aloud, so a pathological chain must not become a wall of speech.
	// Past the cap the log pointer is the answer.
	[Fact]
	public void ForException_ChainDeeperThanTheCap_IsBounded()
	{
		ErrorLogger.RegisterSecretValues(null);

		Exception chain = new Exception("Level 7.");
		for (int level = 6; level >= 1; level--)
			chain = new Exception($"Level {level}.", chain);

		string result = ErrorDialogMessage.ForException(chain, LogPath);

		for (int level = 1; level <= ErrorDialogMessage.MaxChainMessages; level++)
			Assert.Contains($"Level {level}.", result);

		Assert.DoesNotContain($"Level {ErrorDialogMessage.MaxChainMessages + 1}.", result);
		Assert.Contains(ErrorDialogMessage.LogPointerLead, result);
	}

	[Fact]
	public void ForException_MessagelessException_NamesTheType()
	{
		ErrorLogger.RegisterSecretValues(null);

		string result = ErrorDialogMessage.ForException(new OperationCanceledException(string.Empty), LogPath);

		Assert.Contains(nameof(OperationCanceledException), result);
	}

	[Fact]
	public void ForException_NullException_StillReadsAsAnError()
	{
		ErrorLogger.RegisterSecretValues(null);

		string result = ErrorDialogMessage.ForException(null, LogPath);

		Assert.Contains("An error occurred:", result);
		Assert.Contains(LogPath, result);
	}

	// Promising a log file that has nothing in it sends the reader looking for nothing.
	[Fact]
	public void ForException_NoLogPath_OmitsThePointer()
	{
		ErrorLogger.RegisterSecretValues(null);

		string result = ErrorDialogMessage.ForException(new Exception("Nope."), "   ");

		Assert.DoesNotContain(ErrorDialogMessage.LogPointerLead, result);
		Assert.Contains("Nope.", result);
	}

	[Fact]
	public void ForMessage_RegisteredKey_IsRedacted()
	{
		string key = "0123456789abcdef0123456789abcdef01234567";
		ErrorLogger.RegisterSecretValues(new[] { key });

		string result = ErrorDialogMessage.ForMessage($"Test failed: bad key {key}");

		Assert.DoesNotContain(key, result);
		Assert.StartsWith("Test failed:", result);
	}

	[Fact]
	public void ForMessage_PlainValidationText_IsUnchanged()
	{
		ErrorLogger.RegisterSecretValues(null);

		Assert.Equal("Name is required.", ErrorDialogMessage.ForMessage("Name is required."));
	}

	[Fact]
	public void ForMessage_Null_ReturnsEmpty()
	{
		ErrorLogger.RegisterSecretValues(null);

		Assert.Equal(string.Empty, ErrorDialogMessage.ForMessage(null));
	}
}
