using Palace.Helpers;
using Xunit;

namespace Palace.Tests;

public sealed class SmoothExifOrientTests
{
    [Theory]
    [InlineData(1, 100, 50, 100, 50)]
    [InlineData(6, 100, 50, 50, 100)]
    [InlineData(8, 100, 50, 50, 100)]
    [InlineData(3, 100, 50, 100, 50)]
    public void FromExif_OrientedSize(int orient, int sw, int sh, int ow, int oh)
    {
        var e = SmoothViewport.ExifOrient.FromExif(orient, sw, sh);
        Assert.Equal(ow, e.OrientedWidth);
        Assert.Equal(oh, e.OrientedHeight);
    }

    [Fact]
    public void Rotate90_CyclesCardinals()
    {
        Assert.Equal(6, SmoothViewport.ExifOrient.Rotate90(1, clockwise: true));
        Assert.Equal(3, SmoothViewport.ExifOrient.Rotate90(6, clockwise: true));
        Assert.Equal(8, SmoothViewport.ExifOrient.Rotate90(3, clockwise: true));
        Assert.Equal(1, SmoothViewport.ExifOrient.Rotate90(8, clockwise: true));
        Assert.Equal(8, SmoothViewport.ExifOrient.Rotate90(1, clockwise: false));
    }

    [Fact]
    public void FromZoomPan_ComposesOrientTranslation()
    {
        var orient = SmoothViewport.ExifOrient.FromExif(6, storedW: 100, storedH: 50);
        var m = SmoothViewport.Affine2x3.FromZoomPan(2f, tx: 10f, ty: 20f, orient);
        // zoom * R + pan; storage (0,0) → upright (49,0) at orient, then *2 + pan
        var (x, y) = m.Transform(0, 0);
        Assert.Equal((2f * orient.Tx) + 10f, x, precision: 3);
        Assert.Equal((2f * orient.Ty) + 20f, y, precision: 3);
    }
}
