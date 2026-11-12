using System.Runtime.InteropServices;

namespace Palace.Helpers;

/// <summary>
/// Dev-only gate for the Diligent D3D12 / SwapChainPanel 3D viewer spike.
/// Off by default. Enable with env <c>PALACE_MESH3D_SPIKE=1</c> (or
/// <see cref="EnabledOverride"/> in tests). Never a product Settings toggle.
/// </summary>
public static class Mesh3dSpike
{
    public const string EnvVar = "PALACE_MESH3D_SPIKE";

    /// <summary>Packaged sample used when the spike window opens with no path.</summary>
    public const string DefaultSampleRelativePath = "Assets/Mesh3dSpike/SpikeBox.gltf";

    /// <summary>Test override: null = read env, true/false force.</summary>
    public static bool? EnabledOverride { get; set; }

    public static bool IsEnabled
    {
        get
        {
            if (EnabledOverride is { } forced)
            {
                return forced;
            }

            return ParseEnabled(Environment.GetEnvironmentVariable(EnvVar));
        }
    }

    /// <summary>True for 1 / true / yes (case-insensitive).</summary>
    public static bool ParseEnabled(string? value) =>
        value is "1" or "true" or "TRUE" or "yes" or "YES";

    /// <summary>
    /// About card / open button stay hidden unless the flag is on.
    /// Product UI must not show this surface otherwise.
    /// </summary>
    public static bool ShowsDevSurface => IsEnabled;

    /// <summary>x64 only for this spike (Streamline / Diligent D3D12 host).</summary>
    public static bool IsSupportedArchitecture =>
        RuntimeInformation.ProcessArchitecture == Architecture.X64;

    public static string ArchitectureCaption =>
        IsSupportedArchitecture
            ? "x64"
            : $"unsupported ({RuntimeInformation.ProcessArchitecture})";
}
