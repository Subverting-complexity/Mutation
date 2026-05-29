using System;

namespace CognitiveSupport;

/// <summary>
/// Thrown when silence stripping reduces a recording to less than the minimum amount of
/// speech worth transcribing, so the API call is skipped.
/// </summary>
public sealed class NoSpeechDetectedException : Exception
{
	public NoSpeechDetectedException(string message) : base(message)
	{
	}
}
