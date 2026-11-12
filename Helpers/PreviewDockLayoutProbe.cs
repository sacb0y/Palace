using System.Globalization;
using System.Text;
using Windows.Storage;

namespace Palace.Helpers;

/// <summary>
/// Temporary height-only dock layout probe (Wimbly). Enable with LocalSettings
/// <c>PreviewDockLayoutDebug</c> = true (bool) or env <c>PALACE_PREVIEW_DOCK_DEBUG=1</c>.
/// Appends lines to <c>%LOCALAPPDATA%\Palace\preview-dock-layout.log</c>.
/// Remove once Studio height-only PASS is locked.
/// </summary>
public static class PreviewDockLayoutProbe
{
    public const string LocalSettingsKey = "PreviewDockLayoutDebug";
    public const string EnvVarName = "PALACE_PREVIEW_DOCK_DEBUG";

    private static readonly object Gate = new();
    private static bool? _enabled;
    private static string? _logPath;

    public static bool IsEnabled
    {
        get
        {
            if (_enabled is not null)
            {
                return _enabled.Value;
            }

            try
            {
                var env = Environment.GetEnvironmentVariable(EnvVarName);
                if (string.Equals(env, "1", StringComparison.Ordinal)
                    || string.Equals(env, "true", StringComparison.OrdinalIgnoreCase))
                {
                    _enabled = true;
                    return true;
                }

                if (ApplicationData.Current.LocalSettings.Values[LocalSettingsKey] is bool flag)
                {
                    _enabled = flag;
                    return flag;
                }
            }
            catch
            {
                // Settings / env unavailable (tests).
            }

            _enabled = false;
            return false;
        }
    }

    /// <summary>Force enable/disable for a session (tests / Studio harness).</summary>
    public static void SetEnabled(bool enabled) => _enabled = enabled;

    public static void ResetEnabledCache() => _enabled = null;

    public static void Log(string source, string message)
    {
        if (!IsEnabled)
        {
            return;
        }

        try
        {
            var line = string.Create(
                CultureInfo.InvariantCulture,
                $"{DateTimeOffset.Now:o} [{source}] {message}");
            lock (Gate)
            {
                var path = EnsureLogPath();
                File.AppendAllText(path, line + Environment.NewLine);
            }
        }
        catch
        {
            // Probe must never break layout.
        }
    }

    public static string FormatSlot(PreviewDockHostSlot slot) =>
        string.Create(
            CultureInfo.InvariantCulture,
            $"col={slot.Column} span={slot.ColumnSpan} row={slot.Row} alignRight={slot.AlignRight} marginL={slot.MarginLeft:0.###} w={slot.Width:0.###} op={slot.Opacity:0.###} hit={slot.HitTestVisible}");

    public static string FormatLayout(PreviewPaneLayoutState layout) =>
        string.Create(
            CultureInfo.InvariantCulture,
            $"mode={layout.Mode} tree={layout.Tree:0.###} mosaic={layout.Mosaic:0.###} preview={layout.Preview:0.###} expectX={DockHostExpectedOffsetX(layout):0.###}");

    private static double DockHostExpectedOffsetX(PreviewPaneLayoutState layout) =>
        PreviewPane.DockHostExpectedOffsetX(layout);

    private static string EnsureLogPath()
    {
        if (_logPath is not null)
        {
            return _logPath;
        }

        var dir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Palace");
        Directory.CreateDirectory(dir);
        _logPath = Path.Combine(dir, "preview-dock-layout.log");
        return _logPath;
    }

    public static string BuildSnapshot(
        double windowW,
        double windowH,
        PreviewPaneLayoutState layout,
        PreviewDockHostSlot slot,
        double colTree,
        double colMosaic,
        double colPreview,
        double hostOffsetX,
        double hostOffsetY,
        double hostActualW,
        double hostActualH,
        double hostWidthProp,
        int hostColumn,
        int hostColumnSpan,
        string hostAlign,
        string hostMargin,
        double paneOffsetX,
        double paneActualW,
        double visualOffsetX,
        double visualOffsetY,
        string? visualStates)
    {
        var sb = new StringBuilder(512);
        sb.Append(CultureInfo.InvariantCulture, $"win={windowW:0.#}x{windowH:0.#} ");
        sb.Append(FormatLayout(layout));
        sb.Append(' ');
        sb.Append(FormatSlot(slot));
        sb.Append(CultureInfo.InvariantCulture, $" cols=[{colTree:0.#},{colMosaic:0.#},{colPreview:0.#}]");
        sb.Append(CultureInfo.InvariantCulture, $" host: col={hostColumn}/{hostColumnSpan} align={hostAlign} margin={hostMargin}");
        sb.Append(CultureInfo.InvariantCulture, $" actual={hostActualW:0.#}x{hostActualH:0.#} widthProp={hostWidthProp:0.#}");
        sb.Append(CultureInfo.InvariantCulture, $" offset=({hostOffsetX:0.#},{hostOffsetY:0.#})");
        sb.Append(CultureInfo.InvariantCulture, $" pane: offsetX={paneOffsetX:0.#} w={paneActualW:0.#}");
        sb.Append(CultureInfo.InvariantCulture, $" visualOffset=({visualOffsetX:0.#},{visualOffsetY:0.#})");
        if (!string.IsNullOrEmpty(visualStates))
        {
            sb.Append(" vsm=[").Append(visualStates).Append(']');
        }

        return sb.ToString();
    }
}
