using Palace.Models;

namespace Palace.Helpers;

public readonly record struct MosaicSortChoice(MosaicSort Key, string Label);

/// <summary>
/// Library / Tags mosaic sort. SQL fragments are allowlisted from
/// <see cref="MosaicSort"/> — never concatenate user text into ORDER BY.
/// Persist per project via LocalSettings (<see cref="SettingsKey"/>).
/// </summary>
public static class MosaicSortOrder
{
    public const MosaicSort Default = MosaicSort.DateNewest;

    /// <summary>Internal catalog callers that do not take a user sort keep name order.</summary>
    public const MosaicSort CatalogDefault = MosaicSort.Name;

    public static IReadOnlyList<MosaicSortChoice> Choices { get; } =
    [
        new(MosaicSort.DateNewest, "Date (newest)"),
        new(MosaicSort.DateOldest, "Date (oldest)"),
        new(MosaicSort.DateAdded, "Date added"),
        new(MosaicSort.Name, "Name"),
        new(MosaicSort.NameZ, "Name (Z–A)"),
        new(MosaicSort.Size, "Size"),
        new(MosaicSort.Rating, "Rating"),
        new(MosaicSort.Type, "Type"),
        new(MosaicSort.DateAddedOldest, "Date added (oldest)"),
        new(MosaicSort.SizeSmallest, "Size (smallest)"),
        new(MosaicSort.RatingLowest, "Rating (lowest)"),
        new(MosaicSort.TypeZ, "Type (Z–A)"),
        new(MosaicSort.DateTakenNewest, "Date taken (newest)"),
        new(MosaicSort.DateTakenOldest, "Date taken (oldest)")
    ];

    public static IReadOnlyList<string> Labels { get; } =
        Choices.Select(choice => choice.Label).ToList();

    /// <summary>Pre-per-view key (Library and Tags shared it); still read as the fallback.</summary>
    public static string SettingsKey(string projectId) => $"MosaicSort_{projectId}";

    public static string SettingsKey(string projectId, MosaicSortView view) => $"MosaicSort_{projectId}_{view}";

    /// <summary>The view's own setting, else the old shared one, else <see cref="Default"/>.</summary>
    public static MosaicSort Load(Func<string, object?> read, string projectId, MosaicSortView view)
    {
        var own = read(SettingsKey(projectId, view));
        return own is not null ? Parse(own) : Parse(read(SettingsKey(projectId)));
    }

    public static IReadOnlyList<(MosaicSortField Field, string Label)> FieldChoices { get; } =
    [
        (MosaicSortField.DateTaken, "Date taken"),
        (MosaicSortField.DateModified, "Date modified"),
        (MosaicSortField.DateAdded, "Date added"),
        (MosaicSortField.Name, "Name"),
        (MosaicSortField.Size, "Size"),
        (MosaicSortField.Type, "Type"),
        (MosaicSortField.Rating, "Rating")
    ];

    public static IReadOnlyList<string> FieldLabels { get; } = FieldChoices.Select(c => c.Label).ToList();

    public static MosaicSortField FieldOf(MosaicSort sort) => Normalize(sort) switch
    {
        MosaicSort.DateTakenNewest or MosaicSort.DateTakenOldest => MosaicSortField.DateTaken,
        MosaicSort.DateAdded or MosaicSort.DateAddedOldest => MosaicSortField.DateAdded,
        MosaicSort.Name or MosaicSort.NameZ => MosaicSortField.Name,
        MosaicSort.Size or MosaicSort.SizeSmallest => MosaicSortField.Size,
        MosaicSort.Type or MosaicSort.TypeZ => MosaicSortField.Type,
        MosaicSort.Rating or MosaicSort.RatingLowest => MosaicSortField.Rating,
        _ => MosaicSortField.DateModified
    };

    public static bool IsDescending(MosaicSort sort) => Normalize(sort) is
        MosaicSort.DateNewest or MosaicSort.DateAdded or MosaicSort.DateTakenNewest
        or MosaicSort.NameZ or MosaicSort.Size or MosaicSort.Rating or MosaicSort.TypeZ;

    /// <summary>Direction a field starts in: newest / largest / highest first, names and types A to Z.</summary>
    public static bool DefaultDescending(MosaicSortField field) => field is not (MosaicSortField.Name or MosaicSortField.Type);

    public static MosaicSort Compose(MosaicSortField field, bool descending) => field switch
    {
        MosaicSortField.DateTaken => descending ? MosaicSort.DateTakenNewest : MosaicSort.DateTakenOldest,
        MosaicSortField.DateAdded => descending ? MosaicSort.DateAdded : MosaicSort.DateAddedOldest,
        MosaicSortField.Name => descending ? MosaicSort.NameZ : MosaicSort.Name,
        MosaicSortField.Size => descending ? MosaicSort.Size : MosaicSort.SizeSmallest,
        MosaicSortField.Type => descending ? MosaicSort.TypeZ : MosaicSort.Type,
        MosaicSortField.Rating => descending ? MosaicSort.Rating : MosaicSort.RatingLowest,
        _ => descending ? MosaicSort.DateNewest : MosaicSort.DateOldest
    };

    public static MosaicSort ToggleDirection(MosaicSort sort) => Compose(FieldOf(sort), !IsDescending(sort));

    public static int FieldIndexOf(MosaicSort sort)
    {
        var field = FieldOf(sort);
        for (var i = 0; i < FieldChoices.Count; i++)
        {
            if (FieldChoices[i].Field == field)
            {
                return i;
            }
        }

        return 0;
    }

