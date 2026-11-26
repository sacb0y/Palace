namespace Palace.Helpers;

/// <summary>
/// WinUI-free mirror of Library mosaic selection policy
/// (<c>LibraryViewModel.Selection</c>) for unit tests. Keep in lockstep with
/// EnsureSelectMode / SelectAsset / ApplyTileBodyClick / dropped-selection restore.
/// </summary>
public sealed class MosaicSelectHarness
{
    public sealed class Tile
    {
        public Tile(string id, bool isHeader = false)
        {
            Id = id;
            IsHeader = isHeader;
        }

        public string Id { get; }
        public bool IsHeader { get; }
        public bool IsSelected { get; set; }
    }

    private readonly List<Tile> _order;
    private readonly List<Tile> _selection = [];
    private Tile? _anchor;
    private Tile? _focus;
    private SelectEntryKind _entryKind = SelectEntryKind.None;

    public MosaicSelectHarness(IEnumerable<Tile> ordered)
    {
        _order = ordered.ToList();
    }

    public bool IsSelectMode { get; private set; }
    public SelectEntryKind EntryKind => _entryKind;
    public Tile? Anchor => _anchor;
    public Tile? Focus => _focus;
    public IReadOnlyList<Tile> Selection => _selection;
    public DroppedSelectionSlot Dropped { get; } = new();
    public int SelectionCount => _selection.Count(t => !t.IsHeader);

    public void EnsureSelectMode(SelectEntryKind kind)
    {
        var next = SelectMode.NextEntryKind(IsSelectMode, _entryKind, kind);
        if (!IsSelectMode)
        {
            IsSelectMode = true;
        }

        _entryKind = next;
    }

    public void MarkSelectEntry(SelectEntryKind kind) => _entryKind = kind;

    public void SelectAsset(Tile item, bool toggle, bool range = false, bool addRange = false)
    {
        if (item.IsHeader)
        {
            return;
        }

        Dropped.NotifyOtherAction();

        if ((range || addRange) && _anchor is null)
        {
            _anchor = _focus ?? _selection.FirstOrDefault(t => !t.IsHeader);
        }

        if ((range || addRange) && _anchor is not null)
        {
            var flags = _order.Select(t => t.IsHeader).ToList();
            var from = _order.IndexOf(_anchor);
            var to = _order.IndexOf(item);
            if (from >= 0 && to >= 0)
            {
                var priorAnchor = _anchor;
                IReadOnlyList<int> indexes;
                if (addRange)
                {
                    var prior = new List<int>();
                    for (var i = 0; i < _order.Count; i++)
                    {
                        if (!_order[i].IsHeader && _order[i].IsSelected)
                        {
                            prior.Add(i);
                        }
                    }

                    indexes = MosaicModifierSelect.UnionRange(flags, prior, from, to);
                }
                else
                {
                    indexes = MosaicModifierSelect.RangeIndexes(flags, from, to);
                }

                ApplyIndexes(indexes);
                _anchor = priorAnchor;
                _focus = item;
                return;
            }
        }

        _focus = item;
        if (!toggle)
        {
            foreach (var t in _order)
            {
                t.IsSelected = !t.IsHeader && ReferenceEquals(t, item);
            }

            _selection.Clear();
            _selection.Add(item);
            item.IsSelected = true;
            _anchor = item;
            return;
        }

        // Deterministic Ctrl path (same as LibraryViewModel.SetAssetSelected).
        SetAssetSelected(item, MosaicModifierSelect.CtrlClickWantsSelected(item.IsSelected));
    }

    /// <summary>
    /// Ctrl+click from the model: set membership once (no flip-from-current race).
    /// </summary>
    public void SetAssetSelected(Tile item, bool wantSelected)
    {
        if (item.IsHeader)
        {
            return;
        }

        Dropped.NotifyOtherAction();
        _focus = item;
        _anchor = item;
        item.IsSelected = wantSelected;
        if (wantSelected)
        {
            if (!_selection.Contains(item))
            {
                _selection.Add(item);
            }
        }
        else
        {
            _selection.Remove(item);
        }

        MaybeExitWhenEmpty();
    }

    /// <summary>Explorer Ctrl+click: invert model membership once; count changes by ±1.</summary>
    public void CtrlClick(Tile item)
    {
        EnsureSelectMode(SelectMode.EntryFromModifier());
        var want = MosaicModifierSelect.CtrlClickWantsSelected(item.IsSelected);
        SetAssetSelected(item, want);
    }

    public void ApplyTileBodyClick(Tile item)
    {
        if (item.IsHeader)
        {
            return;
        }

        if (SelectMode.TileBodyClickReplacesAndExits(_entryKind))
        {
            SelectAsset(item, toggle: false);
            IsSelectMode = false;
            _entryKind = SelectEntryKind.None;
            return;
        }

        SelectAsset(item, toggle: true);
        MaybeExitWhenEmpty();
    }

    public void CaptureAndDrop()
    {
        if (_selection.Count > 0)
        {
            Dropped.Capture(_selection.Where(t => !t.IsHeader).Select(t => t.Id));
        }

        Clear();
        IsSelectMode = false;
        _entryKind = SelectEntryKind.None;
    }

    /// <summary>
    /// Restore dropped ids that still exist in the current order. Returns false
    /// when nothing visible remains (caller falls through to FileOp undo).
    /// </summary>
    public bool TryRestoreDropped()
    {
        var ids = Dropped.TryTakeRestore();
        if (ids is null || ids.Count == 0)
        {
            return false;
        }

        var want = new HashSet<string>(ids, StringComparer.Ordinal);
        var resolved = _order.Where(t => !t.IsHeader && want.Contains(t.Id)).ToList();
        if (resolved.Count == 0)
        {
            return false;
        }

        ApplyIndexes(resolved.Select(t => _order.IndexOf(t)).Where(i => i >= 0).ToList());
        EnsureSelectMode(SelectMode.EntryFromPurposeful());
        return true;
    }

    public void SimulateTagEditAfterDrop() => Dropped.NotifyOtherAction();

    private void ApplyIndexes(IReadOnlyList<int> indexes)
    {
        var set = indexes.ToHashSet();
        _selection.Clear();
        for (var i = 0; i < _order.Count; i++)
        {
            var on = set.Contains(i) && !_order[i].IsHeader;
            _order[i].IsSelected = on;
            if (on)
            {
                _selection.Add(_order[i]);
            }
        }
    }

    private void Clear()
    {
        foreach (var t in _order)
        {
            t.IsSelected = false;
        }

        _selection.Clear();
        _anchor = null;
        _focus = null;
    }

    private void MaybeExitWhenEmpty()
    {
        if (SelectMode.ExitsWhenSelectionEmpty(IsSelectMode, SelectionCount))
        {
            IsSelectMode = false;
            _entryKind = SelectEntryKind.None;
        }
    }
}
