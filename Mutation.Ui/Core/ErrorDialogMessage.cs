using System;
using CognitiveSupport;

namespace Mutation.Ui.Core;

/// <summary>
/// Composes what an error dialog puts on screen and reads out.
/// <para>
/// Two rules, both of them about what leaves the machine. First, the text is run
/// through the same exact-match redactor the error log uses, so a configured provider
/// key can never appear in a dialog — dialogs are read aloud and routinely
/// screenshotted into bug reports, and the redaction seam was previously applied only
/// when writing the log file. Second, the dialog carries the exception's own message
/// and a pointer to the log, not the whole <c>ToString()</c> chain: the stack and the
/// inner-exception detail are what tend to echo request identifiers and header
/// fragments, and they are no use to a reader anyway (issue #242).
/// </para>
/// Pure apart from the redactor, so the wording is testable without a window.
/// </summary>
public static class ErrorDialogMessage
{
	/// <summary>Lead-in for the log pointer, so tests and callers agree on one wording.</summary>
	public const string LogPointerLead = "Full technical details are in the log file:";

	/// <param name="exception">The failure being reported; null yields a neutral placeholder.</param>
	/// <param name="logPath">Where the full detail was written — normally <see cref="ErrorLogger.PrimaryLogPath"/>.</param>
	public static string ForException(Exception? exception, string? logPath)
	{
		string detail = Redact(Describe(exception));
		string pointer = string.IsNullOrWhiteSpace(logPath)
			? string.Empty
			: $"{Environment.NewLine}{Environment.NewLine}{LogPointerLead}{Environment.NewLine}{logPath}";

		return $"An error occurred:{Environment.NewLine}{detail}{pointer}";
	}

	/// <summary>
	/// Redacts a message the caller composed itself (a validation failure, say). No log
	/// pointer: most of these never reach the log, and promising one that is not there
	/// sends the reader looking for nothing.
	/// </summary>
	public static string ForMessage(string? message) => Redact(message ?? string.Empty);

	// The outer message is what names the operation; the innermost one is usually what
	// actually went wrong, and wrappers like AggregateException say nothing on their own.
	// Everything between the two is the part a reader cannot use.
	private static string Describe(Exception? exception)
	{
		if (exception is null)
			return "No further detail is available.";

		string outer = Blank(exception.Message) ? exception.GetType().Name : exception.Message.Trim();

		Exception innermost = exception;
		while (innermost.InnerException is not null)
			innermost = innermost.InnerException;

		if (ReferenceEquals(innermost, exception))
			return outer;

		string inner = Blank(innermost.Message) ? innermost.GetType().Name : innermost.Message.Trim();
		return string.Equals(inner, outer, StringComparison.Ordinal)
			? outer
			: $"{outer}{Environment.NewLine}{inner}";
	}

	private static bool Blank(string? value) => string.IsNullOrWhiteSpace(value);

	private static string Redact(string text) =>
		string.IsNullOrEmpty(text) ? text : ErrorLogger.RedactSecrets(text);
}
