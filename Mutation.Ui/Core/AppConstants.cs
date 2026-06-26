namespace Mutation.Ui.Core;

/// <summary>
/// Centralized constants for the application to avoid magic numbers scattered throughout the codebase.
/// </summary>
public static class AppConstants
{
    // Speech session retention is now user-configurable; see
    // SpeechToTextSettings.MaxRetainedSessions (default 10, range 1–500).

    // ============================================================
    // Rate Limiting
    // ============================================================

    /// <summary>
    /// Default maximum API calls allowed within the rate limit window.
    /// </summary>
    public const int DefaultApiRateLimitCalls = 20;

    /// <summary>
    /// Default rate limit window duration in seconds.
    /// </summary>
    public const int DefaultApiRateLimitWindowSeconds = 60;

    // ============================================================
    // Hotkey Timing
    // ============================================================

    /// <summary>
    /// Delay in milliseconds between hotkey chord sequences to allow the system to process each chord.
    /// </summary>
    public const int HotkeyChordDelayMs = 25;

    /// <summary>
    /// Delay in milliseconds after releasing modifier keys before sending the target hotkey.
    /// </summary>
    public const int ModifierReleaseDelayMs = 10;

    /// <summary>
    /// Maximum time in milliseconds to wait for user to release modifier keys.
    /// </summary>
    public const int ModifierReleaseTimeoutMs = 200;
}
