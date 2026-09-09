namespace Palace.Helpers;

public sealed class RoomIconChoice
{
    public string Id { get; init; } = "";
    public string Label { get; init; } = "";
}

public static class RoomIcons
{
    public const string Default = "BuildingBank";

    public static IReadOnlyList<RoomIconChoice> All { get; } =
    [
        new() { Id = "BuildingBank", Label = "Palace" },
        new() { Id = "Door", Label = "Door" },
        new() { Id = "Home", Label = "Home" },
        new() { Id = "Book", Label = "Book" },
        new() { Id = "Image", Label = "Image" },
        new() { Id = "Collections", Label = "Collections" },
        new() { Id = "Camera", Label = "Camera" },
        new() { Id = "Bed", Label = "Bed" },
        new() { Id = "Lightbulb", Label = "Idea" },
        new() { Id = "MusicNote2", Label = "Music" },
        new() { Id = "Board", Label = "Board" },
        new() { Id = "Cube", Label = "Cube" }
    ];

    public static string Normalize(string? id)
    {
        if (string.IsNullOrWhiteSpace(id))
        {
            return Default;
        }

        var text = id.Trim();
        return All.Any(choice => choice.Id.Equals(text, StringComparison.OrdinalIgnoreCase))
            ? All.First(choice => choice.Id.Equals(text, StringComparison.OrdinalIgnoreCase)).Id
            : Default;
    }

    public static RoomIconChoice Find(string? id)
    {
        var normalized = Normalize(id);
        return All.First(choice => choice.Id == normalized);
    }
}
