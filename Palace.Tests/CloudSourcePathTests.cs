using Palace.Helpers;
using Palace.Models;
using Xunit;

namespace Palace.Tests;

public sealed class CloudSourcePathTests
{
    [Fact]
    public void Build_UsesProviderAccountAndFolder()
    {
        var path = CloudSourcePath.Build(CloudProvider.Dropbox, "Ada Lovelace", "Photos/Project");
        Assert.Equal(Path.Combine("cloud", "dropbox", "Ada_Lovelace", "Photos", "Project"), path);
    }

    [Fact]
    public void Build_RootFolder_IsRootSegment()
    {
        var path = CloudSourcePath.Build(CloudProvider.OneDrive, "me@example.com", null);
        Assert.Equal(Path.Combine("cloud", "onedrive", "me_example.com", "Root"), path);
    }

    [Fact]
    public void NormalizeRootItemId_EmptyBecomesNull()
    {
        Assert.Null(CloudSourcePath.NormalizeRootItemId(""));
        Assert.Null(CloudSourcePath.NormalizeRootItemId("   "));
        Assert.Null(CloudSourcePath.NormalizeRootItemId(null));
        Assert.Equal("id:abc", CloudSourcePath.NormalizeRootItemId(" id:abc "));
    }

    [Fact]
    public void PathTaken_IsCaseInsensitive()
    {
        var existing = new[] { Path.Combine("cloud", "onedrive", "Ada", "Photos") };
        Assert.True(CloudSourcePath.PathTaken(Path.Combine("cloud", "onedrive", "ada", "photos"), existing));
        Assert.False(CloudSourcePath.PathTaken(Path.Combine("cloud", "onedrive", "Ada", "Other"), existing));
    }

    [Fact]
    public void SameCloudFolder_TreatsEmptyRootAsEqual()
    {
        Assert.True(CloudSourcePath.SameCloudFolder("acct", null, "acct", ""));
        Assert.True(CloudSourcePath.SameCloudFolder("acct", "id:1", "acct", "id:1"));
        Assert.False(CloudSourcePath.SameCloudFolder("acct", "id:1", "acct", "id:2"));
        Assert.False(CloudSourcePath.SameCloudFolder("a", "id:1", "b", "id:1"));
    }

    [Fact]
    public void FolderDisplay_DefaultsToRoot()
    {
        Assert.Equal("Root", CloudSourcePath.FolderDisplay(null));
        Assert.Equal("Photos", CloudSourcePath.FolderDisplay(" Photos "));
    }
}
