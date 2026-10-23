using System.Collections.Concurrent;

namespace Palace.Imaging;

/// <summary>In-memory 1D LUT cache. Thread-safe; no disk I/O in P1.</summary>
public sealed class LutCache
{
    private readonly ConcurrentDictionary<string, ToneMapLut> _map = new(StringComparer.Ordinal);

    public ToneMapLut GetOrAdd(LutCacheKey key, Func<ToneMapLut> bake) =>
        _map.GetOrAdd(key.Token, _ => bake());

    public bool TryGet(LutCacheKey key, out ToneMapLut? lut) =>
        _map.TryGetValue(key.Token, out lut);

    public void Set(LutCacheKey key, ToneMapLut lut) =>
        _map[key.Token] = lut;

    public bool Remove(LutCacheKey key) =>
        _map.TryRemove(key.Token, out _);

    public void Clear() => _map.Clear();

    public int Count => _map.Count;
}
