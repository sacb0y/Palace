namespace Palace.Mesh3d;

/// <summary>
/// Resolves sidecar files for a primary 3D asset (glTF .bin/textures, FBX .fbm/).
/// Full FileOp integration is parked (D5) — refuse multi-file Model3D moves until wired.
/// </summary>
public interface IAssetDependencies
{
    IReadOnlyList<string> Resolve(string primaryPath);
}

/// <summary>Empty resolver until glTF / FBX probes land (D1 + D5).</summary>
public sealed class EmptyAssetDependencies : IAssetDependencies
{
    public static EmptyAssetDependencies Instance { get; } = new();

    public IReadOnlyList<string> Resolve(string primaryPath) => [];
}