    /// <summary>
    /// Field ComboBox SelectionChanged: true when <paramref name="index"/> picks another field.
    /// The new field starts in its <see cref="DefaultDescending"/> direction. -1 / out of range is ignored.
    /// </summary>
    public static bool ShouldApplyFieldIndex(int index, MosaicSort current, out MosaicSort next)
    {
        next = Normalize(current);
        if (index < 0 || index >= FieldChoices.Count || FieldChoices[index].Field == FieldOf(current))
        {
            return false;
        }

        var field = FieldChoices[index].Field;
        next = Compose(field, DefaultDescending(field));
        return true;
    }

    /// <summary>Direction button text / screen reader help: "Newest first", "A to Z", …</summary>
    public static string DirectionLabel(MosaicSort sort)
    {
        var descending = IsDescending(sort);
        return FieldOf(sort) switch
        {
            MosaicSortField.Name or MosaicSortField.Type => descending ? "Z to A" : "A to Z",
            MosaicSortField.Size => descending ? "Largest first" : "Smallest first",
            MosaicSortField.Rating => descending ? "Highest first" : "Lowest first",
            _ => descending ? "Newest first" : "Oldest first"
        };
    }

    /// <summary>DropDownButton face: "Sort: Date taken · Newest first".</summary>
    public static string ButtonLabel(MosaicSort sort)
    {
        var field = FieldChoices[FieldIndexOf(sort)].Label;
        return $"Sort: {field} · {DirectionLabel(sort)}";
    }

    public static string FieldLabel(MosaicSortField field)
    {
        foreach (var (f, label) in FieldChoices)
        {
            if (f == field)
            {
                return label;
            }
        }

        return FieldChoices[0].Label;
    }

    public static MosaicSort Parse(object? stored)
    {
        if (stored is MosaicSort sort && Enum.IsDefined(sort))
        {
            return sort;
        }

        if (stored is int number && Enum.IsDefined((MosaicSort)number))
        {
            return (MosaicSort)number;
        }

        if (stored is string text && Enum.TryParse(text, ignoreCase: true, out MosaicSort parsed)
            && Enum.IsDefined(parsed))
        {
            return parsed;
        }

        return Default;
    }

    public static string Persist(MosaicSort sort) => Normalize(sort).ToString();

    public static MosaicSort Normalize(MosaicSort sort) =>
        Enum.IsDefined(sort) ? sort : Default;

    public static int IndexOf(MosaicSort sort)
    {
        var key = Normalize(sort);
        for (var i = 0; i < Choices.Count; i++)
        {
            if (Choices[i].Key == key)
            {
                return i;
            }
        }

        return 0;
    }

    public static MosaicSort FromIndex(int index) =>
        TryFromIndex(index, out var sort) ? sort : Default;

    /// <summary>
    /// ComboBox TwoWay <c>SelectedIndex</c> writes <c>-1</c> on init/unload and
    /// often <c>0</c> (first item) when <c>ItemsSource</c> attaches. Bind OneWay
    /// and commit from SelectionChanged only after the page stamped the stored
    /// index (<c>_mosaicSortArmed</c>).
    /// </summary>
    public static bool TryFromIndex(int index, out MosaicSort sort)
    {
        if (index < 0 || index >= Choices.Count)
        {
            sort = Default;
            return false;
        }

        sort = Choices[index].Key;
        return true;
    }

    /// <summary>
    /// True when <paramref name="index"/> is a real choice different from
    /// <paramref name="current"/>. False for <c>-1</c> / out of range / no-op.
    /// </summary>
    public static bool ShouldApplyIndex(int index, MosaicSort current, out MosaicSort next)
    {
        current = Normalize(current);
        if (!TryFromIndex(index, out next) || next == current)
        {
            next = current;
            return false;
        }

        return true;
    }

    public static string Label(MosaicSort sort)
    {
        var key = Normalize(sort);
        foreach (var choice in Choices)
        {
            if (choice.Key == key)
            {
                return choice.Label;
            }
        }

        return Choices[0].Label;
    }

    /// <summary>
    /// ORDER BY body only (no leading keyword). Safe to concatenate after
    /// <c>ORDER BY </c> — values are constants, not user input.
    /// </summary>
    public static string Sql(MosaicSort sort) => Normalize(sort) switch
    {
        MosaicSort.DateOldest =>
            "COALESCE(DateModified, DateAdded) ASC, " + NameTie,
        MosaicSort.DateAdded =>
            "DateAdded DESC, " + NameTie,
        MosaicSort.DateAddedOldest =>
            "DateAdded ASC, " + NameTie,
        MosaicSort.DateTakenNewest =>
            "COALESCE(DateTaken, DateModified, DateAdded) DESC, " + NameTie,
        MosaicSort.DateTakenOldest =>
            "COALESCE(DateTaken, DateModified, DateAdded) ASC, " + NameTie,
        MosaicSort.Name =>
            NameTie,
        MosaicSort.NameZ =>
            "FileName COLLATE NOCASE DESC, Path COLLATE NOCASE DESC",
        MosaicSort.Size =>
            "FileSize IS NULL, FileSize DESC, " + NameTie,
        MosaicSort.SizeSmallest =>
            "FileSize IS NULL, FileSize ASC, " + NameTie,
        // Unrated files last in both directions.
        MosaicSort.Rating =>
            "COALESCE(Rating, 0) = 0, Rating DESC, " + NameTie,
        MosaicSort.RatingLowest =>
            "COALESCE(Rating, 0) = 0, Rating ASC, " + NameTie,
        MosaicSort.Type =>
            "Kind, " + NameTie,
        MosaicSort.TypeZ =>
            "Kind DESC, " + NameTie,
        _ =>
            "COALESCE(DateModified, DateAdded) DESC, " + NameTie
    };

    private const string NameTie = "FileName COLLATE NOCASE, Path COLLATE NOCASE";

    public static string OrderBySql(MosaicSort sort) => "ORDER BY " + Sql(sort);
}
