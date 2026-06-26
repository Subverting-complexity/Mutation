namespace CognitiveSupport;

/// <summary>
/// The fixed set of playback-speed multipliers offered for the recorded-audio
/// player, plus helpers to pick a valid value. A multiplier is a literal speed:
/// 1.0 is normal speed, 2.0 is twice as fast, 0.5 is half speed. Pitch is
/// preserved at every speed by the time-stretch stage in <see cref="AudioPlayer"/>.
/// </summary>
public static class PlaybackSpeedOptions
{
    /// <summary>Normal speed, used when no speed has been chosen.</summary>
    public const double Default = 1.0;

    /// <summary>
    /// The exact multipliers the UI lists, from slowest to fastest. These are the
    /// only values playback speed is ever set to.
    /// </summary>
    public static IReadOnlyList<double> Speeds { get; } = new[]
    {
        0.5, 0.75, 1.0, 1.25, 1.5, 1.75, 2.0, 2.25, 2.5, 2.75, 3.0,
    };

    /// <summary>
    /// Snaps an arbitrary speed (e.g. a hand-edited settings value) to the nearest
    /// allowed multiplier. Values outside the supported range collapse to the
    /// closest end (below 0.5 → 0.5, above 3.0 → 3.0), so callers always get a
    /// value that exists in <see cref="Speeds"/>.
    /// </summary>
    public static double Normalize(double speed)
    {
        double nearest = Default;
        double smallestDifference = double.MaxValue;
        foreach (double candidate in Speeds)
        {
            double difference = Math.Abs(candidate - speed);
            if (difference < smallestDifference)
            {
                smallestDifference = difference;
                nearest = candidate;
            }
        }

        return nearest;
    }
}
