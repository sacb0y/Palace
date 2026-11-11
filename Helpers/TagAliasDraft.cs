using Palace.Data;
using Palace.Models;

namespace Palace.Helpers;

public enum TagAliasDraftKind
{
    Empty,
    /// <summary>Typed name folds to the ideal's own name (case, _, -, spaces). Info note; Enter is a no-op.</summary>
    AutoMatch,
    /// <summary>Already an alias of this ideal. Info note; nothing to add.</summary>
    AlreadyHere,
    /// <summary>Alias of another ideal. Error with Move it here / Cancel.</summary>
    ConflictOther,
    /// <summary>A real (non-alias) tag. Error with Merge into ideal.</summary>
    ExistingTag,
    /// <summary>Unused name; safe to create as a new alias.</summary>
    Ready
}

public enum TagAliasDraftSeverity
{
    None,
    Info,
    Error
}

/// <summary>
/// al-02 validation for one typed alias name against the tag whose details are open (the ideal).
/// Pure: callers pass the catalog snapshot, never WinUI.
/// </summary>
public sealed class TagAliasDraft
{
    public TagAliasDraftKind Kind { get; init; }

    /// <summary>Typed name after <see cref="TagNames.Normalize"/>.</summary>
    public string Name { get; init; } = "";

    public string IdealId { get; init; } = "";

    public string IdealName { get; init; } = "";

    /// <summary>Tag row that already owns <see cref="Name"/> (alias row or real tag).</summary>
    public string? ExistingTagId { get; init; }

    public string? ExistingName { get; init; }

    /// <summary>For <see cref="TagAliasDraftKind.ConflictOther"/>: the ideal that owns the alias today.</summary>
    public string? OtherIdealId { get; init; }

    public string? OtherIdealName { get; init; }

    public int? ExistingFileCount { get; init; }

    public TagAliasDraftSeverity Severity => Kind switch
    {
        TagAliasDraftKind.AutoMatch or TagAliasDraftKind.AlreadyHere => TagAliasDraftSeverity.Info,
        TagAliasDraftKind.ConflictOther or TagAliasDraftKind.ExistingTag => TagAliasDraftSeverity.Error,
        TagAliasDraftKind.Empty or TagAliasDraftKind.Ready => TagAliasDraftSeverity.None,
        _ => throw new InvalidOperationException($"Unhandled alias draft kind {Kind}.")
    };

    public bool CanCreate => Kind == TagAliasDraftKind.Ready;

    public bool ShowsMove => Kind == TagAliasDraftKind.ConflictOther;

    public bool ShowsMerge => Kind == TagAliasDraftKind.ExistingTag;

    /// <summary>First line of the strip (the name in bold in the mockup).</summary>
    public string Headline => Kind switch
    {
        TagAliasDraftKind.AutoMatch => $"Already matches {IdealName} automatically. No alias needed.",
        TagAliasDraftKind.AlreadyHere => $"{Name} is already an alias of {IdealName}.",
        TagAliasDraftKind.ConflictOther => $"{Name} already points to {OtherIdealName}",
        TagAliasDraftKind.ExistingTag => ExistingFileCount is { } n
            ? $"{ExistingName} is a tag ({(n == 1 ? "1 file" : $"{n} files")}), so it can’t be an alias."
            : $"{ExistingName} is a tag, so it can’t be an alias.",
        TagAliasDraftKind.Empty or TagAliasDraftKind.Ready => "",
        _ => throw new InvalidOperationException($"Unhandled alias draft kind {Kind}.")
    };

    /// <summary>Second, dimmer line.</summary>
    public string Detail => Kind switch
    {
        TagAliasDraftKind.AutoMatch => "Capitals, _ , - and extra spaces are ignored. Enter does nothing.",
        TagAliasDraftKind.ConflictOther => "An alias can point to only one tag.",
        TagAliasDraftKind.ExistingTag => $"To make it one, merge it into {IdealName} first.",
        TagAliasDraftKind.AlreadyHere or TagAliasDraftKind.Empty or TagAliasDraftKind.Ready => "",
        _ => throw new InvalidOperationException($"Unhandled alias draft kind {Kind}.")
    };

    public string MoveLabel => "Move it here";

    public string MergeLabel => $"Merge {ExistingName} into {IdealName}…";

    public const string CancelLabel = "Cancel";

    public const string HelperText =
        "Spelling, capitals and _ are matched automatically; add only different words.";

    public string MoveConfirmTitle => "Move alias?";

    public string MoveConfirmMessage =>
        $"{Name} points to {OtherIdealName} today. Move it to {IdealName}? Undo puts it back.";

    public string MergeConfirmTitle => "Merge tags?";

    public string MergeConfirmMessage =>
        $"{ExistingName} will become an alias of {IdealName}, and its files will carry {IdealName}. Undo reverses it.";

    public static TagAliasDraft Evaluate(
        string? typed,
        Tag ideal,
        IReadOnlyList<Tag> tags,
        IReadOnlyList<TagAlias> aliases,
        IReadOnlyDictionary<string, int>? fileCounts = null)
    {
        var name = TagNames.Normalize(typed);
        if (name.Length == 0)
        {
            return new TagAliasDraft { Kind = TagAliasDraftKind.Empty, IdealId = ideal.Id, IdealName = ideal.Name };
        }

        TagAliasDraft Make(
            TagAliasDraftKind kind,
            Tag? existing = null,
            Tag? other = null) => new()
        {
            Kind = kind,
            Name = name,
            IdealId = ideal.Id,
            IdealName = ideal.Name,
            ExistingTagId = existing?.Id,
            ExistingName = existing?.Name,
            OtherIdealId = other?.Id,
            OtherIdealName = other?.Name,
            ExistingFileCount = existing is not null && fileCounts is not null
                && fileCounts.TryGetValue(existing.Id, out var count)
                    ? count
                    : null
        };

        if (TagNames.Same(name, ideal.Name))
        {
            return Make(TagAliasDraftKind.AutoMatch);
        }

        var owner = tags.FirstOrDefault(t => TagNames.Same(t.Name, name));
        if (owner is null)
        {
            return Make(TagAliasDraftKind.Ready);
        }

        var edge = aliases.FirstOrDefault(a => string.Equals(a.AliasTagId, owner.Id, StringComparison.Ordinal));
        if (edge is null)
        {
            return Make(TagAliasDraftKind.ExistingTag, existing: owner);
        }

        if (string.Equals(edge.IdealTagId, ideal.Id, StringComparison.Ordinal))
        {
            return Make(TagAliasDraftKind.AlreadyHere, existing: owner);
        }

        var otherIdeal = tags.FirstOrDefault(t => string.Equals(t.Id, edge.IdealTagId, StringComparison.Ordinal));
        return Make(TagAliasDraftKind.ConflictOther, existing: owner, other: otherIdeal);
    }
}
