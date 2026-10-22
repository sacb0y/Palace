namespace Palace.Imaging;

/// <summary>
/// Optional OpenEXR-style chromaticities (CIE xy for R,G,B,W).
/// When absent, LinearScene defaults to BT.709 (documented limit until the
/// decoder passes the attribute through — M3).
/// </summary>
public readonly record struct Chromaticities(
    float Rx, float Ry,
    float Gx, float Gy,
    float Bx, float By,
    float Wx, float Wy)
{
    public static Chromaticities Bt709 { get; } = new(
        0.64f, 0.33f,
        0.30f, 0.60f,
        0.15f, 0.06f,
        0.3127f, 0.3290f);

    public static Chromaticities Bt2020 { get; } = new(
        0.708f, 0.292f,
        0.170f, 0.797f,
        0.131f, 0.046f,
        0.3127f, 0.3290f);

    /// <summary>Match known sets; otherwise null → caller keeps enum primaries.</summary>
    public ColorPrimaries? MatchKnown(float tol = 0.002f)
    {
        if (Near(Bt709, tol))
        {
            return ColorPrimaries.Bt709;
        }

        if (Near(Bt2020, tol))
        {
            return ColorPrimaries.Bt2020;
        }

        return null;
    }

    private bool Near(Chromaticities o, float tol) =>
        Math.Abs(Rx - o.Rx) <= tol && Math.Abs(Ry - o.Ry) <= tol
        && Math.Abs(Gx - o.Gx) <= tol && Math.Abs(Gy - o.Gy) <= tol
        && Math.Abs(Bx - o.Bx) <= tol && Math.Abs(By - o.By) <= tol
        && Math.Abs(Wx - o.Wx) <= tol && Math.Abs(Wy - o.Wy) <= tol;
}
