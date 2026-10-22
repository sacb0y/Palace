namespace Palace.Imaging;

/// <summary>
/// CIEDE2000 on BT.2020 linear working-space RGB (1.0 = ref white), converted
/// via D65 XYZ → Lab. Used by imaging tests; not a full CMS.
/// </summary>
public static class DeltaE2000
{
    public static double FromWorkingRgb(
        float r1, float g1, float b1,
        float r2, float g2, float b2)
    {
        WorkingToLab(r1, g1, b1, out var L1, out var a1, out var bLab1);
        WorkingToLab(r2, g2, b2, out var L2, out var a2, out var bLab2);
        return CieDe2000(L1, a1, bLab1, L2, a2, bLab2);
    }

    public static void WorkingToLab(float r, float g, float b, out double L, out double a, out double bb)
    {
        // BT.2020 RGB → XYZ (D65), then XYZ → Lab.
        // Matrix from Rec. ITU-R BT.2020-2 (linear RGB → XYZ).
        var x = (0.6369580 * r) + (0.1446169 * g) + (0.1688809 * b);
        var y = (0.2627002 * r) + (0.6779981 * g) + (0.0593017 * b);
        var z = (0.0000000 * r) + (0.0280727 * g) + (1.0609851 * b);
        XyzToLab(x, y, z, out L, out a, out bb);
    }

    private static void XyzToLab(double x, double y, double z, out double L, out double a, out double b)
    {
        // D65 white
        const double xn = 0.95047;
        const double yn = 1.00000;
        const double zn = 1.08883;
        var fx = LabF(x / xn);
        var fy = LabF(y / yn);
        var fz = LabF(z / zn);
        L = (116.0 * fy) - 16.0;
        a = 500.0 * (fx - fy);
        b = 200.0 * (fy - fz);
    }

    private static double LabF(double t)
    {
        const double d = 6.0 / 29.0;
        return t > (d * d * d) ? Math.Pow(t, 1.0 / 3.0) : ((t / (3.0 * d * d)) + (4.0 / 29.0));
    }

    /// <summary>Sharma et al. CIEDE2000.</summary>
    public static double CieDe2000(double L1, double a1, double b1, double L2, double a2, double b2)
    {
        const double kL = 1, kC = 1, kH = 1;
        var c1 = Math.Sqrt((a1 * a1) + (b1 * b1));
        var c2 = Math.Sqrt((a2 * a2) + (b2 * b2));
        var cBar = (c1 + c2) / 2.0;
        var cBar7 = Math.Pow(cBar, 7);
        var g = 0.5 * (1.0 - Math.Sqrt(cBar7 / (cBar7 + Math.Pow(25.0, 7))));
        var a1p = (1.0 + g) * a1;
        var a2p = (1.0 + g) * a2;
        var c1p = Math.Sqrt((a1p * a1p) + (b1 * b1));
        var c2p = Math.Sqrt((a2p * a2p) + (b2 * b2));
        var h1p = Hab(b1, a1p);
        var h2p = Hab(b2, a2p);
        var dLp = L2 - L1;
        var dCp = c2p - c1p;
        var dhp = DeltaHp(c1p, c2p, h1p, h2p);
        var dHp = 2.0 * Math.Sqrt(c1p * c2p) * Math.Sin(DegToRad(dhp) / 2.0);
        var lBar = (L1 + L2) / 2.0;
        var cBarP = (c1p + c2p) / 2.0;
        var hBarP = Hbar(h1p, h2p, c1p, c2p);
        var t = 1.0
            - (0.17 * Math.Cos(DegToRad(hBarP - 30.0)))
            + (0.24 * Math.Cos(DegToRad(2.0 * hBarP)))
            + (0.32 * Math.Cos(DegToRad((3.0 * hBarP) + 6.0)))
            - (0.20 * Math.Cos(DegToRad((4.0 * hBarP) - 63.0)));
        var dTheta = 30.0 * Math.Exp(-Math.Pow((hBarP - 275.0) / 25.0, 2));
        var rC = 2.0 * Math.Sqrt(Math.Pow(cBarP, 7) / (Math.Pow(cBarP, 7) + Math.Pow(25.0, 7)));
        var sL = 1.0 + ((0.015 * Math.Pow(lBar - 50.0, 2)) / Math.Sqrt(20.0 + Math.Pow(lBar - 50.0, 2)));
        var sC = 1.0 + (0.045 * cBarP);
        var sH = 1.0 + (0.015 * cBarP * t);
        var rT = -Math.Sin(DegToRad(2.0 * dTheta)) * rC;
        var dE = Math.Sqrt(
            Math.Pow(dLp / (kL * sL), 2)
            + Math.Pow(dCp / (kC * sC), 2)
            + Math.Pow(dHp / (kH * sH), 2)
            + (rT * (dCp / (kC * sC)) * (dHp / (kH * sH))));
        return dE;
    }

    private static double Hab(double b, double ap)
    {
        if (Math.Abs(ap) < 1e-12 && Math.Abs(b) < 1e-12)
        {
            return 0;
        }

        var h = RadToDeg(Math.Atan2(b, ap));
        return h >= 0 ? h : h + 360.0;
    }

    private static double DeltaHp(double c1, double c2, double h1, double h2)
    {
        if (c1 * c2 == 0)
        {
            return 0;
        }

        var dh = h2 - h1;
        if (dh > 180)
        {
            dh -= 360;
        }
        else if (dh < -180)
        {
            dh += 360;
        }

        return dh;
    }

    private static double Hbar(double h1, double h2, double c1, double c2)
    {
        if (c1 * c2 == 0)
        {
            return h1 + h2;
        }

        var dh = Math.Abs(h1 - h2);
        if (dh <= 180)
        {
            return (h1 + h2) / 2.0;
        }

        return (h1 + h2 + (h1 + h2 < 360 ? 360 : -360)) / 2.0;
    }

    private static double DegToRad(double d) => d * Math.PI / 180.0;
    private static double RadToDeg(double r) => r * 180.0 / Math.PI;
}
