using Palace.Helpers;
using Xunit;

namespace Palace.Tests;

public sealed class FolderGroupsTests
{
    [Fact]
    public void ShouldGroup_OnlyTopLevelFolderBrowse()
    {
        Assert.True(FolderGroups.ShouldGroup(false, false, true));
        Assert.False(FolderGroups.ShouldGroup(true, false, true));
        Assert.False(FolderGroups.ShouldGroup(false, true, true));
        Assert.False(FolderGroups.ShouldGroup(false, false, false));
    }

    [Fact]
    public void IsTopLevelFolder_MatchesSourceRoots()
    {
        var roots = new[]
        {
            Path.Combine("library", "Photos"),
            Path.Combine("library", "Refs")
        };

        Assert.True(FolderGroups.IsTopLevelFolder(roots, Path.Combine("library", "photos")));
        Assert.False(FolderGroups.IsTopLevelFolder(roots, Path.Combine("library", "Photos", "Vacation")));
        Assert.False(FolderGroups.IsTopLevelFolder(roots, null));
        Assert.False(FolderGroups.IsTopLevelFolder(roots, ""));
    }

    [Fact]
    public void Title_UsesSelectedAndUpToTwoSubs()
    {
        Assert.Equal("Photos", FolderGroups.Title("Photos", []));
        Assert.Equal("Photos / Vacation", FolderGroups.Title("Photos", ["Vacation"]));
        Assert.Equal("Photos / Vacation / 2024", FolderGroups.Title("Photos", ["Vacation", "2024"]));
        Assert.Equal(
            "Photos / Vacation / 2024",
            FolderGroups.Title("Photos", ["Vacation", "2024", "July"]));
    }

    [Fact]
    public void RelativeSegments_AcceptsEitherSlash()
    {
        var relative = FolderGroups.RelativeSegments(
            @"C:\Photos",
            @"C:/Photos/Vacation/2024");

        Assert.Equal(["Vacation", "2024"], relative);
        Assert.Empty(FolderGroups.RelativeSegments(@"C:\Photos", @"C:\Photos"));
        Assert.Empty(FolderGroups.RelativeSegments(@"C:\Photos", @"D:\Other"));
    }

    [Fact]
    public void DirectoryOf_DropsFileNameOnEitherSlash()
    {
        Assert.Equal(@"C:\Photos\Vacation", FolderGroups.DirectoryOf(@"C:\Photos\Vacation\beach.jpg"));
        Assert.Equal("cloud/dropbox/Ada/Root/Vacation", FolderGroups.DirectoryOf("cloud/dropbox/Ada/Root/Vacation/a.png"));
        Assert.Equal("", FolderGroups.DirectoryOf("loose.jpg"));
    }

    [Fact]
    public void CombineUnder_JoinsTakenSegments()
    {
        var root = Path.Combine("library", "Photos");
        Assert.Equal(root, FolderGroups.CombineUnder(root, []));
        Assert.Equal(
            Path.Combine(root, "Vacation", "2024"),
            FolderGroups.CombineUnder(root, ["Vacation", "2024", "July"]));
    }

    [Fact]
    public void GroupByTopFolders_CapsAtTwoSubsAndKeepsFileOrder()
    {
        var root = Path.Combine("library", "Photos");
        var items = new[]
        {
            Path.Combine(root, "zeta.jpg"),
            Path.Combine(root, "Vacation", "2024", "July", "b.jpg"),
            Path.Combine(root, "Vacation", "2024", "July", "a.jpg"),
            Path.Combine(root, "Work", "client.png"),
            Path.Combine(root, "Vacation", "c.jpg")
        };

        var groups = FolderGroups.GroupByTopFolders(items, "Photos", root, path => path);

        Assert.Equal(
            ["Photos", "Photos / Vacation", "Photos / Vacation / 2024", "Photos / Work"],
            groups.Select(g => g.Title).ToArray());
        Assert.Equal([Path.Combine(root, "zeta.jpg")], groups[0].Items);
        Assert.Equal([Path.Combine(root, "Vacation", "c.jpg")], groups[1].Items);
        Assert.Equal(
            [
                Path.Combine(root, "Vacation", "2024", "July", "b.jpg"),
                Path.Combine(root, "Vacation", "2024", "July", "a.jpg")
            ],
            groups[2].Items);
        Assert.True(FolderGroups.NeedsHeaders(groups));
    }

    [Fact]
    public void NeedsHeaders_FalseWhenEverythingIsInTheSelectedFolder()
    {
        var root = Path.Combine("library", "Photos");
        var groups = FolderGroups.GroupByTopFolders(
            [Path.Combine(root, "a.jpg"), Path.Combine(root, "b.jpg")],
            "Photos",
            root,
            path => path);

        Assert.False(FolderGroups.NeedsHeaders(groups));
    }

    [Fact]
    public void InterleaveHeaders_InsertsOneHeaderPerGroup()
    {
        var groups = new List<FolderGroup<string>>
        {
            new() { Title = "Photos / A", Items = ["a1", "a2"] },
            new() { Title = "Photos / B", Items = ["b1"] }
        };

        var flat = FolderGroups.InterleaveHeaders(groups, group => "H:" + group.Title);
        Assert.Equal(["H:Photos / A", "a1", "a2", "H:Photos / B", "b1"], flat);
    }
}
