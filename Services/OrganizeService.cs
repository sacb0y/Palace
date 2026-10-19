using System.Text.RegularExpressions;
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
    private static readonly Regex Token = new(@"\{([^}]+)\}|%([^%]+)%", RegexOptions.Compiled);

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
                var root = choice.Policy == DestinationPolicy.Destination
                    ? choice.DestinationRoot
                    : source?.Path;
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
    /// as Move / folder ops (one batch per run). Returns how many files moved.
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
        var result = await _fileOps.ApplyOrganizeAsync(projectId, plan).ConfigureAwait(false);
        if (result.Ok)
        {
            foreach (var item in ready)
            {
                var moved = result.Moves.FirstOrDefault(m => m.AssetId == item.AssetId);
                if (!string.IsNullOrEmpty(moved.AssetId))
                {
                    item.NewPath = moved.NewPath;
                    item.Status = "Applied";
                }
            }
        }

        return result;
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

    public string RenderFolder(string template, Asset asset, IReadOnlyList<AssignedTag> tags)
    {
        var rendered = ReplaceTokens(template, asset, tags, forFolder: true);
        var parts = rendered.Split(['/', '\\'], StringSplitOptions.RemoveEmptyEntries)
            .Select(PathSafe.StripIllegal)
            .Where(p => p.Length > 0);
        return Path.Combine(parts.ToArray());
    }

    public string RenderFile(string template, Asset asset, IReadOnlyList<AssignedTag> tags)
    {
        var ext = Path.GetExtension(asset.FileName);
        if (string.IsNullOrEmpty(ext))
        {
            ext = Path.GetExtension(asset.Path);
        }

        var rendered = ReplaceTokens(template, asset, tags, forFolder: false);
        rendered = PathSafe.StripIllegal(rendered);
        rendered = PathSafe.CollapseDashes(rendered);
        if (string.IsNullOrWhiteSpace(rendered))
        {
            rendered = Path.GetFileNameWithoutExtension(asset.FileName);
        }

        if (!rendered.EndsWith(ext, StringComparison.OrdinalIgnoreCase))
        {
            rendered += ext;
        }

        return rendered;
    }

    private static string ReplaceTokens(string template, Asset asset, IReadOnlyList<AssignedTag> tags, bool forFolder)
    {
        var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        return Token.Replace(template, match =>
        {
            var raw = match.Groups[1].Success ? match.Groups[1].Value : match.Groups[2].Value;
            var value = ResolveToken(raw, asset, tags, used);
            if (forFolder && string.IsNullOrWhiteSpace(value))
            {
                return "";
            }

            return value ?? "";
        });
    }

    private static string? ResolveToken(string raw, Asset asset, IReadOnlyList<AssignedTag> tags, HashSet<string> used)
    {
        var key = raw.Trim();
        if (key.Equals("original", StringComparison.OrdinalIgnoreCase))
        {
            return Path.GetFileNameWithoutExtension(asset.FileName);
        }

        if (key.Equals("ext", StringComparison.OrdinalIgnoreCase))
        {
            var ext = Path.GetExtension(asset.FileName);
            return ext.StartsWith('.') ? ext[1..] : ext;
        }

        if (key.Equals("model", StringComparison.OrdinalIgnoreCase))
        {
            return PathSafe.StripIllegal(asset.Model ?? "");
        }

        if (key.Equals("seed", StringComparison.OrdinalIgnoreCase))
        {
            return asset.Seed ?? "";
        }

        if (key.StartsWith("tags", StringComparison.OrdinalIgnoreCase))
        {
            var take = int.MaxValue;
            var colon = key.IndexOf(':');
            if (colon >= 0 && int.TryParse(key[(colon + 1)..], out var n))
            {
                take = n;
            }

            var remaining = tags.Where(t => !used.Contains(t.TagId)).Take(take).ToList();
            foreach (var t in remaining)
            {
                used.Add(t.TagId);
            }

            return string.Join(", ", remaining.Select(t => t.TagName));
        }

        if (key.StartsWith("top:", StringComparison.OrdinalIgnoreCase) &&
            int.TryParse(key[4..], out var topN))
        {
            var top = tags.Take(topN).ToList();
            foreach (var t in top)
            {
                used.Add(t.TagId);
            }

            return string.Join("/", top.Select(t => t.Slug));
        }

        if (key.StartsWith("facet:", StringComparison.OrdinalIgnoreCase)
            || key.StartsWith("group:", StringComparison.OrdinalIgnoreCase))
        {
            var groupName = key[(key.IndexOf(':') + 1)..];
            var hit = tags.FirstOrDefault(t =>
                t.ParentNames.Any(p => p.Equals(groupName, StringComparison.OrdinalIgnoreCase)));
            return hit?.TagName ?? "";
        }

        var byGroup = tags.FirstOrDefault(t =>
            t.ParentNames.Any(p => p.Equals(key, StringComparison.OrdinalIgnoreCase)));
        if (byGroup is not null)
        {
            used.Add(byGroup.TagId);
            return byGroup.TagName;
        }

        var byTag = tags.FirstOrDefault(t => t.TagName.Equals(key, StringComparison.OrdinalIgnoreCase));
        if (byTag is not null)
        {
            used.Add(byTag.TagId);
            return byTag.TagName;
        }

        return "";
    }

    private static string UniqueAmong(string desired, HashSet<string> used, string currentPath)
    {
        var candidate = desired;
        var dir = Path.GetDirectoryName(desired) ?? "";
        var name = Path.GetFileNameWithoutExtension(desired);
        var ext = Path.GetExtension(desired);
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
