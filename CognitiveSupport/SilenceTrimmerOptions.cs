namespace CognitiveSupport;

/// <summary>
/// Configuration for <see cref="SilenceTrimmer"/>. All durations are wall-clock,
/// independent of frame size.
/// </summary>
/// <param name="SilenceThresholdDbFs">
/// RMS loudness floor in dBFS. A 20ms frame at or below this level counts as silence.
/// Lower (more negative) = stricter (only quieter audio is treated as silence).
/// </param>
/// <param name="MinSilenceSeconds">
/// Silence gaps between speech longer than this are trimmed down to this length.
/// Gaps shorter than this are left untouched. 0 removes all inter-speech silence
/// (subject to the guard below).
/// </param>
/// <param name="GuardMilliseconds">
/// Audio kept on each side of a speech segment (hang-time after, pre-roll before)
/// so soft consonants, breaths, and word decay are not clipped. Acts as a floor
/// that is preserved even when <paramref name="MinSilenceSeconds"/> is very small.
/// </param>
public sealed record SilenceTrimmerOptions(
	double SilenceThresholdDbFs,
	double MinSilenceSeconds,
	double GuardMilliseconds);
