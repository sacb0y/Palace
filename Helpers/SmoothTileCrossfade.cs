namespace Palace.Helpers;

/// <summary>
/// V2 sharpen cross-fade: when a finer tile replaces a coarser resident, opacity
/// eases 0→1 over ~80–120 ms (Geo). Coarse→fine only; missing tiles stay at 0.
/// </summary>
public sealed class SmoothTileCrossfade
{
    private readonly Dictionary<SmoothTileId, float> _opacity = new();
    private readonly HashSet<SmoothTileId> _seenThisFrame = new();
    private readonly float _durationMs;

    public SmoothTileCrossfade(float durationMs = SmoothViewport.CrossfadeMsDefault)
    {
        _durationMs = Math.Clamp(
            durationMs,
            SmoothViewport.CrossfadeMsMin,
            SmoothViewport.CrossfadeMsMax);
    }

    public float DurationMs => _durationMs;

    public void Clear()
    {
        _opacity.Clear();
        _seenThisFrame.Clear();
    }

    /// <summary>
    /// Mark a tile as drawn this frame. New tiles start at 0 and rise over
    /// <see cref="DurationMs"/>. Returns the opacity to bind on the instance.
    /// </summary>
    public float Tick(SmoothTileId id, float dtMs, bool resident)
    {
        _seenThisFrame.Add(id);
        if (!resident)
        {
            _opacity[id] = 0f;
            return 0f;
        }

        if (!_opacity.TryGetValue(id, out var o))
        {
            o = 0f;
        }

        if (o < 1f)
        {
            o = Math.Min(1f, o + (dtMs / Math.Max(_durationMs, 1f)));
        }

        _opacity[id] = o;
        return o;
    }

    /// <summary>Drop opacities for tiles not touched since the last <see cref="EndFrame"/>.</summary>
    public void EndFrame()
    {
        if (_opacity.Count == 0)
        {
            _seenThisFrame.Clear();
            return;
        }

        List<SmoothTileId>? drop = null;
        foreach (var kv in _opacity)
        {
            if (_seenThisFrame.Contains(kv.Key))
            {
                continue;
            }

            drop ??= new List<SmoothTileId>();
            drop.Add(kv.Key);
        }

        if (drop is not null)
        {
            foreach (var id in drop)
            {
                _ = _opacity.Remove(id);
            }
        }

        _seenThisFrame.Clear();
    }

    public bool TryGet(SmoothTileId id, out float opacity) => _opacity.TryGetValue(id, out opacity);
}
