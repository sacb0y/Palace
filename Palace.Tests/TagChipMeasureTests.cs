using Palace.Helpers;
using Xunit;

namespace Palace.Tests;

/// <summary>Wimbly #211: TagChip face width after one GrdRoot measure.</summary>
public sealed class TagChipMeasureTests
{
    [Fact]
    public void FaceWidth_LongLabel_AtLeastTextDesired_WhenWide()
    {
        // Proxy for BrdChip.DesiredSize after unconstrained measure (WinUI-free).
        var textDesired = TagChipChrome.WrapWidth("StudioChip211");
        Assert.True(textDesired > TagChipChrome.WrapWidth("StudioChip"));

        Assert.True(
            TagChipMeasure.FaceWidth(double.PositiveInfinity, textDesired) >= textDesired);
        Assert.True(
            TagChipMeasure.FaceWidth(2000, textDesired) >= textDesired);
    }

    [Fact]
    public void FaceWidth_Infinity_NonZero()
    {
        Assert.Equal(120, TagChipMeasure.FaceWidth(double.PositiveInfinity, 120));
        Assert.Equal(1, TagChipMeasure.FaceWidth(double.PositiveInfinity, 0));
        Assert.Equal(1, TagChipMeasure.FaceWidth(double.NaN, 0));
        Assert.Equal(1, TagChipMeasure.FaceWidth(-1, 0));
    }

    [Fact]
    public void FaceWidth_FiniteNarrow_Clips()
    {
        var longFace = TagChipChrome.WrapWidth("StudioChip211");
        Assert.True(longFace > 40);
        Assert.Equal(40, TagChipMeasure.FaceWidth(40, longFace));
        Assert.Equal(longFace, TagChipMeasure.FaceWidth(longFace + 80, longFace));
    }

    [Fact]
    public void NeedsUnconstrainedRemeasure_OnlyWhenInfinityAndZeroChip()
    {
        Assert.True(TagChipMeasure.NeedsUnconstrainedRemeasure(double.PositiveInfinity, 0));
        Assert.False(TagChipMeasure.NeedsUnconstrainedRemeasure(double.PositiveInfinity, 80));
        Assert.False(TagChipMeasure.NeedsUnconstrainedRemeasure(400, 0));
        Assert.False(TagChipMeasure.NeedsUnconstrainedRemeasure(40, 200));
    }

    [Fact]
    public void MainWindow_ShellBackdrop_HitTestFalse_KeepsName()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Palace.csproj")))
        {
            dir = dir.Parent;
        }

        Assert.NotNull(dir);
        var xaml = File.ReadAllText(Path.Combine(dir!.FullName, "MainWindow.xaml"));
        var id = xaml.IndexOf("x:Name=\"GrdShellBackdrop\"", StringComparison.Ordinal);
        Assert.True(id >= 0);
        var end = xaml.IndexOf(">", id, StringComparison.Ordinal);
        var open = xaml[id..end];
        Assert.Contains("IsHitTestVisible=\"False\"", open, StringComparison.Ordinal);
        Assert.Contains("AutomationProperties.Name=\"Shell wallpaper\"", open, StringComparison.Ordinal);
        Assert.DoesNotContain("IsHitTestVisible=\"True\"", open, StringComparison.Ordinal);
    }
}
