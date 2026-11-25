namespace Palace.Mesh3d;

/// <summary>Test double that records calls — no GPU, safe on Linux CI.</summary>
public sealed class RecordingMeshRenderer : IMeshRenderer
{
    public List<string> Log { get; } = [];

    public SceneDesc? LastScene { get; private set; }

    public (int Width, int Height, float Scale)? LastSize { get; private set; }

    public ClipRef? LastClip { get; private set; }

    public Dictionary<string, bool> Visibility { get; } = new(StringComparer.Ordinal);

    public Task LoadAsync(SceneDesc scene, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        LastScene = scene;
        Log.Add($"Load:{scene.RootPath}");
        return Task.CompletedTask;
    }

    public void SetVisibility(string meshId, bool visible)
    {
        Visibility[meshId] = visible;
        Log.Add($"Vis:{meshId}={(visible ? 1 : 0)}");
    }

    public void Play(ClipRef clip)
    {
        LastClip = clip;
        Log.Add($"Play:{clip.Id}");
    }

    public void Resize(int width, int height, float scale)
    {
        LastSize = (width, height, scale);
        Log.Add($"Resize:{width}x{height}@{scale}");
    }

    public ValueTask DisposeAsync()
    {
        Log.Add("Dispose");
        return ValueTask.CompletedTask;
    }
}
