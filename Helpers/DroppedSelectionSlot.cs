namespace Palace.Helpers;

/// <summary>
/// One-slot buffer for a Library selection dropped by folder/filter change.
/// Not a FileOp journal entry — Ctrl+Z restores only while this drop is still
/// the most recent action; later file ops / new selection / navigation discard it.
/// </summary>
public sealed class DroppedSelectionSlot
{
    private List<string>? _ids;

    /// <summary>True when a capture is present and still the most recent action.</summary>
    public bool CanRestore => _ids is { Count: > 0 } && IsMostRecent;

    /// <summary>Ids from the last capture (null when empty / discarded).</summary>
    public IReadOnlyList<string>? Ids => _ids;

    /// <summary>False after any later file op, new selection, or navigation.</summary>
    public bool IsMostRecent { get; private set; }

    /// <summary>Write the buffer when a folder/filter change drops the selection.</summary>
    public void Capture(IEnumerable<string> ids)
    {
        var list = ids
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .Distinct(StringComparer.Ordinal)
            .ToList();
        if (list.Count == 0)
        {
            Discard();
            return;
        }

        _ids = list;
        IsMostRecent = true;
    }

    /// <summary>Clear the slot entirely (no Ctrl+Z restore).</summary>
    public void Discard()
    {
        _ids = null;
        IsMostRecent = false;
    }

    /// <summary>
    /// A later file op, new selection, or navigation — drop is no longer most recent.
    /// Ids are cleared so Ctrl+Z falls through to FileOp undo.
    /// </summary>
    public void NotifyOtherAction() => Discard();

    /// <summary>
    /// Consume the slot when Ctrl+Z should restore selection. Returns null when
    /// the drop is not restorable (falls through to FileOp undo).
    /// </summary>
    public IReadOnlyList<string>? TryTakeRestore()
    {
        if (!CanRestore || _ids is null)
        {
            return null;
        }

        var ids = _ids;
        Discard();
        return ids;
    }
}
