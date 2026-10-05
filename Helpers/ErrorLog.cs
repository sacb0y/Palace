using System.Text;

namespace Palace.Helpers;

/// <summary>
/// Append-only diagnostics file (no WinUI). One size-capped generation is kept as <c>.1</c>.
/// </summary>
internal sealed class ErrorLog
{
    public const string FileName = "error-log.txt";
    public const long DefaultMaxBytes = 512 * 1024;

    private readonly object _gate = new();
    private readonly long _maxBytes;
    private readonly Func<DateTimeOffset> _clock;

    public ErrorLog(string path, long maxBytes = DefaultMaxBytes, Func<DateTimeOffset>? clock = null)
    {
        Path = path;
        _maxBytes = maxBytes;
        _clock = clock ?? (() => DateTimeOffset.UtcNow);
    }

    public string Path { get; }

    public string PreviousPath => Path + ".1";

    /// <summary>Never throws. Returns false when the file could not be written.</summary>
    public bool TryAppend(string source, Exception exception)
    {
        try
        {
            var entry = Format(_clock(), source, exception);
            lock (_gate)
            {
                var dir = System.IO.Path.GetDirectoryName(Path);
                if (!string.IsNullOrEmpty(dir))
                {
                    Directory.CreateDirectory(dir);
                }

                Rotate();
                File.AppendAllText(Path, entry, Encoding.UTF8);
            }

            return true;
        }
        catch
        {
            return false;
        }
    }

    public static string Format(DateTimeOffset when, string source, Exception exception) =>
        $"[{when.ToUniversalTime():yyyy-MM-ddTHH:mm:ss.fffZ}] {source}{Environment.NewLine}"
        + exception + Environment.NewLine + Environment.NewLine;

    private void Rotate()
    {
        var info = new FileInfo(Path);
        if (!info.Exists || info.Length < _maxBytes)
        {
            return;
        }

        File.Move(Path, PreviousPath, overwrite: true);
    }
}
