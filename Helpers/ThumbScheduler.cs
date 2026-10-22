using System.Diagnostics;

namespace Palace.Helpers;

/// <summary>Axis-aligned slot in mosaic DIPs. Off WinUI so the queue can be tested.</summary>
public readonly record struct ThumbRect(double X, double Y, double Width, double Height)
{
    public double Right => X + Width;

    public double Bottom => Y + Height;

    public bool Intersects(ThumbRect other) =>
        X < other.Right && Right > other.X && Y < other.Bottom && Bottom > other.Y;

    /// <summary>Vertical band past the viewport. The mosaic scrolls vertically.</summary>
    public ThumbRect InflateVertical(double band)
    {
        var pad = Math.Max(0, band);
        return new ThumbRect(X, Y - pad, Width, Height + (pad * 2));
    }
}

public readonly record struct ThumbTile(string Key, double X, double Y, double Width, double Height);

public enum ThumbBand
{
    Visible,
    Prefetch,
    Outside
}

public readonly record struct ThumbPlan(
    IReadOnlyList<string> Start,
    IReadOnlyList<string> Cancel,
    int InFlight,
    int Cap);

/// <summary>
/// Mosaic thumbnail order and decode size. Visible tiles first (top-to-bottom,
/// then left-to-right), then a short prefetch band. Never the file's full resolution.
/// </summary>
public static class ThumbScheduler
{
    public const int MinConcurrency = 2;
    public const int MaxConcurrency = 4;

    /// <summary>About two default mosaic rows past the viewport.</summary>
    public const double PrefetchBandDip = 280;

    /// <summary>Hard ceiling so a layout glitch cannot request a 4K decode (Standard preview).</summary>
    public const int MaxDecodePixels = 2048;

    /// <summary>Active decode ceiling (Standard or High from Performance settings).</summary>
    public static int EffectiveMaxDecodePixels =>
        PerformanceSettings.ResolveMaxDecodePixels();

    /// <summary>Two on a small machine, three at four cores, four at eight or more.</summary>
    public static int ConcurrencyCap(int processorCount)
    {
        var cores = Math.Max(1, processorCount);
        if (cores >= 8)
        {
            return MaxConcurrency;
        }

        if (cores >= 4)
        {
            return 3;
        }

        return MinConcurrency;
    }

    /// <summary>In-flight cap after Performance Settings (Auto / Fewer / More).</summary>
    public static int EffectiveConcurrencyCap(int processorCount) =>
        PerformanceSettings.ResolveConcurrency(processorCount);

    public static int LongEdge(int? width, int? height) => Math.Max(width ?? 0, height ?? 0);

    /// <summary>
    /// Tile or preview size in device pixels: DIP size times display scale, clamped.
    /// Both axes are set so <c>BitmapImage</c> does not decode the file at full resolution.
    /// </summary>
    public static (int Width, int Height) DecodePixels(double dipWidth, double dipHeight, double rasterScale)
    {
        var scale = rasterScale < 1 ? 1 : rasterScale > 3 ? 3 : rasterScale;
        return (ClampPixels(dipWidth * scale), ClampPixels(dipHeight * scale));
    }

    /// <summary>
    /// File to pixel-decode for the side preview. Never a <c>BitmapImage.UriSource</c>.
    /// A still larger than the cap, or an unknown size, uses the on-disk thumb when one exists.
    /// Otherwise the caller scale-decodes this path down to the preview box.
    /// </summary>
    public static string? PreviewSource(string? originalPath, string? diskThumbPath, int sourceLongEdge, int decodeLongEdge)
    {
        if (string.IsNullOrEmpty(originalPath))
        {
            return null;
        }

        if (!string.IsNullOrEmpty(diskThumbPath)
            && (sourceLongEdge <= 0 || (decodeLongEdge > 0 && sourceLongEdge > decodeLongEdge)))
        {
            return diskThumbPath;
        }

        return originalPath;
    }

