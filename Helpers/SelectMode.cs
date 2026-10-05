namespace Palace.Helpers;

/// <summary>
/// Select-mode rules for the Library mosaic. Off WinUI so the toggle and count text are testable.
/// </summary>
public static class SelectMode
{
    /// <summary>
    /// True when a plain click or tap on a tile should toggle it instead of replacing the selection
    /// (<c>ItemsViewSelectionMode.Multiple</c> vs <c>Extended</c>).
    /// </summary>
    public static bool ClickTogglesTile(bool selectMode) => selectMode;

    /// <summary>Count line beside the toggle. Empty when select mode is off and nothing is selected.</summary>
    public static string Summary(bool selectMode, int selectedCount)
    {
        if (selectedCount > 0)
        {
            return $"{selectedCount} selected";
        }

        return selectMode ? "Click tiles to select" : "";
    }
}
