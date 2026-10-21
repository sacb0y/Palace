using System.Collections.ObjectModel;

namespace Palace.Helpers;

/// <summary>
/// Reorder / insert / remove in an <see cref="ObservableCollection{T}"/> without
/// Clear()+rebuild so GridView containers and decoded thumbs keep identity.
/// WinUI-free for Palace.Tests.
/// </summary>
public static class StableIdentityList
{
    public static void Sync<T>(
        ObservableCollection<T> target,
        IReadOnlyList<T> desired,
        Func<T, string> keySelector)
        where T : class
    {
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(desired);
        ArgumentNullException.ThrowIfNull(keySelector);

        var desiredKeys = new HashSet<string>(
            desired.Select(keySelector),
            StringComparer.OrdinalIgnoreCase);

        for (var i = target.Count - 1; i >= 0; i--)
        {
            if (!desiredKeys.Contains(keySelector(target[i])))
            {
                target.RemoveAt(i);
            }
        }

        for (var i = 0; i < desired.Count; i++)
        {
            var want = desired[i];
            var wantKey = keySelector(want);
            if (i < target.Count
                && string.Equals(keySelector(target[i]), wantKey, StringComparison.OrdinalIgnoreCase))
            {
                if (!ReferenceEquals(target[i], want))
                {
                    target[i] = want;
                }

                continue;
            }

            var found = -1;
            for (var j = i; j < target.Count; j++)
            {
                if (string.Equals(keySelector(target[j]), wantKey, StringComparison.OrdinalIgnoreCase))
                {
                    found = j;
                    break;
                }
            }

            if (found >= 0)
            {
                target.Move(found, i);
                if (!ReferenceEquals(target[i], want))
                {
                    target[i] = want;
                }
            }
            else
            {
                target.Insert(i, want);
            }
        }

        while (target.Count > desired.Count)
        {
            target.RemoveAt(target.Count - 1);
        }
    }
}
