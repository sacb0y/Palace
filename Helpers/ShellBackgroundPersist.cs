namespace Palace.Helpers;

/// <summary>
/// Debounce gate for LocalSettings writes while blur/darkness sliders drag.
/// WinUI-free for Palace.Tests.
/// </summary>
public static class ShellBackgroundPersist
{
    /// <summary>Gooey: persist slider amounts ~300ms after the last change.</summary>
    public const int DebounceMs = 300;

    /// <summary>
    /// True when <paramref name="elapsedMs"/> since the last dirty mark is long
    /// enough to flush LocalSettings (and Recent per-entry amounts).
    /// </summary>
    public static bool ShouldFlush(int elapsedMs) =>
        elapsedMs >= DebounceMs;

    /// <summary>
    /// Restart the debounce window. Returns the wait length for the next flush.
    /// </summary>
    public static int RestartWaitMs() => DebounceMs;
}
