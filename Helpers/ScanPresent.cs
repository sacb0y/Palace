namespace Palace.Helpers;

/// <summary>
/// Present-on-disk vs indexed (D3). Format toggles filter what gets indexed,
/// not what counts as present for the orphan pass.
/// </summary>
public static class ScanPresent
{
    /// <summary>
    /// Catalog extensions stay in the scan <c>found</c> set even when the
    /// matching index toggle is off, so .glb / .mp4 rows are not orphaned.
    /// </summary>
    public static bool RetainWhenNotIndexed(string? pathOrExt) =>
        PathSafe.IsCatalogExt(PathSafe.Extension(pathOrExt));
}
