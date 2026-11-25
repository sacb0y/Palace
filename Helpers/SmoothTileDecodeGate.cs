namespace Palace.Helpers;

/// <summary>
/// V1 decode policy: never open online-only cloud files; EXIF orientation is
/// a view transform and must not be baked into tiles.
/// </summary>
public static class SmoothTileDecodeGate
{
    public static bool MayDecodePath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return false;
        }

        if (!CloudFile.Exists(path))
        {
            return false;
        }

        if (CloudFile.IsOnlineOnly(path))
        {
            return false;
        }

        return true;
    }

    /// <summary>Always false for V1 — orientation stays outside the tile bytes.</summary>
    public static bool BakeExifRotationIntoTiles => false;
}
