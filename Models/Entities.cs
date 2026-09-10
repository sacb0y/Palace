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
}

public sealed class PromptSuggestion
{
    public string Token { get; set; } = "";
    public string? ExistingTagId { get; set; }
    public string? ExistingTagName { get; set; }
    public bool IsKnown => ExistingTagId is not null;
}

public sealed class ScanReport
{
    public int Added { get; set; }
    public int Updated { get; set; }
    public int Orphaned { get; set; }
    public int Errors { get; set; }
}
