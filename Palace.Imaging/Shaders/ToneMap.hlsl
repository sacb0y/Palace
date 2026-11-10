// Palace.Imaging reference HLSL (P5) — CPU–GPU agreement source.
// Must stay in lockstep with ToneMapper.MapScalar / Bt2390Eetf / SdrToneCurve /
// ToneMapLut.MapPixel (nonzero EV bypasses baked LUT → MapScalar).
// Not compiled by the packaged app yet; present still CPU-rasters FP16.
// PipelineVersion = 6. Working 1.0 = 203 nits (BT.2408).

#ifndef PALACE_IMAGING_TONEMAP_HLSL
#define PALACE_IMAGING_TONEMAP_HLSL

static const float RefWhiteNits = 203.0;
static const float PqPeakNits = 10000.0;
static const float MaxPaperDip = 0.10;

float PalaceNitsToPq(float nits)
{
    // ST 2084 OETF on nits/10000 — mirror ColorMath.NitsToPq.
    const float m1 = 2610.0 / 16384.0;
    const float m2 = 2523.0 / 32.0;
    const float c1 = 3424.0 / 4096.0;
    const float c2 = 2413.0 / 128.0;
    const float c3 = 2392.0 / 128.0;
    float y = saturate(nits / PqPeakNits);
    float ym = pow(y, m1);
    return pow((c1 + c2 * ym) / (1.0 + c3 * ym), m2);
}

float PalacePqToNits(float pq)
{
    const float m1 = 2610.0 / 16384.0;
    const float m2 = 2523.0 / 32.0;
    const float c1 = 3424.0 / 4096.0;
    const float c2 = 2413.0 / 128.0;
    const float c3 = 2392.0 / 128.0;
    float v = saturate(pq);
    float vp = pow(v, 1.0 / m2);
    float den = c2 - c3 * vp;
    if (den <= 0.0)
    {
        return 0.0;
    }
    float linear01 = pow(max(vp - c1, 0.0) / den, 1.0 / m1);
    return linear01 * PqPeakNits;
}

// BT.2390-8 §5.4.1 EETF. ld / lc in Working units (nits/203).
float PalaceBt2390MapLinear(float linear, float lc, float ld)
{
    if (lc <= ld + 1e-6)
    {
        return min(linear, lc);
    }

    linear = clamp(linear, 0.0, lc);
    float lb = 0.0;
    float lw = max(lc, 1e-6) * RefWhiteNits;
    float lMax = max(ld, 1e-6) * RefWhiteNits;
    float pqLb = PalaceNitsToPq(lb);
    float pqLw = PalaceNitsToPq(lw);
    float span = max(pqLw - pqLb, 1e-8);
    float e1 = saturate((PalaceNitsToPq(linear * RefWhiteNits) - pqLb) / span);
    float minLum = saturate((PalaceNitsToPq(lb) - pqLb) / span);
    float maxLum = saturate((PalaceNitsToPq(lMax) - pqLb) / span);
    float ks = 1.5 * maxLum - 0.5;
    float b = minLum;
    float e2;
    if (ks >= 1.0 - 1e-6 || e1 < ks)
    {
        e2 = e1;
    }
    else
    {
        float t = saturate((e1 - ks) / (1.0 - ks));
        float t2 = t * t;
        float t3 = t2 * t;
        e2 = ((2.0 * t3) - (3.0 * t2) + 1.0) * ks
            + ((t3 - (2.0 * t2) + t) * (1.0 - ks))
            + (((-2.0 * t3) + (3.0 * t2)) * maxLum);
    }
    e2 = saturate(e2);
    float e3 = saturate(e2 + b * pow(1.0 - e2, 4.0));
    float outLinear = PalacePqToNits((e3 * span) + pqLb) / RefWhiteNits;
    return min(outLinear, ld);
}

float PalacePaperWhiteFraction(float lc)
{
    float L = max(lc, 1.0);
    float u = 1.0 - (1.0 / L);
    float t = u * u;
    return 1.0 - (MaxPaperDip * t);
}

float PalaceHermite(float x, float x0, float y0, float m0, float x1, float y1, float m1)
{
    float dx = x1 - x0;
    if (dx <= 1e-8)
    {
        return y1;
    }
    float t = saturate((x - x0) / dx);
    float t2 = t * t;
    float t3 = t2 * t;
    float M0 = m0 * dx;
    float M1 = m1 * dx;
    return ((2.0 * t3) - (3.0 * t2) + 1.0) * y0
        + ((t3 - (2.0 * t2) + t) * M0)
        + (((-2.0 * t3) + (3.0 * t2)) * y1)
        + ((t3 - t2) * M1);
}

// Option A SDR. displayWhite = PeakNormalized. lc already includes ×2^EV.
float PalaceSdrMap(float x, float lc, float sceneAvg, float displayWhite)
{
    float w = max(displayWhite, 1e-6);
    if (x <= 0.0)
    {
        return 0.0;
    }
    float kneeIn = sceneAvg > 0.0 ? clamp(sceneAvg, 0.5, 0.7) : 0.6;
    float peak = max(lc, max(1.01, kneeIn + 0.05));
    float whiteOut = w * PalacePaperWhiteFraction(lc);
    float peakOut = w;
    float kneeOut = kneeIn * whiteOut;
    float midSlope = whiteOut;
    float whiteSlope = 0.35 * whiteOut;
    if (x <= kneeIn)
    {
        return x * whiteOut;
    }
    if (x <= 1.0)
    {
        return PalaceHermite(x, kneeIn, kneeOut, midSlope, 1.0, whiteOut, whiteSlope);
    }
    if (x >= peak)
    {
        return peakOut;
    }
    float dx = peak - 1.0;
    float secant = (peakOut - whiteOut) / max(dx, 1e-6);
    float m0 = min(whiteSlope, 1.5 * secant);
    float m1 = 0.15 * secant;
    return PalaceHermite(x, 1.0, whiteOut, m0, peak, peakOut, m1);
}

// Post-exposure maxRGB → display-referred Working. Mirrors ToneMapper.MapScalar.
float PalaceMapScalar(
    float x,
    float lc,
    float p50,
    float peakNormalized,
    float ld,
    bool useHdrPath,
    float exposureGain)
{
    float lcEff = max(lc * max(exposureGain, 1e-6), 1.01);
    if (useHdrPath)
    {
        if (lcEff <= ld + 1e-6)
        {
            return min(x, lcEff);
        }
        return PalaceBt2390MapLinear(x, lcEff, ld);
    }
    return PalaceSdrMap(x, lcEff, p50, max(peakNormalized, 1e-6));
}

// Nonzero EV: do not sample a baked EV=0 LUT — call PalaceMapScalar (P5).
float PalaceLutOrMap(
    float postExposureMaxRgb,
    float lutSampleEv0,
    float lc,
    float p50,
    float peakNormalized,
    float ld,
    bool useHdrPath,
    float exposureEv,
    float exposureGain)
{
    if (abs(exposureEv) < 1e-6)
    {
        return lutSampleEv0;
    }
    return PalaceMapScalar(
        postExposureMaxRgb, lc, p50, peakNormalized, ld, useHdrPath, exposureGain);
}

#endif
