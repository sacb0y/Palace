using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.UI.Xaml.Media;

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

    public string? ContentHash { get; set; }

    [ObservableProperty]
    public partial string? ThumbPath { get; set; }

    [ObservableProperty]
    public partial ImageSource? ThumbImage { get; set; }

    public bool ThumbLoadStarted { get; set; }

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

    [ObservableProperty]
    public partial bool IsSelected { get; set; }

    public int? Width { get; set; }

    public int? Height { get; set; }

    [ObservableProperty]
    public partial bool IsOnlineOnly { get; set; }

    [ObservableProperty]
    public partial string? CloudItemId { get; set; }

    public bool HasThumbnail => !string.IsNullOrEmpty(ThumbPath);

    public bool ShowCloudTile => IsOnlineOnly && !HasThumbnail;

    partial void OnIsOnlineOnlyChanged(bool value) => OnPropertyChanged(nameof(ShowCloudTile));

    partial void OnThumbPathChanged(string? value)
    {
        ThumbLoadStarted = false;
        OnPropertyChanged(nameof(HasThumbnail));
        OnPropertyChanged(nameof(ShowCloudTile));
    }

    public double AspectRatio
    {
        get
        {
            if (Width is > 0 && Height is > 0)
            {
                return (double)Width.Value / Height.Value;
            }

            return Kind == AssetKind.Video ? 16.0 / 9.0 : 1.0;
        }
    }

    public double AspectHintWidth => Math.Clamp(AspectRatio, 0.15, 6.0) * 100;
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

    [ObservableProperty]
    public partial string? EffectiveColor { get; set; }

    [ObservableProperty]
    public partial string CountLabel { get; set; } = "";

    [ObservableProperty]
    public partial bool IsPartial { get; set; }

    public string RemoveAutomationName => $"Remove {TagName}";

    public string RemoveAutomationId =>
        "BtnRemoveTag_" + string.Concat((TagName ?? "").Where(char.IsLetterOrDigit));
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

    [ObservableProperty]
    public partial string Kind { get; set; } = nameof(Models.SourceKind.Local);

    public string? CloudAccountId { get; set; }

    public string? CloudRootItemId { get; set; }
}

public partial class RoomSection : ObservableObject
{
    public string Title { get; set; } = "Pins";
    public ObservableCollection<AssetItem> Items { get; } = [];
}

public sealed class TagPickItem
{
    public string TagId { get; set; } = "";
    public string Name { get; set; } = "";
    public string Display { get; set; } = "";
    public string? EffectiveColor { get; set; }
}

public partial class TagChipItem : ObservableObject
{
    public string TagId { get; set; } = "";

    [ObservableProperty]
    public partial string Name { get; set; } = "";

    [ObservableProperty]
    public partial string? EffectiveColor { get; set; }

    [ObservableProperty]
    public partial bool IsStarred { get; set; }

    [ObservableProperty]
    public partial bool IsAssigned { get; set; }

    [ObservableProperty]
    public partial bool IsPartial { get; set; }

    [ObservableProperty]
    public partial bool IsFilterSelected { get; set; }

    public string? ParentGroupId { get; set; }

    public string AutomationPrefix { get; set; } = "BtnTagChip_";

    public string AutomationId =>
        AutomationPrefix + string.Concat((Name ?? "").Where(char.IsLetterOrDigit));
}

public partial class TagBoardGroup : ObservableObject
{
    public string? GroupId { get; set; }

    [ObservableProperty]
    public partial string Name { get; set; } = "";

    [ObservableProperty]
    public partial string? Color { get; set; }

    public bool IsUngrouped { get; set; }

    [ObservableProperty]
    public partial bool IsExpanded { get; set; } = true;

    public string AutomationId =>
        "BtnTagGroup_" + string.Concat((Name ?? "").Where(char.IsLetterOrDigit));

    public ObservableCollection<TagChipItem> Chips { get; } = [];
}

public partial class TagTreeNode : ObservableObject
{
    public string? TagId { get; set; }
    public bool IsUngroupedBucket { get; set; }

    [ObservableProperty]
    public partial string Name { get; set; } = "";

    [ObservableProperty]
    public partial string? EffectiveColor { get; set; }

    [ObservableProperty]
    public partial string ColorSourceLabel { get; set; } = "";

    [ObservableProperty]
    public partial bool ColorIsCustom { get; set; }

    [ObservableProperty]
    public partial bool IsFilterSelected { get; set; }

    [ObservableProperty]
    public partial bool IsStarred { get; set; }

    public ObservableCollection<TagTreeNode> Children { get; } = [];
}

public sealed class TagGroupPick
{
    public string TagId { get; set; } = "";
    public string Name { get; set; } = "";
}
