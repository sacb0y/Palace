using Palace.Helpers;
using Xunit;

namespace Palace.Tests;

public sealed class MiddleEllipsisTests
{
    [Fact]
    public void Truncate_KeepsShortNames()
    {
        Assert.Equal("shot.png", MiddleEllipsis.Truncate("shot.png"));
        Assert.Equal("", MiddleEllipsis.Truncate(null));
        Assert.Equal("", MiddleEllipsis.Truncate(""));
    }

    [Fact]
    public void Truncate_KeepsExtensionInMiddle()
    {
        var name = "naomi_wardrobe_concept_final_pass_v3.psd";
        var truncated = MiddleEllipsis.Truncate(name, 28);
        Assert.EndsWith(".psd", truncated);
        Assert.Contains('…', truncated);
        Assert.True(truncated.Length <= 28);
    }

    [Fact]
    public void Truncate_LongPath_KeepsLeafExtension()
    {
        var path = @"C:\Users\isiac\Pictures\ref\naomi_wardrobe_concept_final_pass_v3.exr";
        var truncated = MiddleEllipsis.Truncate(path, 40);
        Assert.EndsWith(".exr", truncated);
        Assert.Contains('…', truncated);
    }
}

public sealed class ViewerInfoFieldsTests
{
    [Fact]
    public void FileMetaLeft_JoinsSizeAndResolution()
    {
        Assert.Equal("23 MB · 3840×2160", ViewerInfoFields.FileMetaLeft(23L * 1024 * 1024, 3840, 2160));
        Assert.Equal("", ViewerInfoFields.FileMetaLeft(null, null, null));
        Assert.Equal("1920×1080", ViewerInfoFields.FileMetaLeft(null, 1920, 1080));
    }

    [Fact]
    public void FormatMetaRight_HdrIncludesPosition()
    {
        var right = ViewerInfoFields.FormatMetaRight("sky.exr", null, isHdr: true, "1 / 44");
        Assert.Contains("EXR", right);
        Assert.Contains("HDR", right);
        Assert.Contains("1 / 44", right);
    }

    [Fact]
    public void HasHdrStatRows_FalseForSdr()
    {
        Assert.False(ViewerInfoFields.HasHdrStatRows(false, false, null, null, null, null, null));
        Assert.True(ViewerInfoFields.HasHdrStatRows(true, true, 12.5f, null, null, null, null));
    }
}
