namespace Palace.Helpers;

/// <summary>
/// In-memory CPU tile cache after a single background decode. Each tile is a
/// full 512² RGBA16F Working buffer (gutter inclusive). EXIF rotation is not
/// baked into these bytes. Callers must not retain every L0 tile for large
/// images — drop levels after mipping up.
/// </summary>
public sealed class SmoothCpuTileCache
{
    private readonly Dictionary<SmoothTileId, byte[]> _tiles = new();

    public int Count => _tiles.Count;

    public long ApproxBytes => (long)_tiles.Count * SmoothTileMath.BytesPerTileRgba16F();

    public void Set(SmoothTileId id, byte[] rgba16FSlot)
    {
        ArgumentNullException.ThrowIfNull(rgba16FSlot);
        var expected = checked((int)SmoothTileMath.BytesPerTileRgba16F());
        if (rgba16FSlot.Length != expected)
        {
            throw new ArgumentException($"Tile must be {expected} bytes (512² RGBA16F).", nameof(rgba16FSlot));
        }

        _tiles[id] = rgba16FSlot;
    }

    public bool TryGet(SmoothTileId id, out byte[] rgba16FSlot) =>
        _tiles.TryGetValue(id, out rgba16FSlot!);

    public bool Remove(SmoothTileId id) => _tiles.Remove(id);

    public int RemoveLevel(int level)
    {
        var keys = _tiles.Keys.Where(k => k.Level == level).ToList();
        foreach (var k in keys)
        {
            _tiles.Remove(k);
        }

        return keys.Count;
    }

    public void Clear() => _tiles.Clear();
}
