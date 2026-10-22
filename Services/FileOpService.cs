using Palace.Data;
using Palace.Helpers;
using Palace.Models;

namespace Palace.Services;

/// <summary>Outcome of one <see cref="FileOpService"/> call, worded for a status line or toast.</summary>
public sealed class FileOpResult
{
    public bool Ok { get; init; }
    public string Message { get; init; } = "";
    /// <summary>Undo journal id when anything changed on disk; null when nothing happened.</summary>
    public string? BatchId { get; init; }
    public string? Summary { get; init; }
    /// <summary>Resulting folder (create / rename) or destination folder (move).</summary>
    public string? Path { get; init; }
    public IReadOnlyList<FileOpMove> Moves { get; init; } = [];
    public IReadOnlyList<string> Skipped { get; init; } = [];

    public static FileOpResult Fail(string message) => new() { Ok = false, Message = message };
}

public readonly record struct FileOpMove(string AssetId, string OldPath, string NewPath, string NewSourceId);

/// <summary>
/// Logged, undoable file operations for organizing: move assets, new / rename / delete folder,
/// and Undo. WinUI-free so the app, tests and a future <c>palace</c> CLI share one code path.
/// <list type="bullet">
/// <item>Every path must sit inside a <b>local</b> watched source of the project, not inside another
/// project's nested source, and not behind a junction / symlink below the source.</item>
/// <item>A folder that contains another watched source is never renamed. Rename keeps orphan flags,
/// and rolls the folder back when the catalog update fails.</item>
/// <item>Cancelling a move after files already moved stops there and still journals them for Undo.</item>
/// <item>Never overwrites: name clashes get <c>name (2).ext</c>; <c>File.Move(overwrite: false)</c>.</item>
/// <item>Never reads file contents. Online-only (Dropbox / OneDrive) placeholders move only inside
/// their own watched folder, where a move is a rename and stays a placeholder.</item>
/// <item>Folder delete is for empty folders only, so Undo can always recreate it.</item>
/// </list>
/// </summary>
public sealed partial class FileOpService
{
    private readonly CatalogService _catalog;
    private readonly Func<IDisposable>? _suppressWatcher;

    public FileOpService(CatalogService catalog, Func<IDisposable>? suppressWatcher = null)
    {
        _catalog = catalog;
        _suppressWatcher = suppressWatcher;
    }

