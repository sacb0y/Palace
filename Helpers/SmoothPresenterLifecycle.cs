namespace Palace.Helpers;

/// <summary>
/// WinUI-free lifecycle state for the Diligent SwapChainPanel presenter.
/// Used by unit tests and by <c>SmoothDiligentPresenter</c> to reason about
/// resize / DPI / device-lost without touching WinRT.
/// </summary>
public enum SmoothPresenterPhase
{
    Idle = 0,
    Created = 1,
    Attached = 2,
    Running = 3,
    DeviceLost = 4,
    Disposed = 5
}

public sealed class SmoothPresenterLifecycle
{
    public SmoothPresenterPhase Phase { get; private set; } = SmoothPresenterPhase.Idle;

    public uint Width { get; private set; }

    public uint Height { get; private set; }

    public float DpiScaleX { get; private set; } = 1f;

    public float DpiScaleY { get; private set; } = 1f;

    public int ResizeCount { get; private set; }

    public int DpiChangeCount { get; private set; }

    public int DeviceLostCount { get; private set; }

    public int RecreateCount { get; private set; }

    public bool CanAttach => Phase is SmoothPresenterPhase.Created or SmoothPresenterPhase.DeviceLost;

    public bool CanRender => Phase is SmoothPresenterPhase.Attached or SmoothPresenterPhase.Running;

    public bool NeedsRecreate => Phase == SmoothPresenterPhase.DeviceLost;

    public void MarkCreated()
    {
        EnsureNotDisposed();
        Phase = SmoothPresenterPhase.Created;
        RecreateCount++;
    }

    public void MarkAttached(uint width, uint height)
    {
        EnsureNotDisposed();
        if (Phase is not (SmoothPresenterPhase.Created or SmoothPresenterPhase.DeviceLost or SmoothPresenterPhase.Attached))
        {
            throw new InvalidOperationException($"Cannot attach from {Phase}");
        }

        Width = Math.Max(2, width);
        Height = Math.Max(2, height);
        Phase = SmoothPresenterPhase.Attached;
    }

    public void MarkRunning()
    {
        EnsureNotDisposed();
        if (!CanRender && Phase != SmoothPresenterPhase.Attached)
        {
            throw new InvalidOperationException($"Cannot run from {Phase}");
        }

        Phase = SmoothPresenterPhase.Running;
    }

    /// <summary>
    /// Returns true when a resize is needed. Does <b>not</b> commit <see cref="Width"/> /
    /// <see cref="Height"/> — call <see cref="CommitResize"/> only after native resize succeeds
    /// so a failed resize can be retried with the same target size.
    /// </summary>
    public bool TryResize(uint width, uint height)
    {
        EnsureNotDisposed();
        if (Phase is not (SmoothPresenterPhase.Attached or SmoothPresenterPhase.Running))
        {
            return false;
        }

        width = Math.Max(2, width);
        height = Math.Max(2, height);
        return width != Width || height != Height;
    }

    /// <summary>
    /// Commits panel size after a successful native <c>psv_resize</c>.
    /// </summary>
    public void CommitResize(uint width, uint height)
    {
        EnsureNotDisposed();
        if (Phase is not (SmoothPresenterPhase.Attached or SmoothPresenterPhase.Running))
        {
            throw new InvalidOperationException($"Cannot commit resize from {Phase}");
        }

        width = Math.Max(2, width);
        height = Math.Max(2, height);
        if (width == Width && height == Height)
        {
            return;
        }

        Width = width;
        Height = height;
        ResizeCount++;
    }

    public bool TrySetDpi(float scaleX, float scaleY)
    {
        EnsureNotDisposed();
        if (Phase is SmoothPresenterPhase.Idle or SmoothPresenterPhase.Disposed)
        {
            return false;
        }

        scaleX = scaleX > 0 ? scaleX : 1f;
        scaleY = scaleY > 0 ? scaleY : 1f;
        if (Math.Abs(scaleX - DpiScaleX) < 0.0001f && Math.Abs(scaleY - DpiScaleY) < 0.0001f)
        {
            return false;
        }

        DpiScaleX = scaleX;
        DpiScaleY = scaleY;
        DpiChangeCount++;
        return true;
    }

    public void MarkDeviceLost()
    {
        EnsureNotDisposed();
        Phase = SmoothPresenterPhase.DeviceLost;
        DeviceLostCount++;
    }

    /// <summary>
    /// After device lost: tear down to Created so attach can run again.
    /// </summary>
    public void BeginRecreate()
    {
        EnsureNotDisposed();
        if (Phase != SmoothPresenterPhase.DeviceLost)
        {
            throw new InvalidOperationException($"Cannot recreate from {Phase}");
        }

        Phase = SmoothPresenterPhase.Created;
        RecreateCount++;
    }

    public void MarkDisposed()
    {
        Phase = SmoothPresenterPhase.Disposed;
    }

    public static (uint W, uint H) PhysicalSize(double dipW, double dipH, float scaleX, float scaleY)
    {
        var sx = scaleX <= 0 ? 1f : scaleX;
        var sy = scaleY <= 0 ? 1f : scaleY;
        var w = Math.Max(2, (int)Math.Ceiling(dipW * sx));
        var h = Math.Max(2, (int)Math.Ceiling(dipH * sy));
        return ((uint)w, (uint)h);
    }

    private void EnsureNotDisposed()
    {
        if (Phase == SmoothPresenterPhase.Disposed)
        {
            throw new ObjectDisposedException(nameof(SmoothPresenterLifecycle));
        }
    }
}
