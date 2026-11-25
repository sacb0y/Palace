using Palace.Models;

namespace Palace.Helpers;

/// <summary>
/// Single registry for <see cref="AssetKind"/> metadata (extensions, icon, label,
/// badge, aspect, index toggle, details modules, default viewer surface).
/// Helpers stay thin lookups so a new kind is one registry entry, not ~10 switches.
/// </summary>
public sealed class AssetKindInfo
{
    public required AssetKind Kind { get; init; }

    public required IReadOnlyList<string> Extensions { get; init; }

    /// <summary>Fluent icon name (XAML / <see cref="GalleryMedia.PlaceholderIcon"/>).</summary>
    public required string Icon { get; init; }

    public required string Label { get; init; }

    /// <summary>Mosaic kind-badge key (<c>video</c> / <c>gif</c> / <c>model3d</c>); null = none.</summary>
    public string? BadgeId { get; init; }

    public double DefaultAspect { get; init; } = 1.0;

    /// <summary>Whether the current Library format toggle indexes this kind.</summary>
    public required Func<bool> IndexEnabled { get; init; }

    public required GallerySurface DefaultSurface { get; init; }

    /// <summary>Thumb pipeline id until providers land (<c>still</c> / <c>video</c> / <c>mesh</c> / <c>none</c>).</summary>
    public string ThumbProvider { get; init; } = "none";
}

/// <summary>Lookups over every <see cref="AssetKind"/> — missing entries fail the registry test.</summary>
public static class AssetKindRegistry
{
    private static readonly Dictionary<AssetKind, AssetKindInfo> ByKind;
    private static readonly Dictionary<string, AssetKind> ByExt;

    static AssetKindRegistry()
    {
        var all = new AssetKindInfo[]
        {
            new()
            {
                Kind = AssetKind.Image,
                Extensions = PathSafe.ImageExt.OrderBy(e => e, StringComparer.OrdinalIgnoreCase).ToArray(),
                Icon = "Image",
                Label = "Image",
                BadgeId = null,
                DefaultAspect = 1.0,
                IndexEnabled = static () => LibrarySettings.IndexImages,
                DefaultSurface = GallerySurface.Still,
                ThumbProvider = "still"
            },
            new()
            {
                Kind = AssetKind.Gif,
                Extensions = PathSafe.GifExt.OrderBy(e => e, StringComparer.OrdinalIgnoreCase).ToArray(),
                Icon = "Gif",
                Label = "GIF",
                BadgeId = "gif",
                DefaultAspect = 1.0,
                IndexEnabled = static () => LibrarySettings.IndexImages,
                DefaultSurface = GallerySurface.Still,
                ThumbProvider = "still"
            },
            new()
            {
                Kind = AssetKind.Video,
                Extensions = PathSafe.VideoExt.OrderBy(e => e, StringComparer.OrdinalIgnoreCase).ToArray(),
                Icon = "Video",
                Label = "Video",
                BadgeId = "video",
                DefaultAspect = 16.0 / 9.0,
                IndexEnabled = static () => LibrarySettings.IndexVideo,
                DefaultSurface = GallerySurface.Video,
                ThumbProvider = "video"
            },
            new()
            {
                Kind = AssetKind.Model3D,
                Extensions = PathSafe.Model3DExt.OrderBy(e => e, StringComparer.OrdinalIgnoreCase).ToArray(),
                Icon = "Cube",
                Label = "3D",
                BadgeId = "model3d",
                DefaultAspect = 1.0,
                IndexEnabled = static () => LibrarySettings.IndexModel3D,
                DefaultSurface = GallerySurface.Mesh,
                ThumbProvider = "mesh"
            },
            new()
            {
                Kind = AssetKind.Other,
                Extensions = Array.Empty<string>(),
                Icon = "Document",
                Label = "",
                BadgeId = null,
                DefaultAspect = 1.0,
                IndexEnabled = static () => false,
                DefaultSurface = GallerySurface.None,
                ThumbProvider = "none"
            },
        };

        ByKind = all.ToDictionary(i => i.Kind);
        ByExt = new Dictionary<string, AssetKind>(StringComparer.OrdinalIgnoreCase);
        foreach (var info in all)
        {
            foreach (var ext in info.Extensions)
            {
                ByExt[ext] = info.Kind;
            }
        }
    }

    public static IReadOnlyCollection<AssetKind> AllKinds => ByKind.Keys;

    public static AssetKindInfo Get(AssetKind kind) =>
        ByKind.TryGetValue(kind, out var info)
            ? info
            : ByKind[AssetKind.Other];

    public static AssetKind FromExtension(string? pathOrExt)
    {
        var ext = PathSafe.Extension(pathOrExt);
        return ByExt.TryGetValue(ext, out var kind) ? kind : AssetKind.Other;
    }

    public static bool IsCatalogExt(string? pathOrExt)
    {
        var ext = PathSafe.Extension(pathOrExt);
        return ByExt.ContainsKey(ext);
    }

    public static string Icon(AssetKind kind) => Get(kind).Icon;

    public static string Label(AssetKind kind) => Get(kind).Label;

    public static string? BadgeId(AssetKind kind) => Get(kind).BadgeId;

    public static double DefaultAspect(AssetKind kind) => Get(kind).DefaultAspect;

    public static GallerySurface DefaultSurface(AssetKind kind) => Get(kind).DefaultSurface;

    public static bool ShowKindBadge(bool isFolderHeader, bool isOrphan, AssetKind kind) =>
        GalleryMedia.ShowKindBadge(isFolderHeader, isOrphan, BadgeId(kind) is not null);

    /// <summary>
    /// Viewer surface for an open gallery item: Mesh for 3D (poster in the dock only),
    /// Video when playable, Still when a still path exists, else None.
    /// </summary>
    public static GallerySurface ResolveSurface(
        AssetKind kind,
        bool playableVideo,
        bool hasStillOrPreview)
    {
        if (kind == AssetKind.Model3D)
        {
            return GallerySurface.Mesh;
        }

        if (playableVideo)
        {
            return GallerySurface.Video;
        }

        if (hasStillOrPreview)
        {
            return GallerySurface.Still;
        }

        return GallerySurface.None;
    }

    /// <summary>Side preview: Mesh shows the poster still, never a live render.</summary>
    public static GallerySurface ResolvePreviewSurface(
        AssetKind kind,
        bool multi,
        bool cloudish,
        bool hasPreviewPath)
    {
        if (multi || !hasPreviewPath)
        {
            return GallerySurface.None;
        }

        if (kind == AssetKind.Model3D)
        {
            return GallerySurface.Still; // poster JPEG only
        }

        if (!cloudish && kind == AssetKind.Video)
        {
            return GallerySurface.Video;
        }

        if (cloudish || kind is AssetKind.Image or AssetKind.Gif)
        {
            return GallerySurface.Still;
        }

        return GallerySurface.None;
    }
}