    /// <summary>Deepest local, existing watched source that contains <paramref name="path"/>.</summary>
    public static SourceFolder? ResolveRoot(string path, IEnumerable<SourceFolder> sources)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return null;
        }

        SourceFolder? best = null;
        foreach (var source in sources)
        {
            if (source.Kind != SourceKind.Local || string.IsNullOrWhiteSpace(source.Path))
            {
                continue;
            }

            if (FolderNames.IsSameOrUnder(path, source.Path)
                && (best is null || source.Path.Length > best.Path.Length))
            {
                best = source;
            }
        }

        return best;
    }

    /// <summary>
    /// The project's watched source that may be written at <paramref name="path"/>, or an error.
    /// <list type="bullet">
    /// <item>The deepest watched source across <b>all</b> projects decides, so a folder nested inside
    /// another project's Library folder is refused rather than written behind that project's back.</item>
    /// <item>No folder between the source and <paramref name="path"/> may be a junction or symlink:
    /// those can point outside the watched root while still passing the path check. Cloud
    /// placeholders (Dropbox / OneDrive) have no link target and are fine.</item>
    /// </list>
    /// </summary>
    public static SourceFolder? ResolveWritableRoot(
        string path,
        IEnumerable<SourceFolder> allSources,
        string projectId,
        out string error)
    {
        error = "";
        var root = ResolveRoot(path, allSources);
        if (root is null)
        {
            error = "not in one of your Library folders";
            return null;
        }

        if (!string.Equals(root.ProjectId, projectId, StringComparison.Ordinal))
        {
            error = "inside a Library folder of another project";
            return null;
        }

        if (LinkedFolderBetween(root.Path, path) is { } link)
        {
            error = $"{DisplayName(link)} is a shortcut (junction or symlink) to another place";
            return null;
        }

        return root;
    }

    /// <summary>
    /// First folder strictly below <paramref name="root"/> on the way to <paramref name="path"/>
    /// (inclusive) that is a junction or symlink, or null. Missing folders are not links.
    /// </summary>
    public static string? LinkedFolderBetween(string root, string path)
    {
        var rootFull = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var current = Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        while (current.Length > rootFull.Length && FolderNames.IsSameOrUnder(current, rootFull))
        {
            try
            {
                var info = new DirectoryInfo(current);
                if (info.Exists && info.LinkTarget is not null)
                {
                    return current;
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                return current;
            }

            var parent = Path.GetDirectoryName(current);
            if (parent is null || parent.Length >= current.Length)
            {
                break;
            }

            current = parent;
        }

        return null;
    }

    /// <summary>A watched source (any project) sits strictly inside <paramref name="folderPath"/>.</summary>
    public static SourceFolder? NestedSourceIn(string folderPath, IEnumerable<SourceFolder> allSources) =>
        allSources.FirstOrDefault(s => !string.IsNullOrWhiteSpace(s.Path)
            && !FolderNames.SamePath(s.Path, folderPath)
            && FolderNames.IsSameOrUnder(s.Path, folderPath));

    public static string MoveSummary(int count, string destinationDir) =>
        $"Move {Files(count)} to {DisplayName(destinationDir)}";

    public static string UndoLabel(FileOpBatch? batch) => batch is null
        ? "Nothing to undo"
        : batch.Kind switch
        {
            FileOpKind.Move => $"Undo move of {Files(batch.Items.Count(i => i.Step == FileOpStep.MoveFile))}",
            FileOpKind.Organize => $"Undo organize of {Files(batch.Items.Count(i => i.Step == FileOpStep.MoveFile))}",
            FileOpKind.CreateFolder => $"Undo new folder {DisplayName(batch.Items.LastOrDefault()?.NewPath)}",
            FileOpKind.RenameFolder => $"Undo rename to {DisplayName(batch.Items.LastOrDefault()?.NewPath)}",
            FileOpKind.DeleteFolder => $"Undo delete of {DisplayName(batch.Items.LastOrDefault()?.OldPath)}",
            FileOpKind.TagAssign => $"Undo tag on {Items(batch.Items.Count(i => i.Step == FileOpStep.AssignTag))}",
            FileOpKind.TagRemove => $"Undo remove tag from {Items(batch.Items.Count(i => i.Step == FileOpStep.RemoveTag))}",
            FileOpKind.TagDelete => $"Undo delete of “{TagDeleteName(batch)}”",
            _ => "Undo"
        };

    private static string Items(int count) => count == 1 ? "1 item" : $"{count} items";

    private static string TagDeleteName(FileOpBatch batch)
    {
        var json = batch.Items.LastOrDefault()?.NewPath;
        return CatalogService.DeserializeTagSnapshot(json)?.Tag.Name ?? "tag";
    }

    private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, SemaphoreSlim> Gates = new(StringComparer.Ordinal);

    /// <summary>
    /// One file operation per project at a time (moves, folder edits, Undo's read / run / mark), so
    /// batches never interleave and Undo always reverses the batch it read. Shared across instances.
    /// </summary>
    private static async Task<T> GatedAsync<T>(string projectId, Func<Task<T>> work, CancellationToken cancellationToken = default)
    {
        var gate = Gates.GetOrAdd(projectId, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            return await work().ConfigureAwait(false);
        }
        finally
        {
            gate.Release();
        }
    }

    /// <summary>
    /// Move assets into <paramref name="destinationDir"/> (an existing folder inside a watched source).
    /// Name clashes get a unique name; missing / online-only-across-roots / already-there files are skipped.
    /// When <paramref name="absorbCreatedFolder"/> is the destination and the newest undoable batch is
    /// a <see cref="FileOpKind.CreateFolder"/> for that path (Move dialog "New folder" then Move here),
    /// the create steps are folded into this Move batch so one Undo returns the files and removes the
    /// empty folder.
    /// </summary>
    public Task<FileOpResult> MoveAssetsAsync(
        IReadOnlyList<Asset> assets,
        string destinationDir,
        string projectId,
        CancellationToken cancellationToken = default,
        string? absorbCreatedFolder = null) =>
        GatedAsync(projectId, () => MoveAssetsCoreAsync(assets, destinationDir, projectId, cancellationToken, absorbCreatedFolder), cancellationToken);

    /// <summary>Create <paramref name="name"/> under <paramref name="parentDir"/> (inside a watched source).</summary>
    public Task<FileOpResult> CreateFolderAsync(string parentDir, string name, string projectId) =>
        GatedAsync(projectId, () => CreateFolderCoreAsync(parentDir, name, projectId));

    /// <summary>Rename a folder inside a watched source (not a source root) and rebase its catalog rows.</summary>
    public Task<FileOpResult> RenameFolderAsync(string folderPath, string newName, string projectId) =>
        GatedAsync(projectId, () => RenameFolderCoreAsync(folderPath, newName, projectId));

    /// <summary>Delete an <b>empty</b> folder inside a watched source (not a source root). Undo recreates it.</summary>
    public Task<FileOpResult> DeleteEmptyFolderAsync(string folderPath, string projectId) =>
        GatedAsync(projectId, () => DeleteEmptyFolderCoreAsync(folderPath, projectId));

    /// <summary>
    /// Apply an Organize dry-run: create missing folders, move files, journal <b>one</b>
    /// <see cref="FileOpKind.Organize"/> batch so Undo / Ctrl+Z reverses the whole run
    /// (moves back, then removes empty folders this run created).
    /// </summary>
    public Task<FileOpResult> ApplyOrganizeAsync(string projectId, IReadOnlyList<OrganizeApplyItem> items) =>
        GatedAsync(projectId, () => ApplyOrganizeCoreAsync(projectId, items));

    /// <summary>Reverse the newest file operation of the project (see <see cref="UndoLastCoreAsync"/>).</summary>
    public Task<FileOpResult> UndoLastAsync(string projectId) =>
        GatedAsync(projectId, () => UndoLastCoreAsync(projectId));

    /// <summary>
    /// Reverse a specific undoable batch <b>only when it is still the newest</b>
    /// for the project. Used by Review Reject undo so an unrelated Move/Delete
    /// after the reject cannot be undone by Review Ctrl+Z.
    /// </summary>
    public Task<FileOpResult> UndoBatchAsync(string projectId, string batchId) =>
        GatedAsync(projectId, () => UndoBatchCoreAsync(projectId, batchId));

    public Task<FileOpBatch?> PeekUndoAsync(string projectId) =>
        _catalog.GetLastUndoableFileOpAsync(projectId);

    private async Task<FileOpResult> ApplyOrganizeCoreAsync(string projectId, IReadOnlyList<OrganizeApplyItem> planned)
    {
        if (planned.Count == 0)
        {
            return FileOpResult.Fail("Nothing to organize.");
        }

        var allSources = await _catalog.GetSourceFoldersAsync().ConfigureAwait(false);
        var createdDirs = new List<string>();
        var seenCreated = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var moveItems = new List<FileOpItem>();
        var moves = new List<FileOpMove>();
        var skipped = new List<string>();

        using (_suppressWatcher?.Invoke())
        {
            foreach (var plan in planned)
            {
                var destDir = Path.GetDirectoryName(plan.NewPath);
                if (string.IsNullOrEmpty(destDir))
                {
                    skipped.Add($"{plan.FileName}: bad destination");
                    continue;
                }

                var destRoot = ResolveWritableRoot(destDir, allSources, projectId, out var destError);
                if (destRoot is null)
                {
                    skipped.Add($"{plan.FileName}: {destError}");
                    continue;
                }

                var srcRoot = ResolveWritableRoot(plan.OldPath, allSources, projectId, out var srcError);
                if (srcRoot is null)
                {
                    skipped.Add($"{plan.FileName}: {srcError}");
                    continue;
                }

                if (!CloudFile.Exists(plan.OldPath))
                {
                    skipped.Add($"{plan.FileName}: file is missing");
                    continue;
                }

                try
                {
                    foreach (var created in EnsureDirsUnderRoot(destRoot.Path, destDir, seenCreated))
                    {
                        createdDirs.Add(created);
                    }

                    var target = plan.NewPath;
                    if (CloudFile.Exists(target) || Directory.Exists(target))
                    {
                        target = FolderNames.UniqueFilePath(destDir, Path.GetFileName(target), p =>
                            CloudFile.Exists(p) || Directory.Exists(p));
                    }

                    File.Move(plan.OldPath, target, overwrite: false);
                    await _catalog.UpdateAssetLocationAsync(plan.AssetId, target, destRoot.Id).ConfigureAwait(false);
                    await _catalog.SetOrganizeErrorAsync(plan.AssetId, null).ConfigureAwait(false);

                    moves.Add(new FileOpMove(plan.AssetId, plan.OldPath, target, destRoot.Id));
                    moveItems.Add(new FileOpItem
                    {
                        Step = FileOpStep.MoveFile,
                        AssetId = plan.AssetId,
                        OldPath = plan.OldPath,
                        NewPath = target,
                        OldSourceId = srcRoot.Id,
                        NewSourceId = destRoot.Id
                    });
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException)
                {
                    skipped.Add($"{plan.FileName}: {ex.Message}");
                    await _catalog.SetOrganizeErrorAsync(plan.AssetId, ex.Message).ConfigureAwait(false);
                }
            }
        }

        if (moves.Count == 0)
        {
            // Roll back empty dirs we created if nothing moved.
            foreach (var dir in createdDirs.OrderByDescending(d => d.Length))
            {
                TryDeleteEmptyDir(dir);
            }

            return new FileOpResult
            {
                Ok = false,
                Message = skipped.Count == 1 ? $"Nothing organized. {skipped[0]}." : "Nothing organized.",
                Skipped = skipped
            };
        }

        // Parents before children in the journal so Undo (reverse) deletes children first.
        var journal = createdDirs
            .OrderBy(d => d.Length)
            .Select(d => new FileOpItem { Step = FileOpStep.CreateDir, OldPath = "", NewPath = d })
            .Concat(moveItems)
            .ToList();

        var summary = $"Organize {Files(moves.Count)}";
        var batchId = await LogAsync(projectId, FileOpKind.Organize, summary, journal).ConfigureAwait(false);
        if (batchId is null)
        {
            return JournalFailed();
        }

        var message = skipped.Count == 0
            ? $"Organized {Files(moves.Count)}."
            : $"Organized {Files(moves.Count)}. Skipped {skipped.Count}.";
        return new FileOpResult
        {
            Ok = true,
            Message = message,
            BatchId = batchId,
            Summary = summary,
            Moves = moves,
            Skipped = skipped
        };
    }

    /// <summary>Create missing directories from <paramref name="root"/> down to <paramref name="dir"/>; return ones created now.</summary>
    private static List<string> EnsureDirsUnderRoot(string root, string dir, HashSet<string> seenCreated)
    {
        var created = new List<string>();
        var rootFull = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var target = Path.GetFullPath(dir).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        if (!FolderNames.IsSameOrUnder(target, rootFull))
        {
            return created;
        }

        var rel = Path.GetRelativePath(rootFull, target);
        if (rel == "." || string.IsNullOrEmpty(rel))
        {
            return created;
        }

        var current = rootFull;
        foreach (var segment in rel.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar))
        {
            if (string.IsNullOrEmpty(segment))
            {
                continue;
            }

            current = Path.Combine(current, segment);
            if (Directory.Exists(current))
            {
                continue;
            }

            Directory.CreateDirectory(current);
            if (seenCreated.Add(current))
            {
                created.Add(current);
            }
        }

        return created;
    }

    private static void TryDeleteEmptyDir(string dir)
    {
        try
        {
            if (Directory.Exists(dir) && IsEmptyFolder(dir))
            {
                Directory.Delete(dir, recursive: false);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Best-effort rollback of unused organize folders.
        }
    }

    /// <summary>
    /// Move assets into <paramref name="destinationDir"/> (an existing folder inside a watched source).
    /// Name clashes get a unique name; missing / online-only-across-roots / already-there files are skipped.
    /// </summary>
    private async Task<FileOpResult> MoveAssetsCoreAsync(
        IReadOnlyList<Asset> assets,
        string destinationDir,
        string projectId,
        CancellationToken cancellationToken = default,
        string? absorbCreatedFolder = null)
    {
        if (assets.Count == 0)
        {
            return FileOpResult.Fail("Select assets first.");
        }

        var allSources = await _catalog.GetSourceFoldersAsync().ConfigureAwait(false);
        var sources = allSources.Where(s => string.Equals(s.ProjectId, projectId, StringComparison.Ordinal)).ToList();
        if (ResolveRoot(destinationDir, sources) is null)
        {
            return FileOpResult.Fail("Pick a folder inside one of your Library folders.");
        }

        var destRoot = ResolveWritableRoot(destinationDir, allSources, projectId, out var destError);
        if (destRoot is null)
        {
            return FileOpResult.Fail($"Can't move there: {DisplayName(destinationDir)} is {destError}.");
        }

        destinationDir = Path.GetFullPath(destinationDir);
        if (!Directory.Exists(destinationDir))
        {
            return FileOpResult.Fail($"{DisplayName(destinationDir)} doesn't exist anymore.");
        }

        var moves = new List<FileOpMove>();
        var skipped = new List<string>();
        var items = new List<FileOpItem>();
        var reserved = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var stopped = false;
        using (_suppressWatcher?.Invoke())
        {
            foreach (var asset in assets)
            {
                // Before the first change, cancel outright. After that, stop and still log what
                // moved so Undo can reverse it (never leave unjournaled moves behind).
                if (cancellationToken.IsCancellationRequested)
                {
                    if (moves.Count == 0)
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                    }

                    stopped = true;
                    break;
                }

                var srcRoot = ResolveRoot(asset.Path, sources);
                if (srcRoot is null)
                {
                    skipped.Add($"{asset.FileName}: not in a Library folder");
                    continue;
                }

                // The deepest watched source across all projects owns the file.
                if (!string.Equals(ResolveRoot(asset.Path, allSources)?.ProjectId, projectId, StringComparison.Ordinal))
                {
                    skipped.Add($"{asset.FileName}: inside a Library folder of another project");
                    continue;
                }

                if (LinkedFolderBetween(srcRoot.Path, Path.GetDirectoryName(asset.Path)!) is not null)
                {
                    skipped.Add($"{asset.FileName}: its folder is a shortcut (junction or symlink)");
                    continue;
                }

                if (asset.IsOrphan || !CloudFile.Exists(asset.Path))
                {
                    skipped.Add($"{asset.FileName}: file is missing");
                    continue;
                }

                if (FolderNames.SamePath(Path.GetDirectoryName(asset.Path), destinationDir))
                {
                    skipped.Add($"{asset.FileName}: already in {DisplayName(destinationDir)}");
                    continue;
                }

                if (CloudFile.IsOnlineOnly(asset.Path) && !string.Equals(srcRoot.Id, destRoot.Id, StringComparison.Ordinal))
                {
                    skipped.Add($"{asset.FileName}: online-only; moving it to another Library folder would download it");
                    continue;
                }

                var moved = await MoveOneAsync(asset.Path, destinationDir, reserved).ConfigureAwait(false);
                if (moved.Error is not null)
                {
                    skipped.Add($"{asset.FileName}: {moved.Error}");
                    continue;
                }

                try
                {
                    await _catalog.UpdateAssetLocationAsync(asset.Id, moved.Path!, destRoot.Id).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    // Keep disk and catalog in step: put the file back. If that fails too, stop the
                    // batch here (journal what already moved) and say so instead of a quiet skip.
                    if (TryMoveBack(moved.Path!, asset.Path))
                    {
                        skipped.Add($"{asset.FileName}: {ex.Message}");
                        continue;
                    }

                    skipped.Add($"{asset.FileName}: now at {moved.Path} but the catalog couldn't be updated ({ex.Message}); rescan to fix it");
                    stopped = true;
                    break;
                }

                reserved.Add(moved.Path!);
                moves.Add(new FileOpMove(asset.Id, asset.Path, moved.Path!, destRoot.Id));
                items.Add(new FileOpItem
                {
                    Step = FileOpStep.MoveFile,
                    AssetId = asset.Id,
                    OldPath = asset.Path,
                    NewPath = moved.Path!,
                    OldSourceId = srcRoot.Id,
                    NewSourceId = destRoot.Id
                });
            }
        }

        if (moves.Count == 0)
        {
            return new FileOpResult
            {
                Ok = false,
                Message = skipped.Count == 1 ? $"Nothing moved. {skipped[0]}." : "Nothing moved.",
                Path = destinationDir,
                Skipped = skipped
            };
        }

        // Parents before children so Undo (reverse seq) deletes children first — same as Organize.
        var journal = new List<FileOpItem>();
        string? absorbBatchId = null;
        if (!string.IsNullOrWhiteSpace(absorbCreatedFolder)
            && FolderNames.SamePath(absorbCreatedFolder, destinationDir))
        {
            var peek = await _catalog.GetLastUndoableFileOpAsync(projectId).ConfigureAwait(false);
            if (peek?.Kind == FileOpKind.CreateFolder
                && peek.Items.Any(i => i.Step == FileOpStep.CreateDir
                    && FolderNames.SamePath(i.NewPath, destinationDir)))
            {
                absorbBatchId = peek.Id;
                journal.AddRange(peek.Items
                    .Where(i => i.Step == FileOpStep.CreateDir)
                    .OrderBy(i => i.NewPath.Length)
                    .Select(i => new FileOpItem
                    {
                        Step = FileOpStep.CreateDir,
                        OldPath = i.OldPath,
                        NewPath = i.NewPath
                    }));
            }
        }

        journal.AddRange(items);
        var summary = MoveSummary(moves.Count, destinationDir);
        var batchId = await LogAsync(projectId, FileOpKind.Move, summary, journal).ConfigureAwait(false);
        if (batchId is null)
        {
            return JournalFailed();
        }

        if (absorbBatchId is not null)
        {
            // Create-folder batch is superseded by this Move (CreateDir steps copied above).
            await _catalog.MarkFileOpUndoneAsync(absorbBatchId).ConfigureAwait(false);
        }

        var dest = Quote(DisplayName(destinationDir));
        var message = skipped.Count == 0
            ? $"Moved {Files(moves.Count)} to {dest}."
            : $"Moved {Files(moves.Count)} to {dest}. Skipped {skipped.Count}.";
        if (stopped)
        {
            message += $" Stopped before the other {assets.Count - moves.Count - skipped.Count}.";
        }
        return new FileOpResult
        {
            Ok = true,
            Message = message,
            BatchId = batchId,
            Summary = summary,
            Path = destinationDir,
            Moves = moves,
            Skipped = skipped
        };
    }

    /// <summary>Create <paramref name="name"/> under <paramref name="parentDir"/> (inside a watched source).</summary>
    private async Task<FileOpResult> CreateFolderCoreAsync(string parentDir, string name, string projectId)
    {
        if (!FolderNames.TryValidate(name, out var cleaned, out var error))
        {
            return FileOpResult.Fail(error);
        }

        var allSources = await _catalog.GetSourceFoldersAsync().ConfigureAwait(false);
        if (ResolveRoot(parentDir, allSources.Where(s => string.Equals(s.ProjectId, projectId, StringComparison.Ordinal))) is null)
        {
            return FileOpResult.Fail("New folders go inside one of your Library folders.");
        }

        if (ResolveWritableRoot(parentDir, allSources, projectId, out var rootError) is null)
        {
            return FileOpResult.Fail($"Can't create a folder in {DisplayName(parentDir)}: it is {rootError}.");
        }

        if (!Directory.Exists(parentDir))
        {
            return FileOpResult.Fail($"{DisplayName(parentDir)} doesn't exist anymore.");
        }

        var path = Path.Combine(Path.GetFullPath(parentDir), cleaned);
        if (ChildExists(parentDir, cleaned))
        {
            return FileOpResult.Fail($"\"{cleaned}\" already exists here.");
        }

        using (_suppressWatcher?.Invoke())
        {
            try
            {
                Directory.CreateDirectory(path);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                return FileOpResult.Fail($"Couldn't create \"{cleaned}\": {ex.Message}");
            }
        }

        var summary = $"New folder {cleaned}";
        var batchId = await LogAsync(projectId, FileOpKind.CreateFolder, summary,
            [new FileOpItem { Step = FileOpStep.CreateDir, OldPath = "", NewPath = path }]).ConfigureAwait(false);
        if (batchId is null)
        {
            return JournalFailed();
        }

        return new FileOpResult { Ok = true, Message = $"Created {Quote(cleaned)}.", BatchId = batchId, Summary = summary, Path = path };
    }

    /// <summary>Rename a folder inside a watched source (not a source root) and rebase its catalog rows.</summary>
    private async Task<FileOpResult> RenameFolderCoreAsync(string folderPath, string newName, string projectId)
    {
        if (!FolderNames.TryValidate(newName, out var cleaned, out var error))
        {
            return FileOpResult.Fail(error);
        }

        var allSources = await _catalog.GetSourceFoldersAsync().ConfigureAwait(false);
        var root = ResolveRoot(folderPath, allSources.Where(s => string.Equals(s.ProjectId, projectId, StringComparison.Ordinal)));
        if (root is null)
        {
            return FileOpResult.Fail("Only folders inside your Library folders can be renamed.");
        }

        if (FolderNames.SamePath(folderPath, root.Path) || allSources.Any(s => FolderNames.SamePath(s.Path, folderPath)))
        {
            return FileOpResult.Fail("This is a Library folder. Rename it in File Explorer, then add it again.");
        }

        if (ResolveWritableRoot(folderPath, allSources, projectId, out var rootError) is null)
        {
            return FileOpResult.Fail($"Can't rename {DisplayName(folderPath)}: it is {rootError}.");
        }

        // Renaming would move a watched source on disk while its SourceFolder row (and watcher)
        // still point at the old path.
        if (NestedSourceIn(folderPath, allSources) is { } nested)
        {
            return FileOpResult.Fail($"{DisplayName(folderPath)} contains the Library folder {DisplayName(nested.Path)}. Rename it in File Explorer instead.");
        }

        folderPath = Path.GetFullPath(folderPath).TrimEnd(Path.DirectorySeparatorChar);
        if (!Directory.Exists(folderPath))
        {
            return FileOpResult.Fail($"{DisplayName(folderPath)} doesn't exist anymore.");
        }

        var parent = Path.GetDirectoryName(folderPath)!;
        var target = Path.Combine(parent, cleaned);
        if (string.Equals(folderPath, target, StringComparison.Ordinal))
        {
            return new FileOpResult { Ok = true, Message = "Name unchanged.", Path = folderPath };
        }

        var caseOnly = FolderNames.SamePath(folderPath, target);
        if (!caseOnly && ChildExists(parent, cleaned))
        {
            return FileOpResult.Fail($"\"{cleaned}\" already exists here.");
        }

        using (_suppressWatcher?.Invoke())
        {
            var moved = MoveDirectory(folderPath, target);
            if (moved is not null)
            {
                return FileOpResult.Fail($"Couldn't rename {DisplayName(folderPath)}: {moved}");
            }

            try
            {
                await _catalog.RebaseFolderPathsAsync(folderPath, target).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                // Keep disk and catalog in step: put the folder back under its old name.
                var back = MoveDirectory(target, folderPath);
                return FileOpResult.Fail(back is null
                    ? $"Couldn't rename {DisplayName(folderPath)}: {ex.Message}"
                    : $"Renamed {DisplayName(folderPath)} on disk but couldn't update the catalog ({ex.Message}). Rescan to fix it.");
            }
        }

        var summary = $"Rename {DisplayName(folderPath)} to {cleaned}";
        var batchId = await LogAsync(projectId, FileOpKind.RenameFolder, summary,
            [new FileOpItem { Step = FileOpStep.RenameDir, OldPath = folderPath, NewPath = target }]).ConfigureAwait(false);
        if (batchId is null)
        {
            return JournalFailed();
        }

        return new FileOpResult
        {
            Ok = true,
            Message = $"Renamed {Quote(DisplayName(folderPath))} to {Quote(cleaned)}.",
            BatchId = batchId,
            Summary = summary,
            Path = target
        };
    }

    /// <summary>
    /// A file or folder named <paramref name="name"/> already sits in <paramref name="parent"/>,
    /// ignoring case (Windows names are case-insensitive even when the test disk is not).
    /// </summary>
    public static bool ChildExists(string parent, string name)
    {
        var path = Path.Combine(parent, name);
        if (Directory.Exists(path) || CloudFile.Exists(path))
        {
            return true;
        }

        try
        {
            return Directory.EnumerateFileSystemEntries(parent, "*", new EnumerationOptions { AttributesToSkip = 0, IgnoreInaccessible = true })
                .Any(entry => string.Equals(Path.GetFileName(entry), name, StringComparison.OrdinalIgnoreCase));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    /// <summary>True when the folder has no files or subfolders at all (hidden ones count).</summary>
    public static bool IsEmptyFolder(string folderPath)
    {
        try
        {
            return Directory.Exists(folderPath)
                && !Directory.EnumerateFileSystemEntries(folderPath, "*", new EnumerationOptions
                {
                    AttributesToSkip = 0,
                    IgnoreInaccessible = false,
                    RecurseSubdirectories = false
                }).Any();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    /// <summary>Delete an <b>empty</b> folder inside a watched source (not a source root). Undo recreates it.</summary>
    private async Task<FileOpResult> DeleteEmptyFolderCoreAsync(string folderPath, string projectId)
    {
        var allSources = await _catalog.GetSourceFoldersAsync().ConfigureAwait(false);
        var root = ResolveRoot(folderPath, allSources.Where(s => string.Equals(s.ProjectId, projectId, StringComparison.Ordinal)));
        if (root is null)
        {
            return FileOpResult.Fail("Only folders inside your Library folders can be deleted here.");
        }

        if (FolderNames.SamePath(folderPath, root.Path) || allSources.Any(s => FolderNames.SamePath(s.Path, folderPath)))
        {
            return FileOpResult.Fail("This is a Library folder. Remove it in Settings instead.");
        }

        if (ResolveWritableRoot(folderPath, allSources, projectId, out var rootError) is null)
        {
            return FileOpResult.Fail($"Can't delete {DisplayName(folderPath)}: it is {rootError}.");
        }

        folderPath = Path.GetFullPath(folderPath).TrimEnd(Path.DirectorySeparatorChar);
        if (!Directory.Exists(folderPath))
        {
            return FileOpResult.Fail($"{DisplayName(folderPath)} doesn't exist anymore.");
        }

        if (!IsEmptyFolder(folderPath))
        {
            return FileOpResult.Fail($"{DisplayName(folderPath)} isn't empty. Only empty folders can be deleted here.");
        }

        using (_suppressWatcher?.Invoke())
        {
            try
            {
                Directory.Delete(folderPath, recursive: false);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                return FileOpResult.Fail($"Couldn't delete {DisplayName(folderPath)}: {ex.Message}");
            }
        }

        var summary = $"Delete folder {DisplayName(folderPath)}";
        var batchId = await LogAsync(projectId, FileOpKind.DeleteFolder, summary,
            [new FileOpItem { Step = FileOpStep.DeleteDir, OldPath = folderPath, NewPath = "" }]).ConfigureAwait(false);
        if (batchId is null)
        {
            return JournalFailed();
        }

        return new FileOpResult
        {
            Ok = true,
            Message = $"Deleted folder {Quote(DisplayName(folderPath))}.",
            BatchId = batchId,
            Summary = summary,
            Path = folderPath
        };
    }

    /// <summary>
    /// Reverse the newest file operation of the project: move files back (unique name if the old
    /// spot is taken), rename folders back, remove created folders that are still empty, recreate
    /// deleted ones. Best effort per step; the batch is marked undone afterwards, unless no step
    /// worked and a failure may clear on retry (then it stays undoable).
    /// </summary>
    private async Task<FileOpResult> UndoLastCoreAsync(string projectId)
    {
        var batch = await _catalog.GetLastUndoableFileOpAsync(projectId).ConfigureAwait(false);
        if (batch is null)
        {
            return FileOpResult.Fail("Nothing to undo.");
        }

        return await UndoKnownBatchAsync(batch, projectId).ConfigureAwait(false);
    }

    private async Task<FileOpResult> UndoBatchCoreAsync(string projectId, string batchId)
    {
        if (string.IsNullOrWhiteSpace(batchId))
        {
            return FileOpResult.Fail("Nothing to undo.");
        }

        var newest = await _catalog.GetLastUndoableFileOpAsync(projectId).ConfigureAwait(false);
        if (newest is not null && string.Equals(newest.Id, batchId, StringComparison.Ordinal))
        {
            return await UndoKnownBatchAsync(newest, projectId).ConfigureAwait(false);
        }

        // Toolbar Undo / Ctrl+Z may already have reversed this reject move.
        // Treat already-undone as success with no further moves so Review session
        // can still roll back.
        var known = await _catalog.GetFileOpBatchAsync(batchId).ConfigureAwait(false);
        if (known is not null && known.IsUndone)
        {
            return new FileOpResult
            {
                Ok = true,
                Message = "Already undone.",
                BatchId = batchId,
                Moves = []
            };
        }

        if (newest is null)
        {
            return FileOpResult.Fail("Nothing to undo.");
        }

        return FileOpResult.Fail(
            "Can't undo this reject — another file operation happened after it.");
    }

    private async Task<FileOpResult> UndoKnownBatchAsync(FileOpBatch batch, string projectId)
    {
        // All projects' sources so Undo uses the same ResolveWritableRoot gates as forward ops
        // (refuse another project's nested root / junctions).
        var allSources = await _catalog.GetSourceFoldersAsync().ConfigureAwait(false);
        var restored = new List<FileOpMove>();
        var notes = new List<string>();
        var retryable = false;
        var done = 0;
        using (_suppressWatcher?.Invoke())
        {
            foreach (var item in batch.Items.OrderByDescending(i => i.Seq))
            {
                var note = await UndoStepAsync(item, allSources, projectId, restored).ConfigureAwait(false);
                if (note is null)
                {
                    done++;
                }
                else
                {
                    notes.Add(note.Text);
                    retryable |= note.Retryable;
                }
            }
        }

        var label = UndoLabel(batch);
        if (done == 0 && retryable)
        {
            // Nothing was reversed and the failure may clear (locked file, I/O error): keep the
            // batch on the undo stack so Ctrl+Z can try it again.
            return new FileOpResult
            {
                Ok = false,
                Message = $"Couldn't {label.Replace("Undo ", "undo ", StringComparison.Ordinal)}. {notes[0].TrimEnd('.')}. Try again.",
                Summary = batch.Summary,
                Moves = restored,
                Skipped = notes
            };
        }

        await _catalog.MarkFileOpUndoneAsync(batch.Id).ConfigureAwait(false);
        var message = notes.Count == 0
            ? $"{label.Replace("Undo ", "Undid ", StringComparison.Ordinal)}."
            : $"{label.Replace("Undo ", "Undid ", StringComparison.Ordinal)}, {notes.Count} step(s) skipped. {notes[0]}.";
        return new FileOpResult
        {
            Ok = done > 0,
            Message = message,
            BatchId = batch.Id,
            Summary = batch.Summary,
            Moves = restored,
            Skipped = notes
        };
    }

    /// <summary>Why an undo step was skipped. <see cref="Retryable"/> when nothing changed and trying again later can work (locked file, I/O error).</summary>
    private sealed record UndoNote(string Text, bool Retryable)
    {
        public static UndoNote Skip(string text) => new(text, false);
        public static UndoNote Retry(string text) => new(text, true);
    }

    private async Task<UndoNote?> UndoStepAsync(
        FileOpItem item,
        IReadOnlyList<SourceFolder> allSources,
        string projectId,
        List<FileOpMove> restored)
    {
        switch (item.Step)
        {
            case FileOpStep.MoveFile:
            {
                var oldRoot = ResolveWritableRoot(item.OldPath, allSources, projectId, out var oldError);
                if (oldRoot is null)
                {
                    return UndoNote.Skip($"{Path.GetFileName(item.OldPath)}: {oldError}");
                }

                if (ResolveWritableRoot(item.NewPath, allSources, projectId, out var newError) is null)
                {
                    return UndoNote.Skip($"{Path.GetFileName(item.NewPath)}: {newError}");
                }

                if (!CloudFile.Exists(item.NewPath))
                {
                    // A prior undo attempt may have left the file at OldPath while the catalog still
                    // points at NewPath (catalog update failed and TryMoveBack also failed). Finish
                    // the catalog side so a retry does not burn the journal as "file is gone".
                    if (!string.IsNullOrEmpty(item.AssetId) && CloudFile.Exists(item.OldPath))
                    {
                        try
                        {
                            await _catalog.UpdateAssetLocationAsync(item.AssetId, item.OldPath, item.OldSourceId ?? oldRoot.Id)
                                .ConfigureAwait(false);
                            restored.Add(new FileOpMove(item.AssetId, item.NewPath, item.OldPath, item.OldSourceId ?? oldRoot.Id));
                            return null;
                        }
                        catch (Exception ex)
                        {
                            return UndoNote.Retry($"{Path.GetFileName(item.OldPath)}: {ex.Message}");
                        }
                    }

                    return UndoNote.Skip($"{Path.GetFileName(item.NewPath)}: file is gone");
                }

                var oldDir = Path.GetDirectoryName(item.OldPath)!;
                try
                {
                    Directory.CreateDirectory(oldDir);
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    return UndoNote.Retry($"{Path.GetFileName(item.OldPath)}: {ex.Message}");
                }

                var moved = await MoveOneAsync(item.NewPath, oldDir, reserved: null, preferredName: Path.GetFileName(item.OldPath))
                    .ConfigureAwait(false);
                if (moved.Error is not null)
                {
                    return UndoNote.Retry($"{Path.GetFileName(item.NewPath)}: {moved.Error}");
                }

                if (!string.IsNullOrEmpty(item.AssetId))
                {
                    try
                    {
                        await _catalog.UpdateAssetLocationAsync(item.AssetId, moved.Path!, item.OldSourceId ?? oldRoot.Id)
                            .ConfigureAwait(false);
                    }
                    catch (Exception ex)
                    {
                        if (TryMoveBack(moved.Path!, item.NewPath))
                        {
                            return UndoNote.Retry($"{Path.GetFileName(item.NewPath)}: {ex.Message}");
                        }

                        // Disk is at moved.Path; catalog still NewPath. Stay retryable so the
                        // recovery branch above can reconcile on the next Undo.
                        return UndoNote.Retry($"{Path.GetFileName(item.NewPath)}: {ex.Message} (file is at {moved.Path})");
                    }

                    restored.Add(new FileOpMove(item.AssetId, item.NewPath, moved.Path!, item.OldSourceId ?? oldRoot.Id));
                }

                return null;
            }

            case FileOpStep.CreateDir:
                if (ResolveWritableRoot(item.NewPath, allSources, projectId, out var createError) is null)
                {
                    return UndoNote.Skip($"{DisplayName(item.NewPath)}: {createError}");
                }

                if (!Directory.Exists(item.NewPath))
                {
                    return null;
                }

                if (!IsEmptyFolder(item.NewPath))
                {
                    return UndoNote.Skip($"kept {DisplayName(item.NewPath)} because it isn't empty");
                }

                try
                {
                    Directory.Delete(item.NewPath, recursive: false);
                    return null;
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    return UndoNote.Retry($"{DisplayName(item.NewPath)}: {ex.Message}");
                }

            case FileOpStep.RenameDir:
            {
                if (ResolveWritableRoot(item.OldPath, allSources, projectId, out var renameOldError) is null)
                {
                    return UndoNote.Skip($"{DisplayName(item.OldPath)}: {renameOldError}");
                }

                if (ResolveWritableRoot(item.NewPath, allSources, projectId, out var renameNewError) is null)
                {
                    return UndoNote.Skip($"{DisplayName(item.NewPath)}: {renameNewError}");
                }

                if (!Directory.Exists(item.NewPath))
                {
                    return UndoNote.Skip($"{DisplayName(item.NewPath)}: folder is gone");
                }

                if (!FolderNames.SamePath(item.OldPath, item.NewPath)
                    && (Directory.Exists(item.OldPath) || CloudFile.Exists(item.OldPath)))
                {
                    return UndoNote.Skip($"{DisplayName(item.OldPath)} exists again, so the rename was kept");
                }

                var error = MoveDirectory(item.NewPath, item.OldPath);
                if (error is not null)
                {
                    return UndoNote.Retry($"{DisplayName(item.NewPath)}: {error}");
                }

                try
                {
                    await _catalog.RebaseFolderPathsAsync(item.NewPath, item.OldPath).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    // Keep disk and catalog in step: leave the folder under the renamed name.
                    var back = MoveDirectory(item.OldPath, item.NewPath);
                    return back is null
                        ? UndoNote.Retry($"{DisplayName(item.NewPath)}: {ex.Message}")
                        : UndoNote.Skip($"{DisplayName(item.OldPath)}: renamed back on disk but the catalog couldn't be updated ({ex.Message}); rescan to fix it");
                }

                return null;
            }

            case FileOpStep.DeleteDir:
                if (ResolveWritableRoot(item.OldPath, allSources, projectId, out var deleteError) is null)
                {
                    return UndoNote.Skip($"{DisplayName(item.OldPath)}: {deleteError}");
                }

                try
                {
                    Directory.CreateDirectory(item.OldPath);
                    return null;
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    return UndoNote.Retry($"{DisplayName(item.OldPath)}: {ex.Message}");
                }

            case FileOpStep.AssignTag:
            case FileOpStep.RemoveTag:
            case FileOpStep.DeleteTag:
                return await UndoTagOpStepAsync(item).ConfigureAwait(false);

            default:
                return UndoNote.Skip("unknown step");
        }
    }

    private async Task<UndoNote?> UndoTagOpStepAsync(FileOpItem item)
    {
        switch (item.Step)
        {
            case FileOpStep.AssignTag:
                if (string.IsNullOrEmpty(item.AssetId) || string.IsNullOrEmpty(item.OldPath))
                {
                    return UndoNote.Skip("tag assign: incomplete journal row");
                }

                try
                {
                    await _catalog.RemoveTagForUndoAsync(
                        item.AssetId,
                        TagAssignUndo.TouchesAssignedTag(item.NewSourceId) ? item.OldPath : null,
                        TagAssignUndo.Decode(item.NewSourceId)).ConfigureAwait(false);
                    return null;
                }
                catch (Exception ex)
                {
                    return UndoNote.Retry($"{item.NewPath}: {ex.Message}");
                }

            case FileOpStep.RemoveTag:
                if (string.IsNullOrEmpty(item.AssetId) || string.IsNullOrEmpty(item.OldPath))
                {
                    return UndoNote.Skip("tag remove: incomplete journal row");
                }

                try
                {
                    if (TagAssignUndo.TouchesAssignedTag(item.NewSourceId))
                    {
                        var source = Enum.TryParse<TagSource>(item.OldSourceId, out var parsed)
                            ? parsed
                            : TagSource.Manual;
                        await _catalog.AssignTagForUndoAsync(item.AssetId, item.OldPath, source).ConfigureAwait(false);
                    }

                    foreach (var impliedId in TagAssignUndo.Decode(item.NewSourceId))
                    {
                        await _catalog.AssignTagForUndoAsync(item.AssetId, impliedId, TagSource.Implied).ConfigureAwait(false);
                    }

                    return null;
                }
                catch (Exception ex)
                {
                    return UndoNote.Retry($"{item.NewPath}: {ex.Message}");
                }

            case FileOpStep.DeleteTag:
            {
                var snapshot = CatalogService.DeserializeTagSnapshot(item.NewPath);
                if (snapshot is null)
                {
                    return UndoNote.Skip("tag delete: missing snapshot");
                }

                try
                {
                    await _catalog.RestoreTagSnapshotAsync(snapshot).ConfigureAwait(false);
                    return null;
                }
                catch (DuplicateTagNameException)
                {
                    return UndoNote.Skip($"kept delete of {snapshot.Tag.Name}: a tag with that name exists again");
                }
                catch (Exception ex)
                {
                    return UndoNote.Retry($"{snapshot.Tag.Name}: {ex.Message}");
                }
            }

            default:
                return UndoNote.Skip($"unknown tag step {item.Step}");
        }
    }

    private readonly record struct MoveOutcome(string? Path, string? Error);

    /// <summary>
    /// Move one file into <paramref name="destinationDir"/> without overwriting. Retries with the next
    /// free name when another process grabs the name between the check and the move.
    /// </summary>
    private async Task<MoveOutcome> MoveOneAsync(
        string sourcePath,
        string destinationDir,
        HashSet<string>? reserved,
        string? preferredName = null)
    {
        var name = preferredName ?? Path.GetFileName(sourcePath);
        for (var attempt = 0; attempt < 5; attempt++)
        {
            var taken = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            string dest;
            while (true)
            {
                dest = FolderNames.UniqueFilePath(destinationDir, name, p =>
                    taken.Contains(p)
                    || (reserved?.Contains(p) ?? false)
                    || CloudFile.Exists(p)
                    || Directory.Exists(p));
                // A stale catalog row (orphan) can still own the path; Asset.Path is UNIQUE.
                if (!await _catalog.AssetPathExistsAsync(dest).ConfigureAwait(false))
                {
                    break;
                }

                taken.Add(dest);
            }

            try
            {
                File.Move(sourcePath, dest, overwrite: false);
                return new MoveOutcome(dest, null);
            }
            catch (IOException) when (CloudFile.Exists(dest) && attempt < 4)
            {
                // Lost a race for the name; pick the next one.
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                return new MoveOutcome(null, ex.Message);
            }
        }

        return new MoveOutcome(null, "couldn't find a free name");
    }

    /// <summary>Move a file back after a failed catalog write. False when it is still at <paramref name="from"/>.</summary>
    private static bool TryMoveBack(string from, string to)
    {
        try
        {
            if (CloudFile.Exists(from) && !CloudFile.Exists(to))
            {
                File.Move(from, to, overwrite: false);
                return true;
            }

            return !CloudFile.Exists(from) && CloudFile.Exists(to);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    /// <summary>Directory.Move that also handles a case-only rename. Returns an error message or null.</summary>
    private static string? MoveDirectory(string from, string to)
    {
        try
        {
            if (FolderNames.SamePath(from, to))
            {
                var temp = Path.Combine(Path.GetDirectoryName(from)!, $".palace-rename-{Guid.NewGuid():N}");
                Directory.Move(from, temp);
                try
                {
                    Directory.Move(temp, to);
                }
                catch
                {
                    Directory.Move(temp, from);
                    throw;
                }
            }
            else
            {
                Directory.Move(from, to);
            }

            return null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return ex.Message;
        }
    }

    public const string JournalFailedMessage = "Couldn't record this for Undo, so it was reverted";
    public const string JournalFailedPartialMessage =
        "Couldn't record this for Undo; revert was incomplete — rescan if anything looks wrong";

    /// <summary>
    /// Journal a completed operation. When the journal write fails, the change is reverted with the
    /// Undo steps (every operation stays undoable) and null is returned. If a revert step fails,
    /// <see cref="LastJournalPartial"/> is set so the caller does not claim a full revert.
    /// </summary>
    private async Task<string?> LogAsync(string projectId, FileOpKind kind, string summary, List<FileOpItem> items)
    {
        LastJournalPartial = false;
        try
        {
            return await _catalog.AddFileOpBatchAsync(new FileOpBatch
            {
                ProjectId = projectId,
                Kind = kind,
                Summary = summary,
                Items = items
            }).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            var allSources = await _catalog.GetSourceFoldersAsync().ConfigureAwait(false);
            var notes = new List<string>();
            for (var i = items.Count - 1; i >= 0; i--)
            {
                var note = await UndoStepAsync(items[i], allSources, projectId, []).ConfigureAwait(false);
                if (note is not null)
                {
                    notes.Add(note.Text);
                }
            }

            LastJournalError = notes.Count == 0
                ? ex.Message
                : $"{ex.Message}. {notes[0]}";
            LastJournalPartial = notes.Count > 0;
            return null;
        }
    }

    /// <summary>Why the last journal write failed (for the reverted-operation message).</summary>
    public string? LastJournalError { get; private set; }

    /// <summary>True when a journal-failure revert left some steps on disk / in the catalog.</summary>
    public bool LastJournalPartial { get; private set; }

    private FileOpResult JournalFailed() =>
        FileOpResult.Fail(LastJournalPartial
            ? $"{JournalFailedPartialMessage} ({LastJournalError})."
            : $"{JournalFailedMessage} ({LastJournalError}).");

    private static string Files(int count) => count == 1 ? "1 file" : $"{count} files";

    public static string DisplayName(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return "folder";
        }

        var trimmed = path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var name = Path.GetFileName(trimmed);
        return string.IsNullOrEmpty(name) ? path : name;
    }

    /// <summary>Typographic quotes so folder names don't run into toast sentences.</summary>
    public static string Quote(string name) => $"\u201c{name}\u201d";
}
