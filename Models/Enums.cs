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
    Starred,
    /// <summary>Tags board left-nav Recent row (t2-03b).</summary>
    Recent,
    /// <summary>Non-selectable GROUPS header with + (t2-03b).</summary>
    GroupsHeader
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
    TagDelete,
    /// <summary>
    /// Tags-page batch create (t2-03b). Journals <see cref="FileOpStep.CreateTag"/> and
    /// optional <see cref="FileOpStep.AddMembership"/>; Undo reverses membership then unused creates.
    /// </summary>
    TagCreate,
    /// <summary>Add a TagAlias edge (+ AssetTag rewrite); Undo restores alias assignments.</summary>
    TagAliasAdd,
    /// <summary>
    /// Recycle rejected files (+ emptied <c>_rejected</c> folders). Undo restores from
    /// Recycle Bin / trash and reinserts catalog rows. Permanent delete is not journaled.
    /// </summary>
    DeleteRejected
}

/// <summary>One step inside a <see cref="FileOpKind"/> batch. Undo replays steps in reverse.</summary>
public enum FileOpStep
{
    MoveFile,
    CreateDir,
    RenameDir,
    DeleteDir,
    /// <summary>
    /// AssetId + OldPath=tagId + NewPath=tagName + OldSourceId=prior applied TagSource
    /// when KeepAssigned (source-only / already-had), else the applied source.
    /// </summary>
    AssignTag,
    /// <summary>Same fields as <see cref="AssignTag"/>. OldSourceId is that asset's own TagSource.</summary>
    RemoveTag,
    /// <summary>OldPath=tagId; NewPath=JSON <c>TagDeleteSnapshot</c>.</summary>
    DeleteTag,
    /// <summary>
    /// Tag row created during TagAssign (quick-create) or Tags-page TagCreate.
    /// OldPath=tagId; NewPath=tagName. Undo deletes the tag when unused.
    /// Journaled before AssignTag / AddMembership so Undo strips dependents first.
    /// </summary>
    CreateTag,
    /// <summary>
    /// Group membership added during Tags-page create. OldPath=parentId; NewPath=childId.
    /// Undo removes the edge (so a following CreateTag undo can delete an unused child).
    /// </summary>
    AddMembership,
    /// <summary>OldPath=aliasId; NewPath=idealId; NewSourceId=JSON migrated AssetTag rows.</summary>
    AddAlias,
    /// <summary>
    /// Recycled rejected file. AssetId set; OldPath=original path; NewPath=JSON
    /// <c>AssetDeleteSnapshot</c>; OldSourceId=source folder id; NewSourceId=trash token.
    /// </summary>
    RecycleFile
}
