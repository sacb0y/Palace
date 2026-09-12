using System.IO;

namespace Palace.Helpers;

/// <summary>
/// Local + Files On-Demand scan policy. Detect with
/// <see cref="CloudFile.IsOnlineOnly(FileAttributes)"/> only — never open a
/// stream to test. Mosaic display does not read originals; scan must match that.
/// </summary>
public static class ScanContent
{
    /// <summary>
    /// Hash, metadata Extract, BitmapDecoder, and
    /// <c>ImageDimensions.TryRead</c> on the original are allowed only when
    /// the file is fully local.
    /// </summary>
    public static bool MayReadOriginal(FileAttributes attributes) =>
        !CloudFile.IsOnlineOnly(attributes);

    public static bool MayReadOriginal(string? path) =>
        CloudFile.TryGetAttributes(path, out var attributes) && MayReadOriginal(attributes);

    /// <summary>
    /// Scan must not batch <c>GetThumbnailAsync</c> / decode for placeholders.
    /// Leave no JPEG; the mosaic may later request a provider thumb for
    /// realized tiles only.
    /// </summary>
    public static bool MayGenerateScanThumbnail(FileAttributes attributes) =>
        MayReadOriginal(attributes);

    public static bool MayGenerateScanThumbnail(string? path) =>
        MayReadOriginal(path);
}
