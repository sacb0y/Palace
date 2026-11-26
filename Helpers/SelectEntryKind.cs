namespace Palace.Helpers;

/// <summary>
/// How Library select mode was entered. Drives plain tile-body click:
/// modifier-entered → replace + exit; purposeful → toggle.
/// </summary>
public enum SelectEntryKind
{
    None = 0,
    /// <summary>Shift / Ctrl / Ctrl+Shift click auto-enter.</summary>
    Modifier = 1,
    /// <summary>Select toggle, tile checkbox, hold, or Ctrl+A.</summary>
    Purposeful = 2,
}
