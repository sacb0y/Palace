using System.Runtime.InteropServices;
using System.Text;

namespace Palace.Helpers;

/// <summary>
/// Recycle / restore / permanent delete without WinUI. Production uses the OS
/// Recycle Bin on Windows; tests inject <see cref="FolderFileTrash"/>.
/// </summary>
public interface IFileTrash
{
    /// <summary>
    /// Send <paramref name="path"/> to trash. Returns a non-empty token the same
    /// instance can pass to <see cref="TryRestore"/> (Shell: original path;
    /// Folder: trash file path). Never returns null/empty on success.
    /// </summary>
    string Recycle(string path);

    /// <summary>
    /// Restore a previously recycled file to <paramref name="originalPath"/>.
    /// Returns false when the item is gone (emptied Recycle Bin, etc.).
    /// </summary>
    bool TryRestore(string token, string originalPath);

    /// <summary>Erase <paramref name="path"/> permanently (no Recycle Bin).</summary>
    void PermanentDelete(string path);
}

/// <summary>
/// Test / Linux trash: moves files under a root folder and restores by move-back.
/// Token is the trash file path.
/// </summary>
public sealed class FolderFileTrash : IFileTrash
{
    private readonly string _root;

    public FolderFileTrash(string root)
    {
        _root = root;
        Directory.CreateDirectory(_root);
    }

    public string Root => _root;

    public string Recycle(string path)
    {
        var full = Path.GetFullPath(path);
        if (!File.Exists(full))
        {
            throw new FileNotFoundException("File to recycle is missing.", full);
        }

        var name = Path.GetFileName(full);
        var destDir = Path.Combine(_root, Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(destDir);
        var dest = Path.Combine(destDir, name);
        File.Move(full, dest);
        return dest;
    }

    public bool TryRestore(string token, string originalPath)
    {
        if (string.IsNullOrWhiteSpace(token))
        {
            return false;
        }

        var dest = Path.GetFullPath(originalPath);
        if (File.Exists(dest))
        {
            if (File.Exists(token))
            {
                try { File.Delete(token); } catch (IOException) { } catch (UnauthorizedAccessException) { }
            }

            return true;
        }

        if (!File.Exists(token))
        {
            return false;
        }

        Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
        File.Move(token, dest);
        return true;
    }

    public void PermanentDelete(string path)
    {
        var full = Path.GetFullPath(path);
        if (File.Exists(full))
        {
            File.Delete(full);
        }
    }
}

/// <summary>
/// Windows Recycle Bin via <c>SHFileOperation</c> (FO_DELETE + FOF_ALLOWUNDO).
/// Restore uses Shell Namespace matching, then <c>$I</c>/<c>$R</c> metadata as a fallback.
/// Non-Windows falls back to <see cref="FolderFileTrash"/>.
/// </summary>
public sealed class ShellFileTrash : IFileTrash
{
    private const ushort FoDelete = 0x0003;
    private const ushort FofSilent = 0x0004;
    private const ushort FofNoConfirmation = 0x0010;
    private const ushort FofAllowUndo = 0x0040;
    private const ushort FofNoErrorUi = 0x0400;
    private const ushort FofWantNukeWarning = 0x4000;

    private readonly FolderFileTrash? _fallback;

    public ShellFileTrash(string? fallbackRoot = null)
    {
        if (!OperatingSystem.IsWindows())
        {
            _fallback = new FolderFileTrash(
                fallbackRoot ?? Path.Combine(Path.GetTempPath(), "palace-trash-" + Guid.NewGuid().ToString("N")));
        }
    }

    public string Recycle(string path)
    {
        if (_fallback is not null)
        {
            return _fallback.Recycle(path);
        }

        var full = Path.GetFullPath(path);
        if (!File.Exists(full))
        {
            throw new FileNotFoundException("File to recycle is missing.", full);
        }

        // Double-null-terminated path list required by SHFileOperation.
        var from = full + "\0\0";
        var op = new ShFileOpStruct
        {
            hwnd = IntPtr.Zero,
            wFunc = FoDelete,
            pFrom = from,
            pTo = null,
            fFlags = (ushort)(FofAllowUndo | FofNoConfirmation | FofSilent | FofNoErrorUi | FofWantNukeWarning),
            fAnyOperationsAborted = false,
            hNameMappings = IntPtr.Zero,
            lpszProgressTitle = null
        };

        var result = SHFileOperationW(ref op);
        if (result != 0 || op.fAnyOperationsAborted)
        {
            throw new IOException($"Recycle Bin delete failed (code {result}).");
        }

        // Token for Shell restore is the original full path (lookup key).
        return full;
    }

