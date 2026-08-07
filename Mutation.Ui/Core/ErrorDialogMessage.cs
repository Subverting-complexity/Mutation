using System;
using System.Collections.Generic;
using System.Linq;
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
/// and a pointer to the log, not the whole <c>ToString()</c> chain: the stack is what
/// tends to echo request identifiers and header fragments, and it is no use to a reader
/// anyway (issue #242).
/// </para>
/// Pure apart from the redactor, so the wording is testable without a window.
/// </summary>
public static class ErrorDialogMessage
{
	/// <summary>Lead-in for the log pointer, so tests and callers agree on one wording.</summary>
	public const string LogPointerLead = "Full technical details are in the log file:";

	/// <summary>
	/// How many distinct messages from the exception chain the summary reads out.
	/// <para>
	/// The dialog is read aloud, so this is a listening budget rather than a screen one:
	/// four lines is about as much as stays a headline. Anything past it is covered by the
	/// log pointer, and chains that deep are rare (issue #288).
	/// </para>
	/// </summary>
	public const int MaxChainMessages = 4;

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

	// The outer message names the operation and the innermost is usually the raw cause, but
	// the sentence that tells the reader what to do about it is often neither. A provider call
	// that fails with a 401 wraps it as "Speech to text failed." over "…401 (Unauthorized)."
	// over a socket error: report only the ends and the reader hears a dropped connection and
	// never learns their API key is wrong. So every level speaks, up to the cap.
	private static string Describe(Exception? exception)
	{
		if (exception is null)
			return "No further detail is available.";

		var messages = new List<string>();
		for (Exception? level = exception; level is not null; level = level.InnerException)
		{
			string message = Blank(level.Message) ? level.GetType().Name : level.Message.Trim();

			// Wrappers that restate what they wrap are common enough that without this the
			// summary reads as a stutter — and hearing the same sentence twice suggests two
			// failures rather than one.
			if (messages.Contains(message, StringComparer.Ordinal))
				continue;

			messages.Add(message);
			if (messages.Count == MaxChainMessages)
				break;
		}

		return string.Join(Environment.NewLine, messages);
	}

	private static bool Blank(string? value) => string.IsNullOrWhiteSpace(value);

	private static string Redact(string text) =>
		string.IsNullOrEmpty(text) ? text : ErrorLogger.RedactSecrets(text);
}
