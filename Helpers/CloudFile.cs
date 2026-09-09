using System.IO;

namespace Palace.Helpers;

/// <summary>
/// Detects Windows Files On-Demand / cloud placeholders without opening a stream.
/// Uses <c>GetFileAttributes</c> flags only — never reads file bytes.
/// </summary>
public static class CloudFile
{
    /// <summary>FILE_ATTRIBUTE_RECALL_ON_DATA_ACCESS — OneDrive online-only placeholders.</summary>
    public const FileAttributes RecallOnDataAccess = (FileAttributes)0x00400000;

    /// <summary>FILE_ATTRIBUTE_RECALL_ON_OPEN</summary>
    public const FileAttributes RecallOnOpen = (FileAttributes)0x00040000;

    /// <summary>FILE_ATTRIBUTE_OFFLINE</summary>
    public const FileAttributes Offline = FileAttributes.Offline;

    public const FileAttributes OnlineOnlyMask = RecallOnDataAccess | RecallOnOpen | Offline;

    public static bool IsOnlineOnly(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return false;
        }

        try
        {
            if (!File.Exists(path))
            {
                return false;
            }

            return IsOnlineOnly(File.GetAttributes(path));
        }
        catch
        {
            return false;
        }
    }

    public static bool IsOnlineOnly(FileAttributes attributes) =>
        (attributes & OnlineOnlyMask) != 0;
}
