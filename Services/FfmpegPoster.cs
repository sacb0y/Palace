using Palace.Helpers;
using System.Diagnostics;

namespace Palace.Services;

/// <summary>
/// Video poster frames without system codecs: runs a bundled LGPL ffmpeg
/// (<c>ffmpeg\ffmpeg.exe</c> next to Palace.exe, fetched by
/// <c>tools/Get-Ffmpeg.ps1</c>) when the shell/Media Foundation can't make a
/// thumbnail (e.g. MKV with H.264 4:4:4, WebM VP9/AV1 without the Store
/// extensions). Never runs for online-only files; output only goes to the
/// thumb cache.
/// </summary>
internal static class FfmpegPoster
{
    private static readonly SemaphoreSlim Gate = new(2);

    public static string? ExecutablePath
    {
        get
        {
            var path = Path.Combine(AppContext.BaseDirectory, "ffmpeg", "ffmpeg.exe");
            return File.Exists(path) ? path : null;
        }
    }

    public static bool IsAvailable => ExecutablePath is not null;

    /// <summary>
    /// Arguments for one scaled JPEG frame. Seeks a little in (skip black
    /// intros) and falls back to the first frame for short clips.
    /// </summary>
    public static IReadOnlyList<string> BuildArguments(string input, string outputJpeg, int maxSide, double seekSeconds) =>
    [
        "-hide_banner", "-loglevel", "error", "-nostdin", "-y",
        "-ss", seekSeconds.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture),
        "-i", input,
        "-map", "0:v:0", "-an", "-sn", "-dn",
        "-frames:v", "1",
        "-vf", $"scale='min({maxSide},iw)':'min({maxSide},ih)':force_original_aspect_ratio=decrease:flags=bicubic,format=yuvj420p",
        "-q:v", "3",
        "-f", "image2", "-c:v", "mjpeg",
        outputJpeg
    ];

    public static async Task<bool> TryExtractAsync(string input, string outputJpeg, int maxSide, CancellationToken ct = default)
    {
        var exe = ExecutablePath;
        if (exe is null || CloudFile.IsOnlineOnly(input))
        {
            return false;
        }

        await Gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            foreach (var seek in new[] { 3.0, 0.0 })
            {
                TryDelete(outputJpeg);
                if (await RunAsync(exe, BuildArguments(input, outputJpeg, maxSide, seek), ct).ConfigureAwait(false)
                    && File.Exists(outputJpeg) && new FileInfo(outputJpeg).Length > 0)
                {
                    return true;
                }
            }

            return false;
        }
        finally
        {
            Gate.Release();
        }
    }

    private static async Task<bool> RunAsync(string exe, IReadOnlyList<string> args, CancellationToken ct)
    {
        var psi = new ProcessStartInfo(exe)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardError = true,
            RedirectStandardOutput = true,
            WorkingDirectory = Path.GetDirectoryName(exe)!
        };
        foreach (var a in args)
        {
            psi.ArgumentList.Add(a);
        }

        try
        {
            using var proc = Process.Start(psi);
            if (proc is null)
            {
                return false;
            }

            _ = proc.StandardOutput.ReadToEndAsync(ct);
            _ = proc.StandardError.ReadToEndAsync(ct);
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeout.CancelAfter(TimeSpan.FromSeconds(30));
            try
            {
                await proc.WaitForExitAsync(timeout.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                try { proc.Kill(entireProcessTree: true); } catch { }
                return false;
            }

            return proc.ExitCode == 0;
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            return false;
        }
    }

    private static void TryDelete(string path)
    {
        try { File.Delete(path); } catch { }
    }
}
