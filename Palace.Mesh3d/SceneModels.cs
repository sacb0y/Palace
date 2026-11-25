namespace Palace.Mesh3d;

/// <summary>One outfit / body part slot on a character (composer).</summary>
public sealed class Slot
{
    public required string Id { get; init; }
    public required string Name { get; init; }
    public string? BoundMeshId { get; set; }
}

/// <summary>Per-mesh visibility toggle in the composer.</summary>
public sealed class MeshToggle
{
    public required string MeshId { get; init; }
    public required string Label { get; init; }
    public bool Visible { get; set; } = true;
}

/// <summary>Animation clip reference (name + optional duration).</summary>
public sealed class ClipRef
{
    public required string Id { get; init; }
    public required string Name { get; init; }
    public double DurationSeconds { get; init; }
}

/// <summary>Character = body + outfit slots for the composer.</summary>
public sealed class Character
{
    public required string Id { get; init; }
    public required string Name { get; init; }
    public List<Slot> Slots { get; } = [];
    public List<MeshToggle> Meshes { get; } = [];
    public List<ClipRef> Clips { get; } = [];
}

/// <summary>What <see cref="IMeshRenderer.LoadAsync"/> receives.</summary>
public sealed class SceneDesc
{
    public required string RootPath { get; init; }
    public Character? Character { get; init; }
    public IReadOnlyList<string> DependencyPaths { get; init; } = [];
}
