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
