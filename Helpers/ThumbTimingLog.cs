using System.Text;

namespace Palace.Helpers;

/// <summary>
/// Append-only thumbnail timing log (no WinUI). Packaged builds write
/// <c>thumb-timing.txt</c> beside <c>error-log.txt</c> in LocalState.
/// </summary>
public static class ThumbTimingLog
{
    public const string FileName = "thumb-timing.txt";
    public const long DefaultMaxBytes = 512 * 1024;

    private static readonly object Gate = new();
    private static string? _path;

    public static void Configure(string? path) => _path = string.IsNullOrWhiteSpace(path) ? null : path;

    public static string? Path => _path;

    public static string LibraryShowLine(bool pageReused, bool previewKept, int queue, int cap, int cacheCount, int threadId) =>
        $"library-show page={(pageReused ? "reused" : "new")} preview={(previewKept ? "kept" : "assign")} queue={queue} cap={cap} cache={cacheCount} thread={threadId} role=ui";

    public static string PreviewLine(bool capped, int decodeWidth, int decodeHeight, double scale, string source, int sourceLongEdge, int threadId) =>
        $"preview capped={(capped ? "true" : "false")} size={decodeWidth}x{decodeHeight} scale={scale:0.00} source={source} longEdge={sourceLongEdge} thread={threadId} role=ui";

    public static string PlanLine(string reason, int queue, int cap, int start, int cancel, int cacheHits, int threadId) =>
        $"plan reason={reason} queue={queue} cap={cap} start={start} cancel={cancel} cacheHit={cacheHits} thread={threadId} role=ui";

    public static string TileLine(string cache, string disk, double milliseconds, int decodeWidth, int decodeHeight, int queue, int cap, int startThread, int ensureThread, int assignThread) =>
        $"tile cache={cache} disk={disk} ms={milliseconds:0} size={decodeWidth}x{decodeHeight} capped=true queue={queue} cap={cap} startThread={startThread} ensureThread={ensureThread} assignThread={assignThread} role=ui";

    /// <summary>Never throws.</summary>
    public static bool Write(string line)
    {
        var path = _path;
        if (string.IsNullOrEmpty(path) || string.IsNullOrEmpty(line))
        {
            return false;
        }

        try
        {
            var entry = $"[{DateTimeOffset.UtcNow:yyyy-MM-ddTHH:mm:ss.fffZ}] {line}{Environment.NewLine}";
            lock (Gate)
            {
                var dir = System.IO.Path.GetDirectoryName(path);
                if (!string.IsNullOrEmpty(dir))
                {
                    Directory.CreateDirectory(dir);
                }

                var info = new FileInfo(path);
                if (info.Exists && info.Length >= DefaultMaxBytes)
                {
                    File.Move(path, path + ".1", overwrite: true);
                }

                File.AppendAllText(path, entry, Encoding.UTF8);
            }

            return true;
        }
        catch
        {
            return false;
        }
    }
}