    /// <summary>
    /// Fit the source inside the preview or tile box. Never upscales, never exceeds
    /// <see cref="EffectiveMaxDecodePixels"/>, and never returns the file's full-resolution size
    /// when that size is larger than the box.
    /// </summary>
    public static (int Width, int Height) FitDecodeSize(int sourceWidth, int sourceHeight, int boxWidth, int boxHeight)
    {
        var ceiling = EffectiveMaxDecodePixels;
        var boxW = ClampPixels(boxWidth);
        var boxH = ClampPixels(boxHeight);
        if (sourceWidth <= 0 || sourceHeight <= 0)
        {
            return (boxW, boxH);
        }

        var scale = Math.Min(boxW / (double)sourceWidth, boxH / (double)sourceHeight);
        if (scale > 1)
        {
            scale = 1;
        }

        var width = Math.Max(1, (int)Math.Round(sourceWidth * scale));
        var height = Math.Max(1, (int)Math.Round(sourceHeight * scale));
        return (Math.Min(width, ceiling), Math.Min(height, ceiling));
    }

    public static ThumbBand BandOf(ThumbRect tile, ThumbRect viewport, double prefetchDip)
    {
        if (tile.Intersects(viewport))
        {
            return ThumbBand.Visible;
        }

        if (tile.Intersects(viewport.InflateVertical(prefetchDip)))
        {
            return ThumbBand.Prefetch;
        }

        return ThumbBand.Outside;
    }

    public static IReadOnlyList<ThumbTile> Order(IReadOnlyList<ThumbTile> tiles, ThumbRect viewport, double prefetchDip)
    {
        var visible = new List<ThumbTile>();
        var prefetch = new List<ThumbTile>();
        foreach (var tile in tiles)
        {
            var band = BandOf(new ThumbRect(tile.X, tile.Y, tile.Width, tile.Height), viewport, prefetchDip);
            if (band == ThumbBand.Visible)
            {
                visible.Add(tile);
            }
            else if (band == ThumbBand.Prefetch)
            {
                prefetch.Add(tile);
            }
        }

        visible.Sort(CompareReadingOrder);
        prefetch.Sort(CompareReadingOrder);
        visible.AddRange(prefetch);
        return visible;
    }

    private static int ClampPixels(double px)
    {
        if (double.IsNaN(px) || double.IsInfinity(px) || px <= 1)
        {
            return 1;
        }

        var n = (int)Math.Round(px);
        if (n < 1)
        {
            return 1;
        }

        var ceiling = EffectiveMaxDecodePixels;
        return n > ceiling ? ceiling : n;
    }

    private static int CompareReadingOrder(ThumbTile a, ThumbTile b)
    {
        if (Math.Abs(a.Y - b.Y) > 1)
        {
            return a.Y.CompareTo(b.Y);
        }

        return a.X.CompareTo(b.X);
    }
}

/// <summary>
/// In-flight cap for thumbnail work. Caller completes or cancels keys.
/// A cancelled key leaves the cap immediately so a slow file cannot block
/// the next visible tile. Late completion of that key is ignored.
/// Not thread-safe; the Library page mutates it on the UI thread.
/// </summary>
public sealed class ThumbLoadQueue
{
    private readonly HashSet<string> _inFlight = new(StringComparer.Ordinal);
    private readonly HashSet<string> _satisfied = new(StringComparer.Ordinal);
    private readonly Dictionary<string, int> _tickets = new(StringComparer.Ordinal);
    private int _nextTicket;

    public ThumbLoadQueue(int cap)
    {
        Cap = Math.Clamp(cap, ThumbScheduler.MinConcurrency, ThumbScheduler.MaxConcurrency);
    }

    public int Cap { get; private set; }

    /// <summary>Updates the in-flight ceiling (Performance Settings Auto / Fewer / More).</summary>
    public void SetCap(int cap) =>
        Cap = Math.Clamp(cap, ThumbScheduler.MinConcurrency, ThumbScheduler.MaxConcurrency);

    public int InFlight => _inFlight.Count;

    public int TicketFor(string key) =>
        !string.IsNullOrEmpty(key) && _tickets.TryGetValue(key, out var ticket) ? ticket : 0;

