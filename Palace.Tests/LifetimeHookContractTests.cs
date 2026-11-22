using System.Text.RegularExpressions;
using Xunit;

namespace Palace.Tests;

/// <summary>
/// Source contracts for Loaded/Unloaded re-hook hygiene (Doktor D4 / A1).
/// </summary>
public sealed class LifetimeHookContractTests
{
    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Palace.csproj")))
        {
            dir = dir.Parent;
        }

        return dir?.FullName
            ?? throw new DirectoryNotFoundException("Palace.csproj not found above the test output");
    }

    [Fact]
    public void TagsPage_SubscribesPropertyChangedOnLoaded_UnsubscribesUnloaded()
    {
        var cs = File.ReadAllText(Path.Combine(RepoRoot(), "Pages", "TagsPage.xaml.cs"));
        Assert.Contains("ViewModel.PropertyChanged += ViewModel_PropertyChanged", cs, StringComparison.Ordinal);
        Assert.Contains("ViewModel.PropertyChanged -= ViewModel_PropertyChanged", cs, StringComparison.Ordinal);
        Assert.Contains("TagsPage_Loaded", cs, StringComparison.Ordinal);
        Assert.Contains("TagsPage_Unloaded", cs, StringComparison.Ordinal);
        Assert.Contains("ReleaseOverlayMediaPlayer", cs, StringComparison.Ordinal);
        Assert.Contains("_videoBrowseSwipe?.Unhook()", cs, StringComparison.Ordinal);
        Assert.Contains("ReviewSwipeCommitted -= OnOverlayStillSwipe", cs, StringComparison.Ordinal);
        Assert.Contains("SyncMouseCanDragArm", cs, StringComparison.Ordinal);
        Assert.Contains("PrefetchGhostsForSelection", cs, StringComparison.Ordinal);
        // Constructor must not permanently += a lambda onto the singleton VM.
        var ctor = cs.IndexOf("public TagsPage()", StringComparison.Ordinal);
        var loaded = cs.IndexOf("TagsPage_Loaded", ctor, StringComparison.Ordinal);
        Assert.True(ctor >= 0 && loaded > ctor);
        var ctorBody = cs[ctor..loaded];
        Assert.DoesNotContain("ViewModel.PropertyChanged +=", ctorBody, StringComparison.Ordinal);
    }

    [Fact]
    public void GalleryStillSurface_RehooksWheelAndDpiOnLoaded()
    {
        var cs = File.ReadAllText(Path.Combine(RepoRoot(), "Controls", "GalleryStillSurface.xaml.cs"));
        Assert.Contains("HookSurfaceInput", cs, StringComparison.Ordinal);
        Assert.Contains("UnhookSurfaceInput", cs, StringComparison.Ordinal);
        Assert.Contains("PointerWheelChangedEvent", cs, StringComparison.Ordinal);
        Assert.Contains("CompositionScaleChanged", cs, StringComparison.Ordinal);
        // Loaded must call HookSurfaceInput (not only constructor).
        Assert.True(
            Regex.IsMatch(
                cs,
                @"Loaded\s*\+=[\s\S]*HookSurfaceInput",
                RegexOptions.CultureInvariant),
            "Loaded must re-hook surface input after Unloaded");
    }

    [Fact]
    public void ViewerDetailsCard_ResubscribesGalleryOnLoaded()
    {
        var cs = File.ReadAllText(Path.Combine(RepoRoot(), "Controls", "ViewerDetailsCard.xaml.cs"));
        Assert.Contains("UnsubscribeGallery", cs, StringComparison.Ordinal);
        Assert.Contains("SubscribeGallery", cs, StringComparison.Ordinal);
        var loaded = cs.IndexOf("private void OnLoaded", StringComparison.Ordinal);
        Assert.True(loaded >= 0);
        var unloaded = cs.IndexOf("private void OnUnloaded", loaded, StringComparison.Ordinal);
        Assert.True(unloaded > loaded);
        var body = cs[loaded..unloaded];
        Assert.Contains("SubscribeGallery(Gallery)", body, StringComparison.Ordinal);
    }

    [Fact]
    public void JustifiedMosaicLayout_MeasureCachesByWidthAndCount()
    {
        var cs = File.ReadAllText(Path.Combine(RepoRoot(), "Helpers", "JustifiedMosaicLayout.cs"));
        Assert.Contains("state.Rects.Count != context.ItemCount", cs, StringComparison.Ordinal);
        Assert.Contains("state.LaidOutWidth - width", cs, StringComparison.Ordinal);
        var measure = cs.IndexOf("protected override Size MeasureOverride", StringComparison.Ordinal);
        Assert.True(measure >= 0);
        var arrange = cs.IndexOf("protected override Size ArrangeOverride", measure, StringComparison.Ordinal);
        Assert.True(arrange > measure);
        var body = cs[measure..arrange];
        Assert.Contains("Recalculate(context, state, width)", body, StringComparison.Ordinal);
        // Must be gated — not unconditional Recalculate like the pre-0.3.113 path.
        Assert.Contains("if (state.Rects.Count != context.ItemCount", body, StringComparison.Ordinal);
    }
}
