using System;

namespace Mutation.Ui;

/// <summary>
/// Thrown when a new recording cannot start because the audio recorder lock
/// is still held by a previous operation (typically an in-flight
/// transcription). Callers surface this as "still busy" feedback instead of
/// a generic error.
/// </summary>
public sealed class RecorderBusyException : Exception
{
	public RecorderBusyException(string message)
		: base(message)
	{
	}
}
