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
}

public sealed class OrganizeService
{
    private readonly CatalogService _catalog;
    private static readonly Regex Token = new(@"\{([^}]+)\}|%([^%]+)%", RegexOptions.Compiled);

    public OrganizeService(CatalogService catalog)
    {
        _catalog = catalog;
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

            if (asset.IsOrphan || !File.Exists(asset.Path))
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

                var relativeFolder = RenderFolder(choice.FolderTemplate, asset, tags);
                var fileName = RenderFile(choice.FileTemplate, asset, tags);
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

    public async Task<int> ApplyAsync(IReadOnlyList<OrganizePreviewItem> preview)
    {
        var applied = new List<OrganizeBatchItem>();
        foreach (var item in preview.Where(p => p.Status is "Ready" or "Collision"))
        {
            try
            {
                var destDir = Path.GetDirectoryName(item.NewPath);
                if (!string.IsNullOrEmpty(destDir))
                {
                    Directory.CreateDirectory(destDir);
                }

                if (File.Exists(item.NewPath))
                {
                    item.NewPath = PathSafe.UniquePath(item.NewPath);
                }

                File.Move(item.OldPath, item.NewPath);
                await _catalog.UpdateAssetPathAsync(item.AssetId, item.NewPath);
                await _catalog.SetOrganizeErrorAsync(item.AssetId, null);
                item.Status = "Applied";
                applied.Add(new OrganizeBatchItem
                {
                    AssetId = item.AssetId,
                    OldPath = item.OldPath,
                    NewPath = item.NewPath,
                    Status = OrganizeItemStatus.Applied
                });
            }
            catch (Exception ex)
            {
                item.Status = "Error";
                item.Note = ex.Message;
                await _catalog.SetOrganizeErrorAsync(item.AssetId, ex.Message);
            }
        }

        if (applied.Count > 0)
        {
            await _catalog.CreateOrganizeBatchAsync(applied);
        }

        return applied.Count;
    }

    public async Task<int> UndoLastAsync()
    {
        var batch = await _catalog.GetLastUndoableBatchAsync();
        if (batch is null)
        {
            return 0;
        }

        var items = await _catalog.GetBatchItemsAsync(batch.Id);
        var undone = 0;
        foreach (var item in items.Where(i => i.Status == OrganizeItemStatus.Applied))
        {
            try
            {
                if (File.Exists(item.NewPath))
                {
                    var destDir = Path.GetDirectoryName(item.OldPath);
                    if (!string.IsNullOrEmpty(destDir))
                    {
                        Directory.CreateDirectory(destDir);
                    }

                    var restore = File.Exists(item.OldPath) ? PathSafe.UniquePath(item.OldPath) : item.OldPath;
                    File.Move(item.NewPath, restore);
                    await _catalog.UpdateAssetPathAsync(item.AssetId, restore);
                }

                undone++;
            }
            catch
            {
                // Keep remaining items undoable? Mark batch undone anyway after best-effort.
            }
        }

        await _catalog.MarkBatchUndoneAsync(batch.Id);
        return undone;
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

        await ApplyAsync(ready);
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

        if (key.StartsWith("facet:", StringComparison.OrdinalIgnoreCase))
        {
            var facetName = key["facet:".Length..];
            var hit = tags.FirstOrDefault(t => t.FacetName.Equals(facetName, StringComparison.OrdinalIgnoreCase));
            return hit is null ? "" : hit.FacetName;
        }

        var byFacet = tags.FirstOrDefault(t => t.FacetName.Equals(key, StringComparison.OrdinalIgnoreCase));
        if (byFacet is not null)
        {
            used.Add(byFacet.TagId);
            return byFacet.TagName;
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
        while ((File.Exists(candidate) && !string.Equals(candidate, currentPath, StringComparison.OrdinalIgnoreCase))
               || used.Contains(candidate))
        {
            candidate = Path.Combine(dir, $"{name} ({n}){ext}");
            n++;
        }

        return candidate;
    }
}
