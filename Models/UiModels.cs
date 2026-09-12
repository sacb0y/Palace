using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.UI.Xaml.Media;
using Palace.Helpers;

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
    public bool IsFolderHeader { get; set; }
    public string FolderGroupTitle { get; set; } = "";
    public string FolderGroupPath { get; set; } = "";

    public string MosaicTag => FolderGroups.MosaicTag(IsFolderHeader, Id, FolderGroupPath);

    public string FolderGroupAutomationId =>
        "TxtFolderGroup_" + string.Concat((FolderGroupTitle ?? "").Where(char.IsLetterOrDigit));

    public string MosaicItemName =>
        IsFolderHeader
            ? (string.IsNullOrEmpty(FolderGroupTitle) ? FileName : FolderGroupTitle)
            : FileName;

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

    public long? FileSize { get; set; }

    [ObservableProperty]
    public partial bool IsOnlineOnly { get; set; }

    [ObservableProperty]
    public partial string? CloudItemId { get; set; }

    [ObservableProperty]
    public partial bool IsCloudBacked { get; set; }

    public bool HasThumbnail => !string.IsNullOrEmpty(ThumbPath);

    public bool ShowCloudTile =>
        GalleryMedia.ShowPlaceholderTile(IsOnlineOnly, IsOrphan, ThumbImage is not null);

    public bool ShowCloudBadge =>
        GalleryMedia.ShowCloudBadge(
            IsFolderHeader,
            IsOrphan,
            IsOnlineOnly,
            !string.IsNullOrEmpty(CloudItemId),
            IsCloudBacked);

    public string CloudBadgeAutomationId => GalleryMedia.CloudBadgeAutomationId(Id);

    partial void OnIsOnlineOnlyChanged(bool value)
    {
        OnPropertyChanged(nameof(ShowCloudTile));
        OnPropertyChanged(nameof(ShowCloudBadge));
    }

    partial void OnCloudItemIdChanged(string? value) => OnPropertyChanged(nameof(ShowCloudBadge));

    partial void OnIsCloudBackedChanged(bool value) => OnPropertyChanged(nameof(ShowCloudBadge));

    partial void OnIsOrphanChanged(bool value)
    {
        OnPropertyChanged(nameof(ShowCloudTile));
        OnPropertyChanged(nameof(ShowCloudBadge));
    }

    partial void OnThumbImageChanged(ImageSource? value) => OnPropertyChanged(nameof(ShowCloudTile));

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

    public string ChipAutomationName =>
        string.IsNullOrWhiteSpace(SourceLabel) ? TagName : $"{TagName} {SourceLabel}";

    partial void OnSourceLabelChanged(string value) => OnPropertyChanged(nameof(ChipAutomationName));

    partial void OnTagNameChanged(string value) => OnPropertyChanged(nameof(ChipAutomationName));
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

    public string? ImmediateParentId { get; set; }

    public string? BoardKey { get; set; }

    public bool CanRemoveFromGroup =>
        !string.IsNullOrEmpty(ImmediateParentId) || !string.IsNullOrEmpty(ParentGroupId);

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

    public TagScope? ScopeKind { get; set; }

    public string? AutomationIdOverride { get; set; }

    [ObservableProperty]
    public partial bool IsExpanded { get; set; } = true;

    public string AutomationId =>
        AutomationIdOverride
        ?? (IsUngrouped
            ? "BtnTagGroup_Ungrouped"
            : "BtnTagGroup_" + string.Concat((Name ?? "").Where(char.IsLetterOrDigit)));

    public string CountLabel => $"{Name} ({Chips.Count})";

    public ObservableCollection<TagChipItem> Chips { get; } = [];

    public ObservableCollection<TagLetterSection> Letters { get; } = [];
}

public partial class TagLetterSection : ObservableObject
{
    public string Letter { get; set; } = "";

    public ObservableCollection<TagChipItem> Chips { get; } = [];

    public string Header => $"{Letter} ({Chips.Count})";
}

public partial class TagMosaicSection : ObservableObject
{
    public string Header { get; set; } = "";

    public ObservableCollection<AssetItem> Assets { get; } = [];

    public string AutomationId =>
        "TxtTagMosaicGroup_" + string.Concat((Header ?? "").Where(char.IsLetterOrDigit));

    public string CountLabel => $"{Header} ({Assets.Count})";

    public void NotifyCount() => OnPropertyChanged(nameof(CountLabel));
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

public sealed class TagChipGroupMove
{
    public TagChipItem Chip { get; set; } = new();
    public string GroupId { get; set; } = "";
    public string GroupName { get; set; } = "";
}
