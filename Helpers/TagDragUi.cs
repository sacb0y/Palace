using System.Globalization;

namespace Palace.Helpers;

/// <summary>
/// WinUI-free rules for Tags board drag chrome (Gooey dg-01…03).
/// Ghost bitmaps must be applied in <c>DragStarting</c> with no await/deferral —
/// post-await <c>SetContentFromBitmapImage</c> is dropped (WinUI #10144 / #4528).
/// <see cref="CapturePointer"/> on the chip while <c>CanDrag</c> kills WinUI DnD
/// (<c>DragStarting</c> never fires) — press chrome must skip capture when armed.
/// </summary>
public static class TagDragUi
{
    /// <summary>dg-01a/b: source chip opacity while the ghost follows the pointer.</summary>
    public const double DragSourceOpacity = 0.4;

    /// <summary>
    /// DragUI content must be set synchronously in DragStarting (no GetDeferral await
    /// before SetContentFromBitmapImage).
    /// </summary>
    public static bool RequiresSynchronousDragUiContent => true;

    /// <summary>
    /// TagChip press chrome may CapturePointer only when CanDrag is false.
    /// Capture while CanDrag steals the gesture from WinUI drag (Wimbly / Isiac).
    /// </summary>
    public static bool ShouldCapturePointerForPressChrome(bool canDrag) => !canDrag;

    /// <summary>
    /// Mirror Library <c>AssetDrag.PreArmsCanDragOnMouseHover</c>: WinUI samples
    /// CanDrag at pointer-down, so Tags must pre-arm on mouse hover / move.
    /// Touch / pen leave CanDrag false so the chip ScrollViewer still pans.
    /// </summary>
    public static bool PreArmsCanDragOnMouseHover(bool isMouse) => isMouse;

    public static string GhostCacheKey(string? tagId, int payloadCount) =>
        $"{tagId ?? ""}\n{Math.Max(0, payloadCount)}";

    public static bool TryGetCachedGhostKey(string? cacheKey, string? tagId, int payloadCount) =>
        !string.IsNullOrEmpty(cacheKey)
        && string.Equals(cacheKey, GhostCacheKey(tagId, payloadCount), StringComparison.Ordinal);

    /// <summary>
    /// Child hit-testing inside a group row fires DragLeave without leaving the row.
    /// Clear drop chrome only when the pointer is outside the row bounds.
    /// </summary>
    public static bool ShouldClearDropChromeOnLeave(
        double x,
        double y,
        double width,
        double height)
    {
        if (!(width > 0) || !(height > 0)
            || !double.IsFinite(x) || !double.IsFinite(y)
            || !double.IsFinite(width) || !double.IsFinite(height))
        {
            return true;
        }

        return x < 0 || y < 0 || x > width || y > height;
    }

    public static bool ShowsValidDropChrome(bool isUserGroupRow, bool allowDrop) =>
        isUserGroupRow && allowDrop;

    public static bool ShowsRefuseDropChrome(bool isUserGroupRow, bool allowDrop) =>
        isUserGroupRow && !allowDrop;

    // --- Harness probe (Fenrir ASSERT DragStarting before dg frames) ---
    // Gated: DEBUG builds on by default; Release needs PALACE_TAG_DRAG_LOG=1 (or tests).

    public const string ProbeEnvVar = "PALACE_TAG_DRAG_LOG";

    /// <summary>Test override: null = DEBUG / env; true/false force.</summary>
    public static bool? ProbeEnabledOverride { get; set; }

    private static readonly object ProbeGate = new();
    private static int _dragStartingCount;
    private static int _dragOverCount;
    private static int _dropCount;
    private static Action<string>? _logSink;

    /// <summary>
    /// True when probe counters / LogSink / Log* are live.
    /// DEBUG: on unless override/env forces off. Release: env <c>PALACE_TAG_DRAG_LOG=1</c>
    /// (or <see cref="ProbeEnabledOverride"/>).
    /// </summary>
    public static bool IsProbeEnabled
    {
        get
        {
            if (ProbeEnabledOverride is { } forced)
            {
                return forced;
            }

            var env = Environment.GetEnvironmentVariable(ProbeEnvVar);
            if (env is "1" or "true" or "TRUE" or "yes" or "YES")
            {
                return true;
            }

            if (env is "0" or "false" or "FALSE" or "no" or "NO")
            {
                return false;
            }

#if DEBUG
            return true;
#else
            return false;
#endif
        }
    }

    /// <summary>Optional sink (tests / harness). Ignored when probe is off.</summary>
    public static Action<string>? LogSink
    {
        get => IsProbeEnabled ? _logSink : null;
        set => _logSink = value;
    }

    public static int DragStartingCount
    {
        get { lock (ProbeGate) { return IsProbeEnabled ? _dragStartingCount : 0; } }
    }

    public static int DragOverCount
    {
        get { lock (ProbeGate) { return IsProbeEnabled ? _dragOverCount : 0; } }
    }

    public static int DropCount
    {
        get { lock (ProbeGate) { return IsProbeEnabled ? _dropCount : 0; } }
    }

    public static void ResetProbe()
    {
        lock (ProbeGate)
        {
            _dragStartingCount = 0;
            _dragOverCount = 0;
            _dropCount = 0;
        }
    }

    public static void LogDragStarting(string? tagId, int payloadCount)
    {
        if (!IsProbeEnabled)
        {
            return;
        }

        int n;
        lock (ProbeGate)
        {
            n = ++_dragStartingCount;
        }

        WriteProbe(string.Create(
            CultureInfo.InvariantCulture,
            $"Palace.TagDrag DragStarting n={n} tag={tagId ?? ""} count={payloadCount}"));
    }

    public static void LogDragOver(string? groupId, string? groupName, bool allow, string? caption)
    {
        if (!IsProbeEnabled)
        {
            return;
        }

        int n;
        lock (ProbeGate)
        {
            n = ++_dragOverCount;
        }

        WriteProbe(string.Create(
            CultureInfo.InvariantCulture,
            $"Palace.TagDrag DragOver n={n} group={groupId ?? ""} name={groupName ?? ""} allow={allow} caption={caption ?? ""}"));
    }

    public static void LogDrop(string? groupId, string? groupName, int added)
    {
        if (!IsProbeEnabled)
        {
            return;
        }

        int n;
        lock (ProbeGate)
        {
            n = ++_dropCount;
        }

        WriteProbe(string.Create(
            CultureInfo.InvariantCulture,
            $"Palace.TagDrag Drop n={n} group={groupId ?? ""} name={groupName ?? ""} added={added}"));
    }

    private static void WriteProbe(string line)
    {
        try
        {
            if (_logSink is not null)
            {
                _logSink(line);
            }
            else
            {
                System.Diagnostics.Debug.WriteLine(line);
            }
        }
        catch
        {
            // Probe must never break drag.
        }
    }
}
