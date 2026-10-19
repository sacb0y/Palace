using Palace.Helpers;
using Palace.Models;

namespace Palace.Services;

public sealed class OrganizeChoice
{
    public DestinationPolicy Policy { get; set; }
    public string? DestinationRoot { get; set; }
    public string FolderTemplate { get; set; } = "{Character}/{tags:2}";
    public string FileTemplate { get; set; } = "{Character}-{tags}.{ext}";
    public IReadOnlyList<string>? FolderSegments { get; set; }
    public bool KeepOriginalFileName { get; set; }
}

public sealed class OrganizeService
{
    private readonly CatalogService _catalog;
    private readonly FileOpService _fileOps;

    public OrganizeService(CatalogService catalog, FileOpService fileOps)
    {
        _catalog = catalog;
        _fileOps = fileOps;
    }

    public async Task<IReadOnlyList<OrganizePreviewItem>> DryRunAsync(
        IReadOnlyList<Asset> assets,
        OrganizeChoice choice)
    {
        var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var items = new List<OrganizePreviewItem>();
        foreach (var asset in assets)
        {
            var tags = await _catalog.GetAssignedTagsAsync(asset.Id);
            var source = await _catalog.GetSourceFolderAsync(asset.SourceFolderId);
            var preview = new OrganizePreviewItem
            {
                AssetId = asset.Id,
                FileName = asset.FileName,
                OldPath = asset.Path
            };

            if (asset.IsOrphan || !CloudFile.Exists(asset.Path))
            {
                preview.Status = "Skip";
                preview.Note = "File is missing";
                preview.NewPath = asset.Path;
                items.Add(preview);
                continue;
            }

            try
            {
                var root = OrganizeNames.Root(choice.Policy, choice.DestinationRoot, source?.Path);
                if (string.IsNullOrWhiteSpace(root))
                {
                    preview.Status = "Error";
                    preview.Note = "No destination root";
                    preview.NewPath = asset.Path;
                    items.Add(preview);
                    continue;
                }

                var relativeFolder = choice.FolderSegments is { Count: > 0 }
                    ? Path.Combine(choice.FolderSegments.Select(PathSafe.Slug).Where(s => s.Length > 0).ToArray())
                    : RenderFolder(choice.FolderTemplate, asset, tags);
                var fileName = choice.KeepOriginalFileName
                    ? asset.FileName
                    : RenderFile(choice.FileTemplate, asset, tags);
                var destDir = Path.GetFullPath(Path.Combine(root, relativeFolder));
                var desired = Path.Combine(destDir, fileName);
                if (string.Equals(Path.GetFullPath(asset.Path), Path.GetFullPath(desired), StringComparison.OrdinalIgnoreCase))
                {
                    preview.Status = "Skip";
                    preview.Note = "Already organized";
                    preview.NewPath = asset.Path;
                    items.Add(preview);
                    continue;
                }

                var unique = UniqueAmong(desired, used, asset.Path);
                used.Add(unique);
                preview.NewPath = unique;
                if (!string.Equals(unique, desired, StringComparison.OrdinalIgnoreCase))
                {
                    preview.Status = "Collision";
                    preview.Note = "Will use a unique name";
                }
                else
                {
                    preview.Status = "Ready";
                }
            }
            catch (Exception ex)
            {
                preview.Status = "Error";
                preview.Note = ex.Message;
                preview.NewPath = asset.Path;
            }

            items.Add(preview);
        }

        return items;
    }

    /// <summary>
    /// Apply a dry-run via <see cref="FileOpService"/> so Organize shares the same Undo journal
    /// as Move / folder ops (one batch per run). Does <b>not</b> mutate bound
    /// <see cref="OrganizePreviewItem"/> rows (those fire PropertyChanged onto WinUI) — call
    /// <see cref="MarkPreviewApplied"/> on the UI thread after this returns.
    /// </summary>
    public async Task<FileOpResult> ApplyAsync(string projectId, IReadOnlyList<OrganizePreviewItem> preview)
    {
        var ready = preview.Where(p => p.Status is "Ready" or "Collision").ToList();
        if (ready.Count == 0)
        {
            return FileOpResult.Fail("Nothing to organize.");
        }

        var plan = ready
            .Select(p => new OrganizeApplyItem(p.AssetId, p.FileName, p.OldPath, p.NewPath))
            .ToList();
        // ConfigureAwait(false): file work stays off the UI thread. Never touch ObservableObject
        // preview rows here — that throws RPC_E_WRONG_THREAD when TextBlocks are bound.
        return await _fileOps.ApplyOrganizeAsync(projectId, plan).ConfigureAwait(false);
    }

    /// <summary>
    /// Stamp Applied / NewPath on preview rows after a successful apply. Call on the UI thread
    /// (Library / Tags RelayCommands resume there).
    /// </summary>
    public static void MarkPreviewApplied(IEnumerable<OrganizePreviewItem> preview, FileOpResult result)
    {
        if (!result.Ok || result.Moves.Count == 0)
        {
            return;
        }

        var byId = result.Moves.ToDictionary(m => m.AssetId, StringComparer.Ordinal);
        foreach (var item in preview)
        {
            if (byId.TryGetValue(item.AssetId, out var moved))
            {
                item.NewPath = moved.NewPath;
                item.Status = "Applied";
            }
        }
    }

    public async Task AutoOrganizeNewFileAsync(SourceFolder source, Asset asset)
    {
        var choice = new OrganizeChoice
        {
            Policy = source.DestinationPolicy,
            DestinationRoot = source.DestinationPath,
            FolderTemplate = string.IsNullOrWhiteSpace(source.FolderTemplate) ? "{Character}/{tags:2}" : source.FolderTemplate,
            FileTemplate = string.IsNullOrWhiteSpace(source.FileTemplate) ? "{Character}-{tags}.{ext}" : source.FileTemplate
        };

        var preview = await DryRunAsync([asset], choice);
        var ready = preview.Where(p => p.Status is "Ready" or "Collision").ToList();
        if (ready.Count == 0)
        {
            var err = preview.FirstOrDefault(p => p.Status == "Error");
            if (err is not null)
            {
                await _catalog.SetOrganizeErrorAsync(asset.Id, err.Note);
            }

            return;
        }

        var projectId = source.ProjectId;
        if (string.IsNullOrEmpty(projectId))
        {
            return;
        }

        await ApplyAsync(projectId, ready).ConfigureAwait(false);
    }

    public string RenderFolder(string template, Asset asset, IReadOnlyList<AssignedTag> tags) =>
        OrganizeNames.RenderFolder(template, asset, tags);

    public string RenderFile(string template, Asset asset, IReadOnlyList<AssignedTag> tags) =>
        OrganizeNames.RenderFile(template, asset, tags);

    private static string UniqueAmong(string desired, HashSet<string> used, string currentPath)
    {
        var candidate = desired;
        var dir = Path.GetDirectoryName(desired) ?? "";
        var name = Path.GetFileNameWithoutExtension(desired);
        var ext = Path.GetExtension(desired);
        if (string.IsNullOrWhiteSpace(name))
        {
            name = "untitled";
            candidate = Path.Combine(dir, name + ext);
        }

        var n = 2;
        while ((CloudFile.Exists(candidate) && !string.Equals(candidate, currentPath, StringComparison.OrdinalIgnoreCase))
               || used.Contains(candidate))
        {
            candidate = Path.Combine(dir, $"{name} ({n}){ext}");
            n++;
        }

        return candidate;
    }
}
