namespace Palace.Helpers;

public enum ThumbReplaceResult
{
    Replaced,
    KeptExisting,
    Failed
}

/// <summary>
/// Swap a finished thumbnail into place. A decode that still has the destination
/// open must not throw or fail the queue; the existing file stays.
/// </summary>
public static class ThumbFileReplace
{
    public static ThumbReplaceResult TryMoveOverwrite(string source, string destination) =>
        TryMoveOverwrite(source, destination, static (from, to) => File.Move(from, to, overwrite: true));

    public static ThumbReplaceResult TryMoveOverwrite(string source, string destination, Action<string, string> move)
    {
        try
        {
            move(source, destination);
            return ThumbReplaceResult.Replaced;
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or IOException)
        {
            TryDelete(source);
            return File.Exists(destination) ? ThumbReplaceResult.KeptExisting : ThumbReplaceResult.Failed;
        }
    }

    public static void TryDelete(string? path)
    {
        if (string.IsNullOrEmpty(path))
        {
            return;
        }

        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // The temp file is locked too. Leave it.
        }
    }
}
