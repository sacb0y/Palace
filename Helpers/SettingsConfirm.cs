namespace Palace.Helpers;

/// <summary>
/// Copy for Settings destructive-action confirmations. WinUI-free so Palace.Tests can lock the wording.
/// Dialogs use Cancel/Close as the safe default; Primary is the destructive action.
/// </summary>
public static class SettingsConfirm
{
    public const string RemoveFolderTitle = "Remove folder";
    public const string RemoveFolderPrimary = "Remove";

    public const string ClearCacheTitle = "Clear cache";
    public const string ClearCachePrimary = "Clear";

    public const string ResetCatalogTitle = "Reset catalog folder";
    public const string ResetCatalogPrimary = "Reset";

    public const string DisconnectTitlePrefix = "Disconnect ";
    public const string DisconnectPrimary = "Disconnect";

    /// <summary>
    /// Body for removing a watched folder. Always states that files on disk are not touched,
    /// and includes the affected catalog asset count.
    /// </summary>
    public static string RemoveFolderBody(string path, int assetCount)
    {
        var folder = string.IsNullOrWhiteSpace(path) ? "this folder" : path.Trim();
        var assets = assetCount == 1
            ? "1 catalog asset"
            : $"{assetCount:N0} catalog assets";
        return $"Remove “{folder}” from this project? Palace will drop {assets} (and their tag links) from the catalog. Files on disk are not touched.";
    }

    public static string ClearCacheBody() =>
        "Clear the media cache? Thumbnail and preview files under the cache folder are deleted. Your originals and catalog stay.";

    public static string ResetCatalogBody() =>
        "Reset the catalog folder to the durable default? Palace will point at the default location and ask you to restart. Your current catalog is not deleted.";

    public static string DisconnectTitle(string providerDisplayName) =>
        DisconnectTitlePrefix + providerDisplayName;

    public static string DisconnectBody(string providerDisplayName) =>
        $"Disconnect {providerDisplayName}? Palace forgets this account’s sign-in. Watched cloud folders stay in the catalog until you remove them.";
}
