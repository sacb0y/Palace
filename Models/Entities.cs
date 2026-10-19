namespace Palace.Models;

public sealed class SourceFolder
{
    public string Id { get; set; } = "";
    public string Path { get; set; } = "";
    public string? AccessToken { get; set; }
    public bool AutoOrganizeNewFiles { get; set; }
    public string? FolderTemplate { get; set; }
    public string? FileTemplate { get; set; }
    public DestinationPolicy DestinationPolicy { get; set; } = DestinationPolicy.InSource;
    public string? DestinationPath { get; set; }
    public string? ProjectId { get; set; }
    public SourceKind Kind { get; set; } = SourceKind.Local;
    public string? CloudAccountId { get; set; }
    public string? CloudRootItemId { get; set; }

    /// <summary>Screen readers (UIA) name list / picker items by ToString(); never the type name.</summary>
    public override string ToString() => Path;
}

public sealed class Asset
{
    public long RowId { get; set; }
    public string Id { get; set; } = "";
    public string SourceFolderId { get; set; } = "";
    public string Path { get; set; } = "";
    public string FileName { get; set; } = "";
    public string? ContentHash { get; set; }
    public AssetKind Kind { get; set; } = AssetKind.Other;
    public int? Width { get; set; }
    public int? Height { get; set; }
    public int? DurationMs { get; set; }
    public bool IsOrphan { get; set; }
    public string? Model { get; set; }
    public string? Seed { get; set; }
    public string? Prompt { get; set; }
    public string? NegativePrompt { get; set; }
    public string? MetadataJson { get; set; }
    public string? OrganizeError { get; set; }
    public int? Rating { get; set; }
    public string? Notes { get; set; }
    public string DateAdded { get; set; } = "";
    public string? DateModified { get; set; }
    public long? FileSize { get; set; }
    public bool IsOnlineOnly { get; set; }
    public string? CloudItemId { get; set; }
    /// <summary>Header probe from scan/hydrate — mosaic must not Open the original.</summary>
    public bool IsHdr { get; set; }
    /// <summary><c>ScanFastPath.IndexVersion</c> of the last full local index; 0 = never / older build.</summary>
    public int IndexVersion { get; set; }

    /// <summary>EXIF DateTimeOriginal as local "yyyy-MM-ddTHH:mm:ss" (no zone); null when the file has none.</summary>
    public string? DateTaken { get; set; }
    /// <summary>
    /// The last full local index could not decode the content (placeholder tile). Lets an
    /// incremental rescan, which skips unchanged files, still list them in the scan notice.
    /// </summary>
    public bool Undecodable { get; set; }
}

public sealed class CloudAccount
{
    public string Id { get; set; } = "";
    public CloudProvider Provider { get; set; }
    public string AccountId { get; set; } = "";
    public string? DisplayName { get; set; }
    public string VaultKey { get; set; } = "";
}

public sealed class Tag
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public int Priority { get; set; }
    public string Slug { get; set; } = "";
    public string? Color { get; set; }
    public bool IsStarred { get; set; }

    /// <summary>Screen readers (UIA) name list / picker items by ToString(); never the type name.</summary>
    public override string ToString() => Name;
}

public sealed class TagCreateBatchResult
{
    public int Created { get; init; }
    public int Existed { get; init; }
    public int MembershipRejected { get; init; }
    public IReadOnlyList<Tag> Tags { get; init; } = [];
}

public sealed class TagMembership
{
    public string ParentId { get; set; } = "";
    public string ChildId { get; set; } = "";
}

public sealed class TagImplication
{
    public string TagId { get; set; } = "";
    public string ImpliedTagId { get; set; } = "";
}

public sealed class TagPath
{
    public IReadOnlyList<Tag> Nodes { get; init; } = [];

    public string Display => string.Join(" / ", Nodes.Select(n => n.Name));

    public IReadOnlyList<string> Slugs => Nodes.Select(n => n.Slug).ToList();
}

public sealed class AssetTag
{
    public string AssetId { get; set; } = "";
    public string TagId { get; set; } = "";
    public TagSource Source { get; set; } = TagSource.Manual;
}

public sealed class AssignedTag
{
    public string TagId { get; set; } = "";
    public string TagName { get; set; } = "";
    public IReadOnlyList<string> ParentNames { get; set; } = [];
    public int TagPriority { get; set; }
    public TagSource Source { get; set; }
    public string Slug { get; set; } = "";
    public string? EffectiveColor { get; set; }
}

public sealed class OrganizeRule
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string FolderTemplate { get; set; } = "";
    public string FileTemplate { get; set; } = "";
    public string? FacetIdsJson { get; set; }
}

public sealed class OrganizeBatch
{
    public string Id { get; set; } = "";
    public string CreatedAt { get; set; } = "";
    public bool IsUndone { get; set; }
}

