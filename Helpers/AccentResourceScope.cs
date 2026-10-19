namespace Palace.Helpers;

/// <summary>
/// WinUI theme lookup for a key that already exists on a dictionary.
/// The active theme dictionary is searched before merged dictionaries, and merged
/// dictionaries are walked last-to-first only after that miss. A dictionary added
/// under <c>XamlControlsResources.MergedDictionaries</c> therefore cannot replace
/// <c>AccentFillColorDefaultBrush</c> — generic.xaml already defines that brush
/// on the XamlControlsResources Default and Light theme dictionaries, and those
/// brushes are realized when <c>InitializeComponent</c> loads App.xaml.
/// </summary>
public static class AccentResourceScope
{
    public sealed class Node
    {
        public Dictionary<string, HashSet<string>> ThemeKeys { get; } = new(StringComparer.Ordinal);

        public List<Node> Merged { get; } = new();
    }

    /// <summary>
    /// Dictionary whose theme keys supply <paramref name="key"/> for <paramref name="theme"/>.
    /// Null when nothing in the tree defines it.
    /// </summary>
    public static Node? Find(Node root, string theme, string key)
    {
        if (root.ThemeKeys.TryGetValue(theme, out var own) && own.Contains(key))
        {
            return root;
        }

        for (var i = root.Merged.Count - 1; i >= 0; i--)
        {
            var hit = Find(root.Merged[i], theme, key);
            if (hit is not null)
            {
                return hit;
            }
        }

        return null;
    }
}
