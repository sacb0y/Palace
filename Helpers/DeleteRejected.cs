using Palace.Models;

namespace Palace.Helpers;

/// <summary>Where Delete rejected counts files from.</summary>
public enum DeleteRejectedScope
{
    /// <summary>Current folder / All-rejected view / Review session folder.</summary>
    ThisView = 0,
    /// <summary>Every <c>_rejected</c> file in the project. Product default (Isiac).</summary>
    WholeLibrary = 1
}

/// <summary>Recycle Bin vs permanent erase.</summary>
public enum DeleteRejectedDestination
{
    RecycleBin = 0,
    Permanent = 1
}

/// <summary>One file that could not be deleted (in use, access denied, …).</summary>
public readonly record struct DeleteRejectedFailure(string AssetId, string Path, string FileName, string Reason);

/// <summary>
/// WinUI-free Delete rejected policy: scope filters, network-share skip on Recycle,
/// safety (never outside <c>_rejected</c>), size / status copy.
/// </summary>
public static class DeleteRejected
{
    /// <summary>Catalog backup when deleting at least this many files.</summary>
    public const int BackupThreshold = 25;

    public static bool IsRejectedPath(string? path) => ReviewReject.IsAlreadyRejected(path);

    /// <summary>
    /// Parent <c>_rejected</c> directory for a rejected asset path, or null.
    /// </summary>
    public static string? RejectedFolderOf(string? assetPath)
    {
        if (!IsRejectedPath(assetPath))
        {
            return null;
        }

        return ReviewReject.ParentDir(assetPath);
    }

    /// <summary>
    /// Keep only assets that live directly inside a <c>_rejected</c> folder.
    /// Never deletes anything outside those folders.
    /// </summary>
    public static List<Asset> OnlyRejected(IEnumerable<Asset> assets) =>
        assets.Where(a => IsRejectedPath(a.Path)).ToList();

    /// <summary>
    /// Filter rejected assets to a view prefix (folder or source root). Null/empty prefix = all.
    /// </summary>
    public static List<Asset> InScope(IEnumerable<Asset> rejected, string? viewPrefix)
    {
        var list = OnlyRejected(rejected);
        if (string.IsNullOrWhiteSpace(viewPrefix))
        {
            return list;
        }

        var prefix = viewPrefix.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        return list.Where(a => FolderNames.IsSameOrUnder(a.Path, prefix)).ToList();
    }

    public static List<Asset> PartitionNetwork(
        IReadOnlyList<Asset> assets,
        out List<Asset> network,
        Func<string, bool>? isNetwork = null)
    {
        isNetwork ??= NetworkPaths.IsNetworkShare;
        var local = new List<Asset>();
        network = new List<Asset>();
        foreach (var asset in assets)
        {
            if (isNetwork(asset.Path))
            {
                network.Add(asset);
            }
            else
            {
                local.Add(asset);
            }
        }

        return local;
    }

    /// <summary>
    /// Files that will actually be acted on for the chosen destination.
    /// Recycle skips network shares; Permanent includes them.
    /// </summary>
    public static List<Asset> Actionable(
        IReadOnlyList<Asset> scoped,
        DeleteRejectedDestination destination,
        out List<Asset> networkSkipped,
        Func<string, bool>? isNetwork = null)
    {
        if (destination == DeleteRejectedDestination.Permanent)
        {
            networkSkipped = [];
            return scoped.ToList();
        }

        return PartitionNetwork(scoped, out networkSkipped, isNetwork);
    }

    public static long TotalBytes(IEnumerable<Asset> assets) =>
        assets.Sum(a => a.FileSize ?? 0);

    public static string FormatSize(long bytes)
    {
        if (bytes < 1024)
        {
            return $"{bytes} B";
        }

        double value = bytes;
        string[] units = ["KB", "MB", "GB", "TB"];
        var unit = 0;
        value /= 1024;
        while (value >= 1024 && unit < units.Length - 1)
        {
            value /= 1024;
            unit++;
        }

        return value >= 10 || unit == 0
            ? $"{value:0} {units[unit]}"
            : $"{value:0.0} {units[unit]}";
    }

    public static int DistinctRejectedFolders(IEnumerable<Asset> assets) =>
        assets
            .Select(a => RejectedFolderOf(a.Path))
            .OfType<string>()
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Count();

    public static bool ShouldBackup(int actionableCount) => actionableCount >= BackupThreshold;

    public static string ToolbarLabel(int n) => $"Delete rejected ({n})";

    public static string ConfirmPrimaryLabel(DeleteRejectedDestination destination, int n) =>
        destination == DeleteRejectedDestination.Permanent
            ? $"Delete {n} permanently"
            : $"Move {n} to Recycle Bin";

    /// <summary>Retry after partial failure keeps the destination from that run.</summary>
    public static DeleteRejectedDestination RetryDestination(DeleteRejectedDestination last) => last;

    /// <summary>
    /// Confirm-dialog primary count/bytes for the selected scope + destination.
    /// Recycle subtracts network files in that scope; Permanent keeps the full totals.
    /// </summary>
    public static void DialogActionableTotals(
        int count,
        long bytes,
        int networkInScope,
        long networkBytesInScope,
        DeleteRejectedDestination destination,
        out int actionableCount,
        out long actionableBytes,
        out int networkShown)
    {
        networkShown = destination == DeleteRejectedDestination.RecycleBin
            ? Math.Max(0, networkInScope)
            : 0;
        if (destination == DeleteRejectedDestination.RecycleBin && networkShown > 0)
        {
            actionableCount = Math.Max(0, count - networkShown);
            actionableBytes = Math.Max(0, bytes - networkBytesInScope);
            return;
        }

        actionableCount = count;
        actionableBytes = bytes;
    }

    public static string NetworkSkipNote(int k) =>
        k <= 0
            ? ""
            : $"The {k} on the network share can’t use the Recycle Bin. They’ll be skipped and left in _rejected.";

    public static string StatusRecycleSuccess(int moved, long bytes, int networkSkipped)
    {
        var core = $"Moved {moved} rejected files ({FormatSize(bytes)}) to Recycle Bin";
        return networkSkipped > 0
            ? $"{core} · {networkSkipped} skipped (network)"
            : core;
    }

    public static string StatusPermanentSuccess(int deleted, long bytes) =>
        $"Deleted {deleted} files permanently ({FormatSize(bytes)})";

    public static string StatusRecyclePartial(int moved, int attempted, int failed) =>
        $"{moved} of {attempted} moved to Recycle Bin · {failed} in use";

    public static string StatusPermanentPartial(int deleted, int attempted, int failed) =>
        $"{deleted} of {attempted} deleted permanently · {failed} in use";

    public static string FailureReason(Exception ex) =>
        ex is IOException or UnauthorizedAccessException
            ? "In use by another app"
            : ex.Message;

    /// <summary>
    /// After deleting files from a <c>_rejected</c> folder, true when the folder
    /// exists and has no remaining entries (safe to remove).
    /// </summary>
    public static bool IsEmptyRejectedFolder(string folderPath)
    {
        if (!Directory.Exists(folderPath))
        {
            return false;
        }

        var leaf = Path.GetFileName(folderPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
        if (!string.Equals(leaf, ReviewReject.FolderName, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        try
        {
            return !Directory.EnumerateFileSystemEntries(folderPath).Any();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }
}
