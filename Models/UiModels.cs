using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Palace.Models;

public sealed class FolderNode
{
    public string Name { get; set; } = "";
    public string Path { get; set; } = "";
    public string? SourceFolderId { get; set; }
    public ObservableCollection<FolderNode> Children { get; } = [];
}

public sealed class PathCrumb
{
    public string Name { get; set; } = "";
    public string Path { get; set; } = "";
}

public partial class AssetItem : ObservableObject
{
    public string Id { get; set; } = "";
    public string SourceFolderId { get; set; } = "";

    [ObservableProperty]
    public partial string FileName { get; set; } = "";

    [ObservableProperty]
    public partial string Path { get; set; } = "";

    [ObservableProperty]
    public partial string? ThumbPath { get; set; }

    [ObservableProperty]
    public partial AssetKind Kind { get; set; }

    [ObservableProperty]
    public partial bool IsOrphan { get; set; }

    [ObservableProperty]
    public partial string? Model { get; set; }

    [ObservableProperty]
    public partial string? Prompt { get; set; }

    [ObservableProperty]
    public partial string? OrganizeError { get; set; }

    public bool HasThumbnail => !string.IsNullOrEmpty(ThumbPath) && File.Exists(ThumbPath);
}

public partial class AssignedTagItem : ObservableObject
{
    public string TagId { get; set; } = "";

    [ObservableProperty]
    public partial string TagName { get; set; } = "";

    [ObservableProperty]
    public partial string Display { get; set; } = "";

    [ObservableProperty]
    public partial string SourceLabel { get; set; } = "";

    [ObservableProperty]
    public partial TagSource Source { get; set; }
}

public partial class OrganizePreviewItem : ObservableObject
{
    public string AssetId { get; set; } = "";

    [ObservableProperty]
    public partial string FileName { get; set; } = "";

    [ObservableProperty]
    public partial string OldPath { get; set; } = "";

    [ObservableProperty]
    public partial string NewPath { get; set; } = "";

    [ObservableProperty]
    public partial string Status { get; set; } = "";

    [ObservableProperty]
    public partial string Note { get; set; } = "";
}

public partial class SourceFolderItem : ObservableObject
{
    public string Id { get; set; } = "";

    [ObservableProperty]
    public partial string Path { get; set; } = "";

    [ObservableProperty]
    public partial bool AutoOrganizeNewFiles { get; set; }

    [ObservableProperty]
    public partial string FolderTemplate { get; set; } = "{Character}/{tags:2}";

    [ObservableProperty]
    public partial string FileTemplate { get; set; } = "{Character}-{tags}.{ext}";

    [ObservableProperty]
    public partial string DestinationPolicy { get; set; } = nameof(Models.DestinationPolicy.InSource);

    [ObservableProperty]
    public partial string? DestinationPath { get; set; }
}

public partial class RoomSection : ObservableObject
{
    public string Title { get; set; } = "Pins";
    public ObservableCollection<AssetItem> Items { get; } = [];
}

public sealed class TagPickItem
{
    public string TagId { get; set; } = "";
    public string Display { get; set; } = "";
}

public partial class TagTreeNode : ObservableObject
{
    public string? TagId { get; set; }
    public bool IsUngroupedBucket { get; set; }

    [ObservableProperty]
    public partial string Name { get; set; } = "";

    public ObservableCollection<TagTreeNode> Children { get; } = [];
}

public sealed class TagGroupPick
{
    public string TagId { get; set; } = "";
    public string Name { get; set; } = "";
}
