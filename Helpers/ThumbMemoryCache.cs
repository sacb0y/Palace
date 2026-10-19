namespace Palace.Helpers;

/// <summary>
/// Small LRU of already decoded thumbs, keyed by path and decode size.
/// Returning to a tile, or to the Library page, reuses the bitmap.
/// </summary>
public sealed class ThumbMemoryCache<T> where T : class
{
    private readonly int _capacity;
    private readonly Dictionary<string, LinkedListNode<(string Key, T Value)>> _map = new(StringComparer.OrdinalIgnoreCase);
    private readonly LinkedList<(string Key, T Value)> _order = new();

    public ThumbMemoryCache(int capacity)
    {
        _capacity = Math.Max(1, capacity);
    }

    public int Count => _map.Count;

    public static string KeyFor(string? path, int decodeWidth, int decodeHeight) =>
        string.IsNullOrEmpty(path) ? "" : path + "|" + decodeWidth.ToString() + "x" + decodeHeight.ToString();

    public bool TryGet(string key, out T? value)
    {
        if (!string.IsNullOrEmpty(key) && _map.TryGetValue(key, out var node))
        {
            _order.Remove(node);
            _order.AddFirst(node);
            value = node.Value.Value;
            return true;
        }

        value = null;
        return false;
    }

    public void Set(string key, T value)
    {
        if (string.IsNullOrEmpty(key))
        {
            return;
        }

        if (_map.TryGetValue(key, out var existing))
        {
            _order.Remove(existing);
            _map.Remove(key);
        }

        var node = _order.AddFirst((key, value));
        _map[key] = node;
        while (_map.Count > _capacity && _order.Last is { } last)
        {
            _map.Remove(last.Value.Key);
            _order.RemoveLast();
        }
    }
}
