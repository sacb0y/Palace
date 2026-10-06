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
        new(MosaicSort.Type, "Type")
    ];

    public static IReadOnlyList<string> Labels { get; } =
        Choices.Select(choice => choice.Label).ToList();

    public static string SettingsKey(string projectId) => $"MosaicSort_{projectId}";

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

    public static MosaicSort FromIndex(int index)
    {
        if (index < 0 || index >= Choices.Count)
        {
            return Default;
        }

        return Choices[index].Key;
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
            "COALESCE(DateModified, DateAdded) ASC, FileName COLLATE NOCASE, Path COLLATE NOCASE",
        MosaicSort.DateAdded =>
            "DateAdded DESC, FileName COLLATE NOCASE, Path COLLATE NOCASE",
        MosaicSort.Name =>
            "FileName COLLATE NOCASE, Path COLLATE NOCASE",
        MosaicSort.NameZ =>
            "FileName COLLATE NOCASE DESC, Path COLLATE NOCASE DESC",
        MosaicSort.Size =>
            "FileSize DESC, FileName COLLATE NOCASE, Path COLLATE NOCASE",
        MosaicSort.Rating =>
            "Rating DESC, FileName COLLATE NOCASE, Path COLLATE NOCASE",
        MosaicSort.Type =>
            "Kind, FileName COLLATE NOCASE, Path COLLATE NOCASE",
        _ =>
            "COALESCE(DateModified, DateAdded) DESC, FileName COLLATE NOCASE, Path COLLATE NOCASE"
    };

    public static string OrderBySql(MosaicSort sort) => "ORDER BY " + Sql(sort);
}
