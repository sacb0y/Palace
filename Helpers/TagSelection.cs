namespace Palace.Helpers;

public static class TagSelection
{
    public static bool ShouldApplyLoadedName(string? currentName, string loadedName) =>
        string.IsNullOrEmpty(currentName)
        || string.Equals(currentName, loadedName, StringComparison.Ordinal);
}
