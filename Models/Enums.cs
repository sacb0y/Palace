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
