namespace Palace.Helpers;

/// <summary>
/// Path helpers for the Diligent glTF spike. Off WinUI so Palace.Tests can cover
/// extension / existence policy without loading Diligent.
/// </summary>
public static class Mesh3dGltf
{
    public static readonly HashSet<string> Extensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".gltf",
        ".glb"
    };

    public static bool IsGltfPath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return false;
        }

        var ext = Path.GetExtension(path);
        return Extensions.Contains(ext);
    }

    /// <summary>
    /// Resolve a candidate path for the spike host. Prefers an existing absolute
    /// glTF/GLB; otherwise the packaged sample under the app base directory.
    /// </summary>
    public static string? ResolveLoadPath(string? preferred, string? appBaseDirectory)
    {
        if (IsGltfPath(preferred) && File.Exists(preferred))
        {
            return Path.GetFullPath(preferred);
        }

        if (string.IsNullOrWhiteSpace(appBaseDirectory))
        {
            return null;
        }

        var sample = Path.Combine(appBaseDirectory, Mesh3dSpike.DefaultSampleRelativePath.Replace('/', Path.DirectorySeparatorChar));
        return File.Exists(sample) ? Path.GetFullPath(sample) : null;
    }

    public static string DescribeLoad(string? path) =>
        path is null
            ? "no glTF (sample missing)"
            : Path.GetFileName(path);
}
