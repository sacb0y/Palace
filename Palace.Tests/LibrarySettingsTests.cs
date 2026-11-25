using Palace.Helpers;
using Xunit;

namespace Palace.Tests;

public sealed class LibrarySettingsTests
{
    public LibrarySettingsTests()
    {
        // Isolate static Apply state per test class run.
        LibrarySettings.Apply(
            LibrarySettings.DefaultWatchForChanges,
            LibrarySettings.DefaultIndexImages,
            LibrarySettings.DefaultIndexVideo,
            LibrarySettings.DefaultGenerateThumbsOnScan,
            LibrarySettings.DefaultIndexModel3D);
    }

    [Fact]
    public void ParseBool_DefaultsAndStrings()
    {
        Assert.True(LibrarySettings.ParseBool(null, true));
        Assert.False(LibrarySettings.ParseBool(null, false));
        Assert.True(LibrarySettings.ParseBool(true, false));
        Assert.False(LibrarySettings.ParseBool("false", true));
        Assert.True(LibrarySettings.ParseBool("True", false));
    }

    [Fact]
    public void Apply_UpdatesStaticGates()
    {
        LibrarySettings.Apply(
            watchForChanges: false,
            indexImages: true,
            indexVideo: false,
            generateThumbsOnScan: false,
            indexModel3D: true);
        Assert.False(LibrarySettings.WatchForChanges);
        Assert.True(LibrarySettings.IndexImages);
        Assert.False(LibrarySettings.IndexVideo);
        Assert.True(LibrarySettings.IndexModel3D);
        Assert.False(LibrarySettings.GenerateThumbsOnScan);
    }

    [Fact]
    public void IsIndexedExt_HonorsImageAndVideoToggles()
    {
        LibrarySettings.Apply(true, indexImages: true, indexVideo: true, generateThumbsOnScan: true, indexModel3D: false);
        Assert.True(LibrarySettings.IsIndexedExt(".png"));
        Assert.True(LibrarySettings.IsIndexedExt(".gif"));
        Assert.True(LibrarySettings.IsIndexedExt(".mp4"));
        Assert.False(LibrarySettings.IsIndexedExt(".txt"));
        Assert.False(LibrarySettings.IsIndexedExt(".glb"));

        LibrarySettings.Apply(true, indexImages: false, indexVideo: true, generateThumbsOnScan: true, indexModel3D: false);
        Assert.False(LibrarySettings.IsIndexedExt(".png"));
        Assert.False(LibrarySettings.IsIndexedExt(".gif"));
        Assert.True(LibrarySettings.IsIndexedExt(".mp4"));

        LibrarySettings.Apply(true, indexImages: true, indexVideo: false, generateThumbsOnScan: true, indexModel3D: false);
        Assert.True(LibrarySettings.IsIndexedExt("photo.avif"));
        Assert.False(LibrarySettings.IsIndexedExt("clip.mov"));
    }

    [Fact]
    public void FormatCaptions_MentionCurrentKindsAndFutureRoom()
    {
        Assert.Contains("JPG", LibrarySettings.ImagesFormatsCaption, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("MP4", LibrarySettings.VideoFormatsCaption, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("GLB", LibrarySettings.Model3DFormatsCaption, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("audio", LibrarySettings.FormatsPlaceholderCaption, StringComparison.OrdinalIgnoreCase);
    }
}