    public bool TryRestore(string token, string originalPath)
    {
        if (_fallback is not null)
        {
            return _fallback.TryRestore(token, originalPath);
        }

        var dest = Path.GetFullPath(
            string.IsNullOrWhiteSpace(originalPath) ? token : originalPath);
        if (File.Exists(dest))
        {
            return true;
        }

        if (TryShellNamespaceRestore(dest))
        {
            return File.Exists(dest);
        }

        return TryRecycleBinSidecarRestore(dest) && File.Exists(dest);
    }

    public void PermanentDelete(string path)
    {
        if (_fallback is not null)
        {
            _fallback.PermanentDelete(path);
            return;
        }

        var full = Path.GetFullPath(path);
        if (File.Exists(full))
        {
            File.Delete(full);
        }
    }

    private static bool TryShellNamespaceRestore(string originalPath)
    {
        if (!OperatingSystem.IsWindows())
        {
            return false;
        }

        try
        {
            var shellType = Type.GetTypeFromProgID("Shell.Application");
            if (shellType is null)
            {
                return false;
            }

            dynamic shell = Activator.CreateInstance(shellType)!;
            dynamic bin = shell.NameSpace(10);
            if (bin is null)
            {
                return false;
            }

            var originalFull = Path.GetFullPath(originalPath);
            var originalDir = Path.GetDirectoryName(originalFull) ?? "";
            var originalName = Path.GetFileName(originalFull);

            foreach (var item in bin.Items())
            {
                if (!IsRecycleItemMatch(item, bin, originalFull, originalDir, originalName))
                {
                    continue;
                }

                try
                {
                    item.InvokeVerb("restore");
                }
                catch
                {
                    try { item.InvokeVerb("undelete"); }
                    catch { continue; }
                }

                if (File.Exists(originalFull))
                {
                    return true;
                }
            }
        }
        catch
        {
            return false;
        }

        return File.Exists(originalPath);
    }

