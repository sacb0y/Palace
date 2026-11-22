namespace Palace.Helpers;

/// <summary>
/// Clear page-owned VM callbacks on Unloaded. Use delegate == (target+method),
/// never <see cref="object.ReferenceEquals"/> — a fresh cast makes a new
/// delegate instance so ReferenceEquals always misses.
/// </summary>
public static class OwnedDelegate
{
    public static void ClearIfOurs(ref Action? slot, Action ours)
    {
        if (slot == ours)
        {
            slot = null;
        }
    }

    public static void ClearIfOurs<T>(ref Action<T>? slot, Action<T> ours)
    {
        if (slot == ours)
        {
            slot = null;
        }
    }

    public static void ClearIfOurs<T1, T2>(ref Action<T1, T2>? slot, Action<T1, T2> ours)
    {
        if (slot == ours)
        {
            slot = null;
        }
    }

    public static void ClearIfOurs<TResult>(ref Func<TResult>? slot, Func<TResult> ours)
    {
        if (slot == ours)
        {
            slot = null;
        }
    }
}
