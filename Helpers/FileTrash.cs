using System.Runtime.InteropServices;

namespace Palace.Helpers;

/// <summary>
/// Recycle / restore / permanent delete without WinUI. Production uses the OS
/// Recycle Bin on Windows; tests inject <see cref="FolderFileTrash"/>.
/// </summary>
public interface IFileTrash
{
    /// <summary>
    /// Send <paramref name="path"/> to trash. Returns a token the same instance
    /// can pass to <see cref="TryRestore"/> (often the original path).
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
/// Restore walks the shell Recycle Bin for a matching original path. Non-Windows
/// falls back to <see cref="FolderFileTrash"/>.
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

        return full;
    }

    public bool TryRestore(string token, string originalPath)
    {
        if (_fallback is not null)
        {
            return _fallback.TryRestore(token, originalPath);
        }

        var dest = Path.GetFullPath(originalPath);
        if (File.Exists(dest))
        {
            return true;
        }

        return TryShellRestore(dest);
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

    private static bool TryShellRestore(string originalPath)
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

            foreach (var item in bin.Items())
            {
                string? location = null;
                try
                {
                    location = item.ExtendedProperty("System.Recycle.DeletedFrom") as string
                        ?? bin.GetDetailsOf(item, 1) as string;
                }
                catch
                {
                    // ignore per-item probe failures
                }

                if (string.IsNullOrWhiteSpace(location))
                {
                    continue;
                }

                var loc = location.Trim();
                string candidate;
                try
                {
                    candidate = Directory.Exists(loc)
                        ? Path.Combine(loc, (string)item.Name)
                        : loc;
                    candidate = Path.GetFullPath(candidate);
                }
                catch
                {
                    continue;
                }

                if (!string.Equals(candidate, originalPath, StringComparison.OrdinalIgnoreCase)
                    && !string.Equals(Path.GetFullPath(loc), originalPath, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                item.InvokeVerb("restore");
                return File.Exists(originalPath);
            }
        }
        catch
        {
            return false;
        }

        return File.Exists(originalPath);
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
