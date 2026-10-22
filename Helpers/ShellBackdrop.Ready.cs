namespace Palace.Helpers;

/// <summary>
/// Pure readiness for glass Composition slots (WinUI-free; linked by Palace.Tests).
/// </summary>
public static partial class ShellBackdrop
{
    /// <summary>
    /// Whether <c>EnsureGlassVisual</c> can keep the existing Composition graph.
    /// Bound path + theme are the source of truth — after <c>EnsureSharedSurface</c>,
    /// matching only the shared surface path would treat a stale slot as ready
    /// (Petal→Aurora no-op). Shared-path equality is redundant once the surface loaded
    /// (<see cref="IsSlotReady"/> omits it; callers may Debug.Assert it separately).
    /// </summary>
    public static bool IsSlotReady(
        bool hasVisual,
        bool hostMatches,
        bool compositorMatches,
        bool hasEffectBrush,
        bool wantSurface,
        bool hasSurfaceSourceBrush,
        bool hasBackdropSourceBrush,
        string? boundSourcePath,
        string? imagePath,
        bool boundSourceIsLight,
        bool light)
    {
        if (!hasVisual || !hostMatches || !compositorMatches || !hasEffectBrush)
        {
            return false;
        }

        if (wantSurface)
        {
            return hasSurfaceSourceBrush
                && string.Equals(boundSourcePath, imagePath, StringComparison.OrdinalIgnoreCase)
                && boundSourceIsLight == light;
        }

        return hasBackdropSourceBrush;
    }
}
