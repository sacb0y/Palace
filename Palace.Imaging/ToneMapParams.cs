namespace Palace.Imaging;

/// <summary>Pure tonemap inputs. ExposureEV defaults to 0 (Unity-style UI later).</summary>
public readonly record struct ToneMapParams(
    ImageStats Stats,
    DisplayTarget Display,
    float ExposureEv = 0f)
{
    /// <summary><c>2^EV</c> linear gain applied before the tone curve.</summary>
    public float ExposureGain => MathF.Pow(2f, ExposureEv);

    public bool UseHdrPath => Display.IsHdrOutput;
}
