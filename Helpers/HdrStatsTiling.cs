namespace Palace.Helpers;

/// <summary>
/// Full-image luminance stats without one gigabyte decode: full-width strips
/// at native resolution (CIE Y / MaxCLL are not downscaled), merged here.
/// </summary>
internal static class HdrStatsTiling
{
    /// <summary>Pixels per strip: 16 MP is 128 MB as Rgba16, 48 MB as P010.</summary>
    public const int MaxStripPixels = 16 * 1024 * 1024;

    /// <summary>Strip starts/heights even so 4:2:0 chroma stays aligned.</summary>
    public const int RowAlign = 2;

    public static IReadOnlyList<(int Y, int Height)> PlanStrips(
        int width,
        int height,
        int maxPixels = MaxStripPixels)
    {
        if (width <= 0 || height <= 0)
        {
            return [];
        }

        var rows = Math.Max(RowAlign, maxPixels / width);
        rows -= rows % RowAlign;
        if (rows >= height)
        {
            return [(0, height)];
        }

        var strips = new List<(int, int)>();
        for (var y = 0; y < height; y += rows)
        {
            strips.Add((y, Math.Min(rows, height - y)));
        }

        return strips;
    }
}

/// <summary>Merges per-strip CIE Y / MaxCLL partials into the whole-image stats.</summary>
internal sealed class HdrStatsAccumulator
{
    private float _maxY;
    private float _minY = float.MaxValue;
    private double _sumY;
    private long _count;
    private float _maxScrgb;

    public long Count => _count;

    public void Add(float maxY, float minY, double sumY, long count, float maxScrgb)
    {
        if (count <= 0)
        {
            return;
        }

        _maxY = Math.Max(_maxY, maxY);
        _minY = Math.Min(_minY, minY);
        _sumY += sumY;
        _count += count;
        _maxScrgb = Math.Max(_maxScrgb, maxScrgb);
    }

    public (float MaxNits, float AvgNits, float MinNits, float MaxScrgb)? Result()
    {
        if (_count <= 0)
        {
            return null;
        }

        var max = _maxY;
        var min = _minY;
        if (max <= 0)
        {
            max = 0;
            min = 0;
        }
        else if (min == float.MaxValue)
        {
            min = 0;
        }

        return (max, (float)(_sumY / _count), min, _maxScrgb);
    }
}
