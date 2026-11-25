using System.IO.Compression;

namespace Palace.Helpers;

/// <summary>
/// Packaging checks for <c>palace_smooth.dll</c> in the MSIX / output layout.
/// WinUI-free so Palace.Tests can assert without loading the native module.
/// </summary>
public static class SmoothPresenterPackaging
{
    public const string DllFileName = "palace_smooth.dll";
    public const string NativeRelativeDir = "Native/smooth/win-x64";
    public const string CsprojContentSnippet = @"Native\smooth\win-x64\palace_smooth.dll";

    public static string ExpectedNativePath(string repoRoot) =>
        Path.Combine(repoRoot, "Native", "smooth", "win-x64", DllFileName);

    public static bool CsprojPackagesDll(string csprojText) =>
        !string.IsNullOrEmpty(csprojText)
        && csprojText.Contains(CsprojContentSnippet, StringComparison.OrdinalIgnoreCase);

    public static bool LayoutContainsDll(string? appDir)
    {
        if (string.IsNullOrWhiteSpace(appDir) || !Directory.Exists(appDir))
        {
            return false;
        }

        var direct = Path.Combine(appDir, DllFileName);
        if (File.Exists(direct))
        {
            return true;
        }

        try
        {
            return Directory.EnumerateFiles(appDir, DllFileName, SearchOption.AllDirectories).Any();
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// SideloadOnly MSIX is a zip; package root means <c>palace_smooth.dll</c> with no directory prefix.
    /// </summary>
    public static bool MsixContainsDllAtPackageRoot(string? msixPath)
    {
        if (string.IsNullOrWhiteSpace(msixPath) || !File.Exists(msixPath))
        {
            return false;
        }

        try
        {
            using var zip = ZipFile.OpenRead(msixPath);
            foreach (var entry in zip.Entries)
            {
                var name = entry.FullName.Replace('\\', '/').TrimStart('/');
                if (string.Equals(name, DllFileName, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }
        }
        catch
        {
            return false;
        }

        return false;
    }
}
