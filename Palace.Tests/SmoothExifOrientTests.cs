using Palace.Helpers;
using Xunit;

namespace Palace.Tests;

public sealed class SmoothExifOrientTests
{
    /// <summary>EXIF 1–8: FromExif oriented size ≡ HdrPixels.OrientedSize (strip R/fit).</summary>
    [Theory]
    [InlineData(1, 100, 50, 100, 50)]
    [InlineData(2, 100, 50, 100, 50)]
    [InlineData(3, 100, 50, 100, 50)]
    [InlineData(4, 100, 50, 100, 50)]
    [InlineData(5, 100, 50, 50, 100)]
    [InlineData(6, 100, 50, 50, 100)]
    [InlineData(7, 100, 50, 50, 100)]
    [InlineData(8, 100, 50, 50, 100)]
    public void FromExif_OrientedSize(int orient, int sw, int sh, int ow, int oh)
    {
        var e = SmoothViewport.ExifOrient.FromExif(orient, sw, sh);
        Assert.Equal(ow, e.OrientedWidth);
        Assert.Equal(oh, e.OrientedHeight);
        Assert.Equal(HdrPixels.OrientedSize(sw, sh, (uint)orient), (e.OrientedWidth, e.OrientedHeight));
    }

    [Fact]
    public void Rotate90_PlainCycle_1_6_3_8()
    {
        // Wimbly: plain 1→6→3→8→1
        Assert.Equal(6, SmoothViewport.ExifOrient.Rotate90(1, clockwise: true));
        Assert.Equal(3, SmoothViewport.ExifOrient.Rotate90(6, clockwise: true));
        Assert.Equal(8, SmoothViewport.ExifOrient.Rotate90(3, clockwise: true));
        Assert.Equal(1, SmoothViewport.ExifOrient.Rotate90(8, clockwise: true));
        Assert.Equal(8, SmoothViewport.ExifOrient.Rotate90(1, clockwise: false));
        Assert.Equal(3, SmoothViewport.ExifOrient.Rotate90(8, clockwise: false));
    }

    [Fact]
    public void Rotate90_MirroredCycle_2_7_4_5()
    {
        // Wimbly: mirrored 2→7→4→5→2 (not snap-to-plain)
        Assert.Equal(7, SmoothViewport.ExifOrient.Rotate90(2, clockwise: true));
        Assert.Equal(4, SmoothViewport.ExifOrient.Rotate90(7, clockwise: true));
        Assert.Equal(5, SmoothViewport.ExifOrient.Rotate90(4, clockwise: true));
        Assert.Equal(2, SmoothViewport.ExifOrient.Rotate90(5, clockwise: true));
        Assert.Equal(5, SmoothViewport.ExifOrient.Rotate90(2, clockwise: false));
        Assert.Equal(4, SmoothViewport.ExifOrient.Rotate90(5, clockwise: false));
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    [InlineData(6)]
    [InlineData(7)]
    [InlineData(8)]
    public void Rotate90_OnStrip_OrientedSizeStaysConsistent(int startOrient)
    {
        // Strip keeps storage tiles; R cycles EXIF — oriented size matches OrientedSize.
        const int sw = 200;
        const int sh = 100;
        var o = startOrient;
        for (var step = 0; step < 4; step++)
        {
            var e = SmoothViewport.ExifOrient.FromExif(o, sw, sh);
            Assert.Equal(HdrPixels.OrientedSize(sw, sh, (uint)o), (e.OrientedWidth, e.OrientedHeight));
            o = SmoothViewport.ExifOrient.Rotate90(o, clockwise: true);
        }

        Assert.Equal(startOrient, o); // full cycle
    }

    [Fact]
    public void FromZoomPan_ComposesOrientTranslation()
    {
        var orient = SmoothViewport.ExifOrient.FromExif(6, storedW: 100, storedH: 50);
        var m = SmoothViewport.Affine2x3.FromZoomPan(2f, tx: 10f, ty: 20f, orient);
        // zoom * R + pan; storage (0,0) → upright (50,0) at orient, then *2 + pan
        var (x, y) = m.Transform(0, 0);
        Assert.Equal((2f * orient.Tx) + 10f, x, precision: 3);
        Assert.Equal((2f * orient.Ty) + 20f, y, precision: 3);
    }
}
