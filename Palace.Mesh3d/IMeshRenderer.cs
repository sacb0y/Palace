namespace Palace.Mesh3d;

/// <summary>
/// Seam between WinUI <c>MeshSurface</c> and a native Diligent renderer (or a
/// <see cref="RecordingMeshRenderer"/> fake for Linux tests).
/// </summary>
public interface IMeshRenderer : IAsyncDisposable
{
    Task LoadAsync(SceneDesc scene, CancellationToken cancellationToken = default);

    void SetVisibility(string meshId, bool visible);

    void Play(ClipRef clip);

    void Resize(int width, int height, float scale);
}
