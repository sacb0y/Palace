using Palace.Models;

namespace Palace.Helpers;

/// <summary>
/// T8 inspector: decide whether a union chip belongs under Applied vs Implied-by-rules.
/// Pure — off WinUI.
/// </summary>
public static class AssignedTagPartition
{
    /// <summary>
    /// True only when every source on the union chip is <see cref="TagSource.Implied"/>.
    /// Mixed Manual+Implied (or any applied + Implied) stays in the primary Assigned list.
    /// </summary>
    public static bool IsImpliedOnly(IReadOnlyCollection<TagSource>? sources) =>
        sources is { Count: > 0 } && sources.All(s => s == TagSource.Implied);

    /// <summary>
    /// Single-source chips keep that source. Mixed chips prefer a non-Implied source
    /// (Manual first by enum order) so remove / opacity / tooltip chrome stay applied-side.
    /// </summary>
    public static TagSource EffectiveSource(
        IReadOnlyCollection<TagSource>? sources,
        TagSource sample)
    {
        if (sources is null || sources.Count == 0)
        {
            return sample;
        }

        if (sources.Count == 1)
        {
            return sources.First();
        }

        TagSource? best = null;
        foreach (var source in sources)
        {
            if (source == TagSource.Implied)
            {
                continue;
            }

            if (best is null || (int)source < (int)best.Value)
            {
                best = source;
            }
        }

        return best ?? TagSource.Implied;
    }
}
