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

/// <summary>User mosaic sort for Library and Tags. Mapped to SQL in <c>MosaicSortOrder</c>.</summary>
public enum MosaicSort
{
    DateNewest,
    DateOldest,
    DateAdded,
    Name,
    NameZ,
    Size,
    Rating,
    Type
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
    DeleteFolder
}

/// <summary>One step inside a <see cref="FileOpKind"/> batch. Undo replays steps in reverse.</summary>
public enum FileOpStep
{
    MoveFile,
    CreateDir,
    RenameDir,
    DeleteDir
}
