namespace Palace.Helpers;

public static class TagSelection
{
    public static bool ShouldApplyLoadedName(
        string? currentName,
        string loadedName,
        string? baselineName,
        string? baselineTagId,
        string tagId)
    {
        if (!string.Equals(baselineTagId, tagId, StringComparison.Ordinal))
        {
            return true;
        }

        return string.Equals(currentName, baselineName, StringComparison.Ordinal)
            || string.Equals(currentName, loadedName, StringComparison.Ordinal);
    }
}
