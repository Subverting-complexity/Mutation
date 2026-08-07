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
