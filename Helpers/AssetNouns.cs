namespace Palace.Helpers;

/// <summary>
/// Kind-agnostic count wording for Library / Tags UI. Prefer "item(s)" so 3D and audio
/// can share the same chrome without hardcoding "image".
/// </summary>
public static class AssetNouns
{
    public static string Count(int n) =>
        n == 1 ? "1 item" : $"{n} items";

    public static string CountWith(int n, string suffix) =>
        $"{Count(n)} {suffix}";

    public static string WithThisTag(int n) =>
        n == 1 ? "1 item with this tag" : $"{n} items with this tag";
}
