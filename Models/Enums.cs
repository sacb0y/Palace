namespace Palace.Models;

public enum AssetKind
{
    Image,
    Gif,
    Video,
    Other
}

public enum TagSource
{
    Manual,
    Prompt,
    AiLocal,
    AiCloud,
    Implied
}

public enum TagFilterMode
{
    Any,
    All,
    None
}

/// <summary>
/// User mosaic sort for Library and Tags: one value per field and direction. Mapped to SQL in
/// <c>MosaicSortOrder</c>. Values are persisted by name; only append.
/// </summary>
public enum MosaicSort
{
    DateNewest,
    DateOldest,
    DateAdded,
    Name,
    NameZ,
    Size,
    Rating,
    Type,
    DateAddedOldest,
    SizeSmallest,
    RatingLowest,
    TypeZ,
    DateTakenNewest,
    DateTakenOldest
}

/// <summary>What the mosaic sorts by (the direction is separate: <c>MosaicSortOrder.IsDescending</c>).</summary>
public enum MosaicSortField
{
    DateTaken,
    DateModified,
    DateAdded,
    Name,
    Size,
    Type,
    Rating
}

/// <summary>Where a mosaic sort is remembered.</summary>
public enum MosaicSortView
{
    Folders,
    TagBrowse,
    Tags
}

public enum TagScope
{
    All,
    Ungrouped,
    Starred
}

public enum DestinationPolicy
{
    InSource,
    Destination
}

public enum OrganizeItemStatus
{
    Planned,
    Ready,
    Collision,
    Applied,
    Skipped,
    Error,
    Undone
}

public enum SourceKind
{
    Local,
    OneDrive,
    Dropbox
}

public enum CloudProvider
{
    OneDrive,
    Dropbox
}

/// <summary>Kind of a logged, undoable file operation batch (<c>FileOpService</c>).</summary>
public enum FileOpKind
{
    Move,
    CreateFolder,
    RenameFolder,
    DeleteFolder,
    /// <summary>One Organize apply: created folders + file moves in a single Undo entry.</summary>
    Organize,
    /// <summary>Assign tag(s) to asset(s); Undo removes those assignments.</summary>
    TagAssign,
    /// <summary>Remove tag(s) from asset(s); Undo re-assigns them.</summary>
    TagRemove,
    /// <summary>Delete a tag row (assignments / memberships / implications snapshotted); Undo restores.</summary>
    TagDelete
}

/// <summary>One step inside a <see cref="FileOpKind"/> batch. Undo replays steps in reverse.</summary>
public enum FileOpStep
{
    MoveFile,
    CreateDir,
    RenameDir,
    DeleteDir,
    /// <summary>AssetId + OldPath=tagId + NewPath=tagName + OldSourceId=TagSource.</summary>
    AssignTag,
    /// <summary>Same fields as <see cref="AssignTag"/>; Undo re-assigns.</summary>
    RemoveTag,
    /// <summary>OldPath=tagId; NewPath=JSON <c>TagDeleteSnapshot</c>.</summary>
    DeleteTag
}
