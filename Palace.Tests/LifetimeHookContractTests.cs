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
    public void TagsPage_Unloaded_ClearsMosaicDelegatesSeparately_NotReferenceEquals()
    {
        var cs = File.ReadAllText(Path.Combine(RepoRoot(), "Pages", "TagsPage.xaml.cs"))
            .Replace("\r\n", "\n");
        Assert.DoesNotContain("ReferenceEquals(ViewModel.Mosaic", cs, StringComparison.Ordinal);
        Assert.Contains("OwnedDelegate.ClearIfOurs(ref mosaicReset, OnMosaicReset)", cs, StringComparison.Ordinal);
        Assert.Contains(
            "OwnedDelegate.ClearIfOurs(ref mosaicChunk, OnMosaicChunkAppended)",
            cs,
            StringComparison.Ordinal);
        Assert.Contains("if (!IsLoaded)", cs, StringComparison.Ordinal);
        Assert.Contains("_priorityArmed = true", cs, StringComparison.Ordinal);
        // IsLoaded gate must sit between RefreshAsync await and _priorityArmed.
        var refresh = cs.IndexOf("ViewModel.RefreshAsync", StringComparison.Ordinal);
        var loadedGate = cs.IndexOf("if (!IsLoaded)", refresh, StringComparison.Ordinal);
        var armed = cs.IndexOf("_priorityArmed = true", loadedGate, StringComparison.Ordinal);
        Assert.True(refresh >= 0 && loadedGate > refresh && armed > loadedGate);
    }

    [Fact]
    public void LibraryPage_Unloaded_ClearsMosaicDelegatesSeparately_IsLoadedAfterAwait()
    {
        var cs = File.ReadAllText(Path.Combine(RepoRoot(), "Pages", "LibraryPage.xaml.cs"))
            .Replace("\r\n", "\n");
        Assert.DoesNotContain("ReferenceEquals(ViewModel.Mosaic", cs, StringComparison.Ordinal);
        Assert.Contains("OwnedDelegate.ClearIfOurs(ref mosaicReset, OnMosaicReset)", cs, StringComparison.Ordinal);
        Assert.Contains(
            "OwnedDelegate.ClearIfOurs(ref mosaicChunk, OnMosaicChunkAppended)",
            cs,
            StringComparison.Ordinal);
        // IsLoaded gate must sit between ReloadTagCatalogAsync await and AddHandler wiring.
        var catalog = cs.IndexOf("ViewModel.ReloadTagCatalogAsync", StringComparison.Ordinal);
        var loadedGate = cs.IndexOf("if (!IsLoaded)", catalog, StringComparison.Ordinal);
        var wire = cs.IndexOf("GrdAssets.AddHandler", loadedGate, StringComparison.Ordinal);
        Assert.True(catalog >= 0 && loadedGate > catalog && wire > loadedGate);
    }

    /// <summary>
    /// Fresh cast + ReferenceEquals always misses — fail if any page/code-behind
    /// clears a VM callback that way (Tags/Library MosaicReset bug class).
    /// </summary>
    [Fact]
    public void CodeBehind_NoReferenceEqualsAgainstDelegateCast()
    {
        var root = RepoRoot();
        var dirs = new[]
        {
            Path.Combine(root, "Pages"),
            Path.Combine(root, "Controls"),
        };
        var files = new List<string>();
        foreach (var dir in dirs)
        {
            if (!Directory.Exists(dir))
            {
                continue;
            }

            files.AddRange(Directory.EnumerateFiles(dir, "*.cs", SearchOption.AllDirectories));
        }

        foreach (var name in new[] { "MainWindow.xaml.cs", "GalleryWindow.xaml.cs", "App.xaml.cs" })
        {
            var path = Path.Combine(root, name);
            if (File.Exists(path))
            {
                files.Add(path);
            }
        }

        // ReferenceEquals(…(Action)…) / ReferenceEquals(…(Func…)…) — either arg.
        // One nesting level covers casts like (Action)OnFoo / (Func<int>)OnBar.
        var rx = new Regex(
            @"ReferenceEquals\s*\((?:[^()]|\([^()]*\))*\(\s*(?:Action|Func)\b",
            RegexOptions.CultureInvariant | RegexOptions.Singleline);

        var hits = new List<string>();
        foreach (var file in files)
        {
            var text = File.ReadAllText(file);
            foreach (Match m in rx.Matches(text))
            {
                var rel = Path.GetRelativePath(root, file).Replace('\\', '/');
                var snippet = m.Value.Replace('\r', ' ').Replace('\n', ' ').Trim();
                if (snippet.Length > 120)
                {
                    snippet = snippet[..117] + "...";
                }

                hits.Add($"{rel}: {snippet}");
            }
        }

        Assert.True(
            hits.Count == 0,
            "ReferenceEquals + delegate cast never matches the assigned slot; use OwnedDelegate.ClearIfOurs.\n"
            + string.Join("\n", hits));
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
    public void JustifiedMosaicLayout_MeasureCachesWithDirty()
    {
        var cs = File.ReadAllText(Path.Combine(RepoRoot(), "Helpers", "JustifiedMosaicLayout.cs"));
        Assert.Contains("MosaicLayoutCache.NeedsRecalculate", cs, StringComparison.Ordinal);
        Assert.Contains("_layoutDirty", cs, StringComparison.Ordinal);
        var measure = cs.IndexOf("protected override Size MeasureOverride", StringComparison.Ordinal);
        Assert.True(measure >= 0);
        var arrange = cs.IndexOf("protected override Size ArrangeOverride", measure, StringComparison.Ordinal);
        Assert.True(arrange > measure);
        var body = cs[measure..arrange];
        Assert.Contains("Recalculate(context, state, width)", body, StringComparison.Ordinal);
        // Must be gated via MosaicLayoutCache — not unconditional Recalculate.
        Assert.Contains("MosaicLayoutCache.NeedsRecalculate", body, StringComparison.Ordinal);
        Assert.DoesNotContain(
            "Recalculate(context, state, width);\n        var realization",
            body.Replace("\r\n", "\n"),
            StringComparison.Ordinal);
    }
}
