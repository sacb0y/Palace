using System.IO.Enumeration;

namespace Palace.Helpers;

/// <summary>One file found by <see cref="ScanWalk"/>; attributes come from the directory listing (no extra stat).</summary>
public readonly record struct ScanEntry(string Path, FileAttributes Attributes);

/// <summary>
/// Recursive file listing for scans. Same entries as
/// <c>Directory.EnumerateFiles(root, "*.*", SearchOption.AllDirectories)</c>, but a folder that
/// cannot be opened (access denied, removed mid-scan) is counted and skipped instead of
/// throwing out of the whole scan.
/// </summary>
public sealed class ScanWalk : FileSystemEnumerator<ScanEntry>
{
    public ScanWalk(string root)
        : base(root, new EnumerationOptions
        {
            RecurseSubdirectories = true,
            IgnoreInaccessible = false,
            AttributesToSkip = 0,
            MatchType = MatchType.Win32,
            ReturnSpecialDirectories = false
        })
    {
    }

    /// <summary>Folders (including the root) that could not be listed.</summary>
    public int InaccessibleFolders { get; private set; }

    public static IEnumerable<ScanEntry> Files(string root, Action<int>? inaccessible = null)
    {
        using var walk = new ScanWalk(root);
        while (walk.MoveNext())
        {
            yield return walk.Current;
        }

        inaccessible?.Invoke(walk.InaccessibleFolders);
    }

    protected override bool ShouldIncludeEntry(ref FileSystemEntry entry) => !entry.IsDirectory;

    protected override ScanEntry TransformEntry(ref FileSystemEntry entry) =>
        new(entry.ToFullPath(), entry.Attributes);

    protected override bool ContinueOnError(int error)
    {
        InaccessibleFolders++;
        return true;
    }
}
