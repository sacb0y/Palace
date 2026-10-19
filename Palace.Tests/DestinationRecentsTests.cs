using Palace.Helpers;
using Xunit;

namespace Palace.Tests;

public sealed class DestinationRecentsTests
{
    private static readonly string Root = Path.Combine(Path.GetTempPath(), "recents-root");

    private static string P(params string[] parts) => Path.Combine([Root, .. parts]);

    [Fact]
    public void Push_puts_newest_first_and_drops_an_older_copy_ignoring_case()
    {
        var list = DestinationRecents.Push([P("a"), P("b")], P("B"));

        Assert.Equal([P("B"), P("a")], list);
    }

    [Fact]
    public void Push_caps_the_list()
    {
        IReadOnlyList<string> list = [];
        for (var i = 0; i < 12; i++)
        {
            list = DestinationRecents.Push(list, P($"f{i}"));
        }

        Assert.Equal(DestinationRecents.Max, list.Count);
        Assert.Equal(P("f11"), list[0]);
    }

    [Fact]
    public void Serialize_and_Parse_round_trip_and_ignore_junk()
    {
        var text = DestinationRecents.Serialize([P("a"), P("b") + Path.DirectorySeparatorChar]);

        Assert.Equal([P("a"), P("b")], DestinationRecents.Parse(text));
        Assert.Empty(DestinationRecents.Parse(null));
        Assert.Empty(DestinationRecents.Parse(42));
        Assert.Empty(DestinationRecents.Parse("  \n "));
    }

    [Fact]
    public void Rebase_follows_a_rename_and_drops_a_delete()
    {
        IReadOnlyList<string> list = [P("Old"), P("Old", "Sub"), P("Other")];

        Assert.Equal([P("New"), P("New", "Sub"), P("Other")], DestinationRecents.Rebase(list, P("Old"), P("New")));
        Assert.Equal([P("Other")], DestinationRecents.Rebase(list, P("Old"), null));
    }

    [Fact]
    public void Visible_keeps_existing_folders_inside_watched_roots()
    {
        var exists = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { P("a"), Path.Combine(Path.GetTempPath(), "elsewhere") };

        var visible = DestinationRecents.Visible(
            [P("a"), P("gone"), Path.Combine(Path.GetTempPath(), "elsewhere")],
            [Root],
            exists.Contains);

        Assert.Equal([P("a")], visible);
    }
}
