namespace Palace.Helpers;

/// <summary>
/// Detects UNC / mapped network paths that cannot use the Windows Recycle Bin.
/// WinUI-free for Palace.Tests.
/// </summary>
public static class NetworkPaths
{
    /// <summary>
    /// True for UNC shares (<c>\\server\share</c>) or drive letters whose
    /// <see cref="DriveType"/> is <see cref="DriveType.Network"/>. Missing /
    /// unknown roots are treated as local so Recycle can still be attempted.
    /// </summary>
    public static bool IsNetworkShare(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return false;
        }

        if (path.StartsWith(@"\\", StringComparison.Ordinal)
            || path.StartsWith("//", StringComparison.Ordinal))
        {
            return true;
        }

        try
        {
            var root = Path.GetPathRoot(path);
            if (string.IsNullOrWhiteSpace(root) || root.Length < 2)
            {
                return false;
            }

            // DriveInfo needs "C:\" style; a bare "C:" is fine on Windows.
            var drive = new DriveInfo(root);
            return drive.DriveType == DriveType.Network;
        }
        catch (Exception ex) when (ex is ArgumentException or IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }
}
