namespace Palace.Helpers;

/// <summary>
/// GPU tile-slot LRU. Coarse-level tiles (level ≥ coarse pin) and temporary
/// pins are never evicted. Capacity is derived from available VRAM, clamped
/// down to budget with floor = coarse-set size (never inflated to 128).
/// </summary>
public sealed class SmoothGpuTileLru
{
    private readonly int _capacity;
    private readonly int _coarsePinLevel;
    private readonly Dictionary<SmoothTileId, int> _idToSlot = new();
    private readonly SmoothTileId?[] _slotToId;
    private readonly LinkedList<SmoothTileId> _order = new();
    private readonly Dictionary<SmoothTileId, LinkedListNode<SmoothTileId>> _nodes = new();
    private readonly Stack<int> _free = new();
    private readonly HashSet<SmoothTileId> _tempPins = new();

    public SmoothGpuTileLru(int capacity, int coarsePinLevel)
    {
        if (capacity < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(capacity));
        }

        _capacity = capacity;
        _coarsePinLevel = coarsePinLevel;
        _slotToId = new SmoothTileId?[capacity];
        for (var i = capacity - 1; i >= 0; i--)
        {
            _free.Push(i);
        }
    }

    public int Capacity => _capacity;

    public int ResidentCount => _idToSlot.Count;

    public int FreeCount => _free.Count;

    public int CoarsePinLevel => _coarsePinLevel;

    /// <summary>
    /// Pool size from free VRAM. Clamps down to budget/8 ÷ bytes-per-tile,
    /// never below <paramref name="coarseTileCount"/>, never above <paramref name="max"/>.
    /// Does not bump a tiny budget up to 128.
    /// </summary>
    public static int CapacityFromVramBytes(long availableVramBytes, int coarseTileCount, int max = 192)
    {
        if (coarseTileCount < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(coarseTileCount));
        }

        if (max < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(max));
        }

        var floor = Math.Min(max, coarseTileCount);
        var per = SmoothTileMath.BytesPerTileRgba16F();
        var budget = Math.Max(0L, availableVramBytes / 8);
        var fromBudget = (int)Math.Min(int.MaxValue, budget / Math.Max(1L, per));
        if (fromBudget < floor)
        {
            return floor;
        }

        return Math.Min(max, fromBudget);
    }

    public bool TryGetSlot(SmoothTileId id, out int slot)
    {
        if (_idToSlot.TryGetValue(id, out slot))
        {
            Touch(id);
            return true;
        }

        slot = -1;
        return false;
    }

    /// <summary>
    /// Reserves a slot for <paramref name="id"/>, evicting an unpinned LRU
    /// entry when the pool is full. Returns false only when every resident
    /// tile is pinned (coarse or temp).
    /// </summary>
    public bool TryInsert(SmoothTileId id, out int slot, out SmoothTileId? evicted)
    {
        evicted = null;
        if (_idToSlot.TryGetValue(id, out slot))
        {
            Touch(id);
            return true;
        }

        if (_free.Count == 0)
        {
            if (!TryEvict(out var victim, out var victimSlot))
            {
                slot = -1;
                return false;
            }

            evicted = victim;
            _free.Push(victimSlot);
        }

        slot = _free.Pop();
        _slotToId[slot] = id;
        _idToSlot[id] = slot;
        var node = _order.AddFirst(id);
        _nodes[id] = node;
        return true;
    }

    public bool IsPinned(SmoothTileId id) =>
        id.Level >= _coarsePinLevel || _tempPins.Contains(id);

    public void SetTempPin(SmoothTileId id, bool pinned)
    {
        if (pinned)
        {
            _tempPins.Add(id);
        }
        else
        {
            _tempPins.Remove(id);
        }
    }

    public void ClearTempPins() => _tempPins.Clear();

    /// <summary>Drop residency after device lost; free all slots for re-upload.</summary>
    public void Reset()
    {
        _idToSlot.Clear();
        _order.Clear();
        _nodes.Clear();
        _tempPins.Clear();
        _free.Clear();
        for (var i = 0; i < _capacity; i++)
        {
            _slotToId[i] = null;
        }

        for (var i = _capacity - 1; i >= 0; i--)
        {
            _free.Push(i);
        }
    }

    private void Touch(SmoothTileId id)
    {
        if (_nodes.TryGetValue(id, out var node))
        {
            _order.Remove(node);
            _nodes[id] = _order.AddFirst(id);
        }
    }

    private bool TryEvict(out SmoothTileId victim, out int slot)
    {
        for (var node = _order.Last; node is not null; node = node.Previous)
        {
            var id = node.Value;
            if (IsPinned(id))
            {
                continue;
            }

            victim = id;
            slot = _idToSlot[id];
            _order.Remove(node);
            _nodes.Remove(id);
            _idToSlot.Remove(id);
            _slotToId[slot] = null;
            return true;
        }

        victim = default;
        slot = -1;
        return false;
    }
}
