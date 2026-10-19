namespace Palace.Models;

/// <summary>Built-in shell background presets (Settings → Background).</summary>
public enum ShellPresetKind
{
    Default = 0,
    Mica = 1,
    MicaAlt = 2,
    Neutral = 3,
    Ribbons = 4,
    Capsules = 5,
    Bokeh = 6,
    Nebula = 7,
    Aero = 8,
    Petal = 9,
    Facets = 10,
    Aurora = 11,
    Custom = 12,
    Viewport = 13,
    Blueprint = 14,
    Sketchbook = 15,
    Pixel = 16,
    /// <summary>Manifest id <c>nodes</c>. Display name is Node Graph.</summary>
    Nodes = 17
}

/// <summary>Settings picker group. Chrome presets (Default, Mica, Neutral, Custom) are <see cref="None"/>.</summary>
public enum ShellPresetGroup
{
    None = 0,
    Vivid = 1,
    Creator = 2
}

/// <summary>Gradient stop hex triples for painted presets (off WinUI).</summary>
public static class ShellPresetGradients
{
    public const string NeutralDark = "#FF202020";
    public const string NeutralLight = "#FFF3F3F3";

    public static (string Start, string Mid, string End)? TryStops(ShellPresetKind preset, bool light) =>
        preset switch
        {
            ShellPresetKind.Default => light
                ? (Helpers.ShellBackground.LightGradientStartHex,
                    Helpers.ShellBackground.LightGradientMidHex,
                    Helpers.ShellBackground.LightGradientEndHex)
                : (Helpers.ShellBackground.DarkGradientStartHex,
                    Helpers.ShellBackground.DarkGradientMidHex,
                    Helpers.ShellBackground.DarkGradientEndHex),
            ShellPresetKind.Neutral => light
                ? (NeutralLight, NeutralLight, NeutralLight)
                : (NeutralDark, NeutralDark, NeutralDark),
            _ => null
        };
}