    public bool IsCurrent(string key, int ticket) =>
        ticket != 0 && _tickets.TryGetValue(key, out var current) && current == ticket;

    public void MarkSatisfied(string key)
    {
        if (string.IsNullOrEmpty(key))
        {
            return;
        }

        _satisfied.Add(key);
        _inFlight.Remove(key);
        _tickets.Remove(key);
    }

    public void Complete(string key) => MarkSatisfied(key);

    /// <summary>Completes the key only when <paramref name="ticket"/> is still the live slot.</summary>
    public bool TryComplete(string key, int ticket)
    {
        if (!IsCurrent(key, ticket))
        {
            return false;
        }

        MarkSatisfied(key);
        return true;
    }

    public void Abandon(string key)
    {
        if (!string.IsNullOrEmpty(key))
        {
            _inFlight.Remove(key);
            _tickets.Remove(key);
        }
    }

    /// <summary>Drops the slot only when <paramref name="ticket"/> is still live.</summary>
    public bool TryAbandon(string key, int ticket)
    {
        if (!IsCurrent(key, ticket))
        {
            return false;
        }

        _inFlight.Remove(key);
        _tickets.Remove(key);
        return true;
    }

    public void Reset()
    {
        _inFlight.Clear();
        _satisfied.Clear();
        _tickets.Clear();
    }

    /// <summary>
    /// Mosaic reset: cancel every in-flight key, free their cap slots, and forget
    /// satisfied ids. Callers cancel the decode tokens for the returned keys.
    /// A later <see cref="TryComplete"/> with the old ticket does nothing.
    /// </summary>
    public IReadOnlyList<string> CancelOutstanding()
    {
        var keys = _inFlight.ToList();
        _inFlight.Clear();
        _tickets.Clear();
        _satisfied.Clear();
        return keys;
    }

    public ThumbPlan Plan(IReadOnlyList<ThumbTile> tiles, ThumbRect viewport, double prefetchDip)
    {
        var ordered = ThumbScheduler.Order(tiles, viewport, prefetchDip);
        var wanted = new HashSet<string>(StringComparer.Ordinal);
        foreach (var tile in ordered)
        {
            if (!string.IsNullOrEmpty(tile.Key))
            {
                wanted.Add(tile.Key);
            }
        }

        // A tile that left the band drops its cap slot now. The decode token is
        // still cancelled; a slow EXR must not keep the 2–4 cap busy.
        var cancel = new List<string>();
        foreach (var key in _inFlight)
        {
            if (!wanted.Contains(key))
            {
                cancel.Add(key);
            }
        }

        foreach (var key in cancel)
        {
            _inFlight.Remove(key);
            _tickets.Remove(key);
        }

        var start = new List<string>();
        foreach (var tile in ordered)
        {
            if (_inFlight.Count >= Cap)
            {
                break;
            }

            if (string.IsNullOrEmpty(tile.Key) || _satisfied.Contains(tile.Key) || _inFlight.Contains(tile.Key))
            {
                continue;
            }

            _tickets[tile.Key] = ++_nextTicket;
            _inFlight.Add(tile.Key);
            start.Add(tile.Key);
        }

        return new ThumbPlan(start, cancel, _inFlight.Count, Cap);
    }
}

public readonly record struct ThumbDecodeOutcome(bool Cancelled, double Milliseconds);

/// <summary>
/// Runs one thumbnail or preview decode. The queue does not keep a cap slot for
/// this task after the key is abandoned; the token still stops the work.
/// </summary>
public static class ThumbDecodeRun
{
    public static async Task<ThumbDecodeOutcome> RunAsync(
        CancellationToken cancellationToken,
        Func<CancellationToken, Task> decode)
    {
        var sw = Stopwatch.StartNew();
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            await decode(cancellationToken).ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
            return new ThumbDecodeOutcome(false, sw.Elapsed.TotalMilliseconds);
        }
        catch (Exception ex) when (ErrorReporter.IsCancellation(ex))
        {
            return new ThumbDecodeOutcome(true, sw.Elapsed.TotalMilliseconds);
        }
    }
}
