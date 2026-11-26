using System.Globalization;
using System.Text;

namespace Palace.Helpers;

/// <summary>
/// Appends real frame-time lines for the Diligent presenter spike.
/// Default path: under the Palace data dir (<c>smooth-presenter-frames.log</c>).
/// Override with <c>PALACE_SMOOTH_FRAME_LOG</c> (file path).
/// </summary>
public static class SmoothPresenterLog
{
    public const string EnvVar = "PALACE_SMOOTH_FRAME_LOG";
    public const string DefaultFileName = "smooth-presenter-frames.log";

    private static readonly object Gate = new();
    private static long _pendingInputQpc;
    private static float _pendingTargetZoom = float.NaN;
    private static int _pendingInputArmed;

    /// <summary>
    /// Present-path input latency: call from PointerWheelChanged when
    /// <see cref="EnvVar"/> is set. Completed on the first Present whose
    /// transform zoom reflects the target (see <see cref="TryCompleteInputLatency"/>).
    /// </summary>
    public static void MarkWheelInput(float targetZoom)
    {
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(EnvVar)))
        {
            return;
        }

        _pendingInputQpc = System.Diagnostics.Stopwatch.GetTimestamp();
        _pendingTargetZoom = targetZoom;
        Volatile.Write(ref _pendingInputArmed, 1);
    }

    /// <summary>
    /// First Present after <see cref="MarkWheelInput"/> — records present-path
    /// latency. Ease may still be catching the target; log both z and target.
    /// </summary>
    public static void TryCompleteInputLatency(string? dataDir, float presentedZoom)
    {
        if (Interlocked.Exchange(ref _pendingInputArmed, 0) == 0)
        {
            return;
        }

        var now = System.Diagnostics.Stopwatch.GetTimestamp();
        var ms = (now - _pendingInputQpc) * 1000.0 / System.Diagnostics.Stopwatch.Frequency;
        var target = _pendingTargetZoom;
        AppendNote(
            dataDir,
            $"input_lat_ms={ms.ToString("F3", CultureInfo.InvariantCulture)} "
            + $"z={presentedZoom.ToString("F4", CultureInfo.InvariantCulture)} "
            + $"target={target.ToString("F4", CultureInfo.InvariantCulture)} path=present");
    }

    public static string ResolvePath(string? dataDir)
    {
        var env = Environment.GetEnvironmentVariable(EnvVar);
        if (!string.IsNullOrWhiteSpace(env))
        {
            return env.Trim();
        }

        var root = string.IsNullOrWhiteSpace(dataDir)
            ? Path.GetTempPath()
            : dataDir;
        return Path.Combine(root, DefaultFileName);
    }

    public static string FormatLine(
        SmoothPresenterNative.FrameStats stats,
        string? note = null)
    {
        var backend = stats.Backend switch
        {
            (int)SmoothPresenterNative.Backend.D3D12 => "d3d12",
            (int)SmoothPresenterNative.Backend.D3D11 => "d3d11",
            _ => "auto"
        };
        var sb = new StringBuilder(160);
        sb.Append(DateTimeOffset.UtcNow.ToString("o", CultureInfo.InvariantCulture));
        sb.Append(" frame=").Append(stats.FrameIndex.ToString(CultureInfo.InvariantCulture));
        sb.Append(" ").Append(backend);
        sb.Append(' ').Append(stats.Width.ToString(CultureInfo.InvariantCulture))
            .Append('x').Append(stats.Height.ToString(CultureInfo.InvariantCulture));
        sb.Append(" wait_ms=").Append(stats.WaitMs.ToString("F3", CultureInfo.InvariantCulture));
        sb.Append(" render_ms=").Append(stats.RenderMs.ToString("F3", CultureInfo.InvariantCulture));
        sb.Append(" present_ms=").Append(stats.PresentMs.ToString("F3", CultureInfo.InvariantCulture));
        sb.Append(" total_ms=").Append(stats.TotalMs.ToString("F3", CultureInfo.InvariantCulture));
        if (!string.IsNullOrWhiteSpace(note))
        {
            sb.Append(" note=").Append(note.Replace('\n', ' ').Replace('\r', ' '));
        }

        return sb.ToString();
    }

    public static void Append(string path, string line)
    {
        if (string.IsNullOrWhiteSpace(path) || string.IsNullOrWhiteSpace(line))
        {
            return;
        }

        lock (Gate)
        {
            var dir = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(dir))
            {
                Directory.CreateDirectory(dir);
            }

            File.AppendAllText(path, line + Environment.NewLine, Encoding.UTF8);
        }
    }

    public static void AppendStats(string? dataDir, SmoothPresenterNative.FrameStats stats, string? note = null)
    {
        Append(ResolvePath(dataDir), FormatLine(stats, note));
    }

    public static void AppendNote(string? dataDir, string note)
    {
        if (string.IsNullOrWhiteSpace(note))
        {
            return;
        }

        var line = DateTimeOffset.UtcNow.ToString("o", CultureInfo.InvariantCulture)
                   + " note=" + note.Replace('\n', ' ').Replace('\r', ' ');
        Append(ResolvePath(dataDir), line);
    }
}
