using System;

namespace CognitiveSupport;

/// <summary>
/// One silence period that <see cref="SilenceTrimmer"/> removed.
///
/// <paramref name="Position"/> is where the cut landed on the <em>processed</em>
/// timeline — the audio that is actually written out — not on the source timeline.
/// That is what makes the value still usable after the fact: every earlier removal
/// has already shortened the output, so a later point's position already accounts
/// for the shift, and the value can be handed straight to a seek or a split.
///
/// <paramref name="RemovedDuration"/> is how much audio was dropped there, measured
/// on the original timeline. It is the "how big was the pause" signal used to pick
/// the most natural place to cut a long recording into chunks.
/// </summary>
public readonly record struct SilenceRemovalPoint(TimeSpan Position, TimeSpan RemovedDuration);
