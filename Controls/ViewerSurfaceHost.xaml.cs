using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Palace.Helpers;
using Palace.Models;

namespace Palace.Controls;

/// <summary>
/// Owns the still surface, MediaPlayerElement, and Mesh placeholder (future
/// SwapChainPanel). Hosts bind <see cref="Surface"/> from <c>GalleryViewModel</c>
/// and call through <see cref="Still"/> / <see cref="Player"/> for media updates.
/// </summary>
public sealed partial class ViewerSurfaceHost : UserControl
{
    public static readonly DependencyProperty SurfaceProperty = DependencyProperty.Register(
        nameof(Surface),
        typeof(GallerySurface),
        typeof(ViewerSurfaceHost),
        new PropertyMetadata(GallerySurface.None));

    public static readonly DependencyProperty MediaAutomationIdProperty = DependencyProperty.Register(
        nameof(MediaAutomationId),
        typeof(string),
        typeof(ViewerSurfaceHost),
        new PropertyMetadata("MpeViewerSurface"));

    public static readonly DependencyProperty MeshAutomationIdProperty = DependencyProperty.Register(
        nameof(MeshAutomationId),
        typeof(string),
        typeof(ViewerSurfaceHost),
        new PropertyMetadata("BrdMeshSurface"));

    public ViewerSurfaceHost()
    {
        InitializeComponent();
    }

    public GallerySurface Surface
    {
        get => (GallerySurface)GetValue(SurfaceProperty);
        set => SetValue(SurfaceProperty, value);
    }

    public string MediaAutomationId
    {
        get => (string)GetValue(MediaAutomationIdProperty);
        set => SetValue(MediaAutomationIdProperty, value);
    }

    public string MeshAutomationId
    {
        get => (string)GetValue(MeshAutomationIdProperty);
        set => SetValue(MeshAutomationIdProperty, value);
    }

    public GalleryStillSurface Still => SrfStill;

    public MediaPlayerElement Player => MpeVideo;

    public Border MeshHost => BrdMesh;
}
