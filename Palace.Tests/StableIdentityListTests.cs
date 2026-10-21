using System.Collections.ObjectModel;
using Palace.Helpers;
using Xunit;

namespace Palace.Tests;

public sealed class StableIdentityListTests
{
    private sealed class Row
    {
        public required string Key { get; init; }
        public string Label { get; set; } = "";
    }

    [Fact]
    public void Sync_ReordersWithoutClear_KeepsInstances()
    {
        var a = new Row { Key = "a", Label = "A" };
        var b = new Row { Key = "b", Label = "B" };
        var c = new Row { Key = "c", Label = "C" };
        var target = new ObservableCollection<Row> { a, b, c };

        StableIdentityList.Sync(target, [c, a, b], r => r.Key);

        Assert.Equal(3, target.Count);
        Assert.Same(c, target[0]);
        Assert.Same(a, target[1]);
        Assert.Same(b, target[2]);
    }

    [Fact]
    public void Sync_InsertsAndRemovesByKey()
    {
        var a = new Row { Key = "a" };
        var b = new Row { Key = "b" };
        var target = new ObservableCollection<Row> { a, b };
        var d = new Row { Key = "d" };

        StableIdentityList.Sync(target, [d, a], r => r.Key);

        Assert.Equal(2, target.Count);
        Assert.Same(d, target[0]);
        Assert.Same(a, target[1]);
        Assert.DoesNotContain(b, target);
    }
}
