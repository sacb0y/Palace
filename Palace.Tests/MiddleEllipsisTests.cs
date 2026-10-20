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
    public void FormatMetaRight_IncludesFormatAndFrame()
    {
        var right = ViewerInfoFields.FormatMetaRight("sky.exr", null, isHdr: true, "1 / 44");
        Assert.Equal("EXR · 1 / 44", right);
        Assert.Equal("GIF · 12 frames", ViewerInfoFields.FormatMetaRight("a.gif", 12, false, null));
    }

    [Fact]
    public void FileMetaLine_JoinsWithPipe()
    {
        Assert.Equal(
            "23 MB · 3840×2160 | EXR · 1 / 44",
            ViewerInfoFields.FileMetaLine(23L * 1024 * 1024, 3840, 2160, "sky.exr", null, true, "1 / 44"));
        Assert.Equal("", ViewerInfoFields.FileMetaLine(null, null, null, null, null, false, null));
    }

    [Fact]
    public void HasHdrStatRows_FalseForSdr()
    {
        Assert.False(ViewerInfoFields.HasHdrStatRows(false, false, null, null, null, null, null));
        Assert.True(ViewerInfoFields.HasHdrStatRows(true, true, 12.5f, null, null, null, null));
    }

    [Fact]
    public void HasColorHdrRows_IgnoresGamutOnly()
    {
        Assert.False(ViewerInfoFields.HasColorHdrRows(null, null, false, null, null));
        Assert.True(ViewerInfoFields.HasColorHdrRows("sRGB", null, false, null, null));
        // Gamut summary alone must not keep an empty Color & HDR expander open.
        Assert.False(ViewerInfoFields.HasColorHdrRows("", "", false, "", ""));
    }

    [Fact]
    public void ShortHdrLine_CompactsAndTruncates()
    {
        Assert.Equal("", ViewerInfoFields.ShortHdrLine(null));
        Assert.Equal(
            "OpenEXR · SDR 80 nits",
            ViewerInfoFields.ShortHdrLine("OpenEXR · SDR 80 nits"));
        var longLine = ViewerInfoFields.ShortHdrLine(
            "Very long HDR status that should truncate for the dense grid",
            maxChars: 20);
        Assert.True(longLine.Length <= 20);
        Assert.EndsWith("…", longLine);
    }
}