public sealed class OrganizeBatchItem
{
    public string Id { get; set; } = "";
    public string BatchId { get; set; } = "";
    public string AssetId { get; set; } = "";
    public string OldPath { get; set; } = "";
    public string NewPath { get; set; } = "";
    public OrganizeItemStatus Status { get; set; }
    public string? Error { get; set; }
}

public sealed class Room
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string Kind { get; set; } = "Room";
    public string? Icon { get; set; }
    public int SortOrder { get; set; }
    public string? ProjectId { get; set; }

    /// <summary>Screen readers (UIA) name list / picker items by ToString(); never the type name.</summary>
    public override string ToString() => Name;
}

public sealed class RoomItem
{
    public string CollectionId { get; set; } = "";
    public string AssetId { get; set; } = "";
    public string? Section { get; set; }
    public int SortOrder { get; set; }
}

public sealed class Project
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";

    /// <summary>Screen readers (UIA) name list / picker items by ToString(); never the type name.</summary>
    public override string ToString() => Name;
}

public sealed class PromptSuggestion
{
    public string Token { get; set; } = "";
    public string? ExistingTagId { get; set; }
    public string? ExistingTagName { get; set; }
    public bool IsKnown => ExistingTagId is not null;

    /// <summary>Screen readers (UIA) name list / picker items by ToString(); never the type name.</summary>
    public override string ToString() => ExistingTagName ?? Token;
}

/// <summary>What indexing one file did to the catalog.</summary>
public enum ScanOutcome
{
    /// <summary>New catalog row.</summary>
    Added,
    /// <summary>Existing row re-indexed (changed file, stale row or stale thumbnail).</summary>
    Updated,
    /// <summary>Nothing changed since the last index; the row was left as it was.</summary>
    Unchanged
}

public sealed class ScanReport
{
    /// <summary>Per-category cap on remembered paths; counts are always exact.</summary>
    public const int MaxListed = 20;

    public int Added { get; set; }
    public int Updated { get; set; }
    public int Unchanged { get; set; }
    public int Orphaned { get; set; }
    public int Errors { get; set; }

    /// <summary>Files left out because Palace does not catalog their format (see <c>PathSafe.IsCatalogExt</c>).</summary>
    public int Skipped { get; set; }

    /// <summary>Skipped files by lower-case extension (<c>""</c> = no extension).</summary>
    public Dictionary<string, int> SkippedByExtension { get; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Indexed, but the content could not be decoded (placeholder tile).</summary>
    public int Undecodable { get; set; }
    public List<string> UndecodablePaths { get; } = [];

    /// <summary>Files that threw while indexing (also counted in <see cref="Errors"/>).</summary>
    public List<string> ErrorPaths { get; } = [];
    public List<string> ErrorReasons { get; } = [];

    /// <summary>Folders that could not be opened (also counted in <see cref="Errors"/>).</summary>
    public int InaccessibleFolders { get; set; }

    public void NoteIndexed(ScanOutcome outcome)
    {
        switch (outcome)
        {
            case ScanOutcome.Added: Added++; break;
            case ScanOutcome.Updated: Updated++; break;
            default: Unchanged++; break;
        }
    }

    public void NoteSkipped(string path)
    {
        Skipped++;
        var ext = Path.GetExtension(path).ToLowerInvariant();
        SkippedByExtension[ext] = SkippedByExtension.GetValueOrDefault(ext) + 1;
    }

    public void NoteUndecodable(string path)
    {
        Undecodable++;
        if (UndecodablePaths.Count < MaxListed)
        {
            UndecodablePaths.Add(path);
        }
    }

    public void NoteError(string path, Exception exception)
    {
        Errors++;
        if (ErrorPaths.Count < MaxListed)
        {
            ErrorPaths.Add(path);
            ErrorReasons.Add(exception.Message);
        }
    }

    public void NoteInaccessibleFolders(int count)
    {
        InaccessibleFolders += count;
        Errors += count;
    }

    public void Add(ScanReport other)
    {
        Added += other.Added;
        Updated += other.Updated;
        Unchanged += other.Unchanged;
        Orphaned += other.Orphaned;
        Errors += other.Errors;
        Skipped += other.Skipped;
        foreach (var (ext, count) in other.SkippedByExtension)
        {
            SkippedByExtension[ext] = SkippedByExtension.GetValueOrDefault(ext) + count;
        }

        Undecodable += other.Undecodable;
        UndecodablePaths.AddRange(other.UndecodablePaths.Take(MaxListed - UndecodablePaths.Count));
        var room = MaxListed - ErrorPaths.Count;
        ErrorPaths.AddRange(other.ErrorPaths.Take(room));
        ErrorReasons.AddRange(other.ErrorReasons.Take(room));
        InaccessibleFolders += other.InaccessibleFolders;
    }
}
