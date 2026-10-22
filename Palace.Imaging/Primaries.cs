namespace Palace.Imaging;

/// <summary>
/// Linear RGB primary conversions into the BT.2020 working space.
/// Matrices are standard D65 Bradford-adapted transforms (public CIE data).
/// </summary>
public static class Primaries
{
    public static void ToBt2020(
        float r,
        float g,
        float b,
        ColorPrimaries source,
        out float or,
        out float og,
        out float ob)
    {
        switch (source)
        {
            case ColorPrimaries.Bt709:
                // BT.709 → BT.2020 (D65).
                or = (0.6274040f * r) + (0.3292820f * g) + (0.0433136f * b);
                og = (0.0690970f * r) + (0.9195400f * g) + (0.0113610f * b);
                ob = (0.0163916f * r) + (0.0880130f * g) + (0.8955950f * b);
                return;

            case ColorPrimaries.DisplayP3:
                // Display P3 → BT.2020 (D65).
                or = (0.7538330f * r) + (0.1985970f * g) + (0.0475700f * b);
                og = (0.0457440f * r) + (0.9417770f * g) + (0.0124790f * b);
                ob = (-0.0012103f * r) + (0.0176020f * g) + (0.9836090f * b);
                return;

            default:
                or = r;
                og = g;
                ob = b;
                return;
        }
    }
}