    private static bool IsRecycleItemMatch(
        dynamic item,
        dynamic bin,
        string originalFull,
        string originalDir,
        string originalName)
    {
        string? itemName = null;
        try { itemName = item.Name as string; } catch { /* ignore */ }

        var nameMatch = !string.IsNullOrWhiteSpace(itemName)
            && string.Equals(itemName, originalName, StringComparison.OrdinalIgnoreCase);

        foreach (var location in EnumerateOriginalLocations(item, bin))
        {
            if (string.IsNullOrWhiteSpace(location))
            {
                continue;
            }

            string candidate;
            try
            {
                var loc = location.Trim().TrimEnd('\\', '/');
                // DeletedFrom is often the parent folder; sometimes the full original path.
                if (string.Equals(Path.GetFullPath(loc), originalFull, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }

                if (Directory.Exists(loc) || !Path.HasExtension(loc))
                {
                    candidate = Path.GetFullPath(Path.Combine(loc, itemName ?? originalName));
                }
                else
                {
                    candidate = Path.GetFullPath(loc);
                }
            }
            catch
            {
                continue;
            }

            if (string.Equals(candidate, originalFull, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            // Same folder + same file name (Recycle display name can match original).
            var candDir = Path.GetDirectoryName(candidate) ?? "";
            if (nameMatch
                && string.Equals(candDir, originalDir, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private static IEnumerable<string> EnumerateOriginalLocations(dynamic item, dynamic bin)
    {
        string? prop = null;
        try { prop = item.ExtendedProperty("System.Recycle.DeletedFrom") as string; } catch { /* ignore */ }
        if (!string.IsNullOrWhiteSpace(prop))
        {
            yield return prop;
        }

        // Detail columns vary by locale; probe a few common "Original Location" indexes.
        foreach (var col in new[] { 1, 2, 3 })
        {
            string? detail = null;
            try { detail = bin.GetDetailsOf(item, col) as string; } catch { /* ignore */ }
            if (!string.IsNullOrWhiteSpace(detail)
                && detail.Contains('\\', StringComparison.Ordinal))
            {
                yield return detail;
            }
        }
    }

    /// <summary>
    /// Walk <c>$Recycle.Bin</c> <c>$I*</c> metadata for a matching original path and move the
    /// paired <c>$R*</c> file back. Used when Shell Namespace restore cannot find the item.
    /// </summary>
    private static bool TryRecycleBinSidecarRestore(string originalPath)
    {
        if (!OperatingSystem.IsWindows())
        {
            return false;
        }

        try
        {
            var originalFull = Path.GetFullPath(originalPath);
            var driveRoot = Path.GetPathRoot(originalFull);
            if (string.IsNullOrWhiteSpace(driveRoot))
            {
                return false;
            }

            var recycleRoot = Path.Combine(driveRoot, "$Recycle.Bin");
            if (!Directory.Exists(recycleRoot))
            {
                return false;
            }

            foreach (var sidDir in Directory.EnumerateDirectories(recycleRoot))
            {
                string[] infoFiles;
                try
                {
                    infoFiles = Directory.GetFiles(sidDir, "$I*");
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    continue;
                }

                foreach (var infoPath in infoFiles)
                {
                    if (!TryReadRecycleInfoOriginalPath(infoPath, out var deletedFrom)
                        || string.IsNullOrWhiteSpace(deletedFrom))
                    {
                        continue;
                    }

                    string deletedFull;
                    try { deletedFull = Path.GetFullPath(deletedFrom); }
                    catch { continue; }

                    if (!string.Equals(deletedFull, originalFull, StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    var name = Path.GetFileName(infoPath);
                    if (name.Length < 2 || name[0] != '$' || name[1] is not ('I' or 'i'))
                    {
                        continue;
                    }

                    var dataName = "$R" + name[2..];
                    var dataPath = Path.Combine(sidDir, dataName);
                    if (!File.Exists(dataPath))
                    {
                        // Extension may differ; search $R + same suffix stem.
                        var stem = Path.GetFileNameWithoutExtension(name)[2..];
                        dataPath = Directory.EnumerateFiles(sidDir, "$R" + stem + "*")
                            .FirstOrDefault() ?? "";
                        if (string.IsNullOrWhiteSpace(dataPath) || !File.Exists(dataPath))
                        {
                            continue;
                        }
                    }

                    Directory.CreateDirectory(Path.GetDirectoryName(originalFull)!);
                    File.Move(dataPath, originalFull);
                    try { File.Delete(infoPath); } catch (IOException) { } catch (UnauthorizedAccessException) { }
                    return File.Exists(originalFull);
                }
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return false;
        }

        return false;
    }

    /// <summary>
    /// Parse a Vista+ <c>$I</c> Recycle Bin info file for the original path.
    /// </summary>
    internal static bool TryReadRecycleInfoOriginalPath(string infoPath, out string originalPath)
    {
        originalPath = "";
        try
        {
            var bytes = File.ReadAllBytes(infoPath);
            if (bytes.Length < 28)
            {
                return false;
            }

            var version = BitConverter.ToInt64(bytes, 0);
            int pathOffset;
            if (version == 2)
            {
                // Win10+: header(8) + size(8) + deleted(8) + pathChars(4) + path
                if (bytes.Length < 32)
                {
                    return false;
                }

                var charCount = BitConverter.ToInt32(bytes, 24);
                pathOffset = 28;
                if (charCount <= 0 || pathOffset + charCount * 2 > bytes.Length)
                {
                    return false;
                }

                originalPath = Encoding.Unicode.GetString(bytes, pathOffset, charCount * 2)
                    .TrimEnd('\0');
                return !string.IsNullOrWhiteSpace(originalPath);
            }

            // Win Vista/7/8: header(8) + size(8) + deleted(8) + null-terminated path
            pathOffset = 24;
            var end = pathOffset;
            while (end + 1 < bytes.Length)
            {
                if (bytes[end] == 0 && bytes[end + 1] == 0)
                {
                    break;
                }

                end += 2;
            }

            if (end <= pathOffset)
            {
                return false;
            }

            originalPath = Encoding.Unicode.GetString(bytes, pathOffset, end - pathOffset);
            return !string.IsNullOrWhiteSpace(originalPath);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            return false;
        }
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern int SHFileOperationW(ref ShFileOpStruct fileOp);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct ShFileOpStruct
    {
        public IntPtr hwnd;
        public ushort wFunc;
        [MarshalAs(UnmanagedType.LPWStr)]
        public string pFrom;
        [MarshalAs(UnmanagedType.LPWStr)]
        public string? pTo;
        public ushort fFlags;
        public bool fAnyOperationsAborted;
        public IntPtr hNameMappings;
        [MarshalAs(UnmanagedType.LPWStr)]
        public string? lpszProgressTitle;
    }
}
