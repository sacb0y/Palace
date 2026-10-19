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
public sealed class FileOpService
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
            FileOpKind.CreateFolder => $"Undo new folder {DisplayName(batch.Items.LastOrDefault()?.NewPath)}",
            FileOpKind.RenameFolder => $"Undo rename to {DisplayName(batch.Items.LastOrDefault()?.NewPath)}",
            FileOpKind.DeleteFolder => $"Undo delete of {DisplayName(batch.Items.LastOrDefault()?.OldPath)}",
            _ => "Undo"
        };

    public Task<FileOpBatch?> PeekUndoAsync(string projectId) =>
        _catalog.GetLastUndoableFileOpAsync(projectId);

    /// <summary>
    /// Move assets into <paramref name="destinationDir"/> (an existing folder inside a watched source).
    /// Name clashes get a unique name; missing / online-only-across-roots / already-there files are skipped.
    /// </summary>
    public async Task<FileOpResult> MoveAssetsAsync(
        IReadOnlyList<Asset> assets,
        string destinationDir,
        string projectId,
        CancellationToken cancellationToken = default)
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
                    // Keep disk and catalog in step: put the file back.
                    TryMoveBack(moved.Path!, asset.Path);
                    skipped.Add($"{asset.FileName}: {ex.Message}");
                    continue;
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

        var summary = MoveSummary(moves.Count, destinationDir);
        var batchId = await LogAsync(projectId, FileOpKind.Move, summary, items).ConfigureAwait(false);
        var message = skipped.Count == 0
            ? $"Moved {Files(moves.Count)} to {DisplayName(destinationDir)}."
            : $"Moved {Files(moves.Count)} to {DisplayName(destinationDir)}. Skipped {skipped.Count}.";
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
    public async Task<FileOpResult> CreateFolderAsync(string parentDir, string name, string projectId)
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
        return new FileOpResult { Ok = true, Message = $"Created {cleaned}.", BatchId = batchId, Summary = summary, Path = path };
    }

    /// <summary>Rename a folder inside a watched source (not a source root) and rebase its catalog rows.</summary>
    public async Task<FileOpResult> RenameFolderAsync(string folderPath, string newName, string projectId)
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
        return new FileOpResult { Ok = true, Message = $"Renamed to {cleaned}.", BatchId = batchId, Summary = summary, Path = target };
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
    public async Task<FileOpResult> DeleteEmptyFolderAsync(string folderPath, string projectId)
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
        return new FileOpResult { Ok = true, Message = $"Deleted {DisplayName(folderPath)}.", BatchId = batchId, Summary = summary, Path = folderPath };
    }

    /// <summary>
    /// Reverse the newest file operation of the project: move files back (unique name if the old
    /// spot is taken), rename folders back, remove created folders that are still empty, recreate
    /// deleted ones. Best effort per step; the batch is marked undone afterwards.
    /// </summary>
    public async Task<FileOpResult> UndoLastAsync(string projectId)
    {
        var batch = await _catalog.GetLastUndoableFileOpAsync(projectId).ConfigureAwait(false);
        if (batch is null)
        {
            return FileOpResult.Fail("Nothing to undo.");
        }

        var sources = await _catalog.GetSourceFoldersAsync(projectId).ConfigureAwait(false);
        var restored = new List<FileOpMove>();
        var notes = new List<string>();
        var done = 0;
        using (_suppressWatcher?.Invoke())
        {
            foreach (var item in batch.Items.OrderByDescending(i => i.Seq))
            {
                var note = await UndoStepAsync(item, sources, restored).ConfigureAwait(false);
                if (note is null)
                {
                    done++;
                }
                else
                {
                    notes.Add(note);
                }
            }
        }

        await _catalog.MarkFileOpUndoneAsync(batch.Id).ConfigureAwait(false);
        var label = UndoLabel(batch);
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

    private async Task<string?> UndoStepAsync(FileOpItem item, IReadOnlyList<SourceFolder> sources, List<FileOpMove> restored)
    {
        switch (item.Step)
        {
            case FileOpStep.MoveFile:
            {
                var oldRoot = ResolveRoot(item.OldPath, sources);
                if (oldRoot is null || ResolveRoot(item.NewPath, sources) is null)
                {
                    return $"{Path.GetFileName(item.OldPath)}: no longer in a Library folder";
                }

                if (!CloudFile.Exists(item.NewPath))
                {
                    return $"{Path.GetFileName(item.NewPath)}: file is gone";
                }

                var oldDir = Path.GetDirectoryName(item.OldPath)!;
                if (LinkedFolderBetween(oldRoot.Path, oldDir) is not null)
                {
                    return $"{Path.GetFileName(item.OldPath)}: its old folder is now a shortcut (junction or symlink)";
                }

                try
                {
                    Directory.CreateDirectory(oldDir);
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    return $"{Path.GetFileName(item.OldPath)}: {ex.Message}";
                }

                var moved = await MoveOneAsync(item.NewPath, oldDir, reserved: null, preferredName: Path.GetFileName(item.OldPath))
                    .ConfigureAwait(false);
                if (moved.Error is not null)
                {
                    return $"{Path.GetFileName(item.NewPath)}: {moved.Error}";
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
                        TryMoveBack(moved.Path!, item.NewPath);
                        return $"{Path.GetFileName(item.NewPath)}: {ex.Message}";
                    }

                    restored.Add(new FileOpMove(item.AssetId, item.NewPath, moved.Path!, item.OldSourceId ?? oldRoot.Id));
                }

                return null;
            }

            case FileOpStep.CreateDir:
                if (ResolveRoot(item.NewPath, sources) is null)
                {
                    return $"{DisplayName(item.NewPath)}: no longer in a Library folder";
                }

                if (!Directory.Exists(item.NewPath))
                {
                    return null;
                }

                if (!IsEmptyFolder(item.NewPath))
                {
                    return $"kept {DisplayName(item.NewPath)} because it isn't empty";
                }

                try
                {
                    Directory.Delete(item.NewPath, recursive: false);
                    return null;
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    return $"{DisplayName(item.NewPath)}: {ex.Message}";
                }

            case FileOpStep.RenameDir:
            {
                if (ResolveRoot(item.OldPath, sources) is null || ResolveRoot(item.NewPath, sources) is null)
                {
                    return $"{DisplayName(item.NewPath)}: no longer in a Library folder";
                }

                if (!Directory.Exists(item.NewPath))
                {
                    return $"{DisplayName(item.NewPath)}: folder is gone";
                }

                if (!FolderNames.SamePath(item.OldPath, item.NewPath)
                    && (Directory.Exists(item.OldPath) || CloudFile.Exists(item.OldPath)))
                {
                    return $"{DisplayName(item.OldPath)} exists again, so the rename was kept";
                }

                var error = MoveDirectory(item.NewPath, item.OldPath);
                if (error is not null)
                {
                    return $"{DisplayName(item.NewPath)}: {error}";
                }

                await _catalog.RebaseFolderPathsAsync(item.NewPath, item.OldPath).ConfigureAwait(false);
                return null;
            }

            case FileOpStep.DeleteDir:
                if (ResolveRoot(item.OldPath, sources) is null)
                {
                    return $"{DisplayName(item.OldPath)}: no longer in a Library folder";
                }

                try
                {
                    Directory.CreateDirectory(item.OldPath);
                    return null;
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    return $"{DisplayName(item.OldPath)}: {ex.Message}";
                }

            default:
                return "unknown step";
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

    private static void TryMoveBack(string from, string to)
    {
        try
        {
            if (CloudFile.Exists(from) && !CloudFile.Exists(to))
            {
                File.Move(from, to, overwrite: false);
            }
        }
        catch
        {
            // Best effort; the catalog row still points at the old path and a rescan repairs it.
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

    private Task<string> LogAsync(string projectId, FileOpKind kind, string summary, List<FileOpItem> items) =>
        _catalog.AddFileOpBatchAsync(new FileOpBatch
        {
            ProjectId = projectId,
            Kind = kind,
            Summary = summary,
            Items = items
        });

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
}
