using Palace.Models;

namespace Palace.Helpers;

/// <summary>
/// Kind of a reusable viewer / moodboard Details section. Off WinUI so Rooms
/// can reuse the same module ids without referencing the card control.
/// </summary>
public enum DetailsModuleKind
{
    File,
    ColorHdr,
    Histogram,
    Gamut,
    Tags,
    Video,
    Audio,
    Mesh3D,
    Generation
}

/// <summary>
/// Contract for one Details section. Moodboard / Rooms can host the same ids
/// with their own chrome; selection stays in <see cref="DetailsModuleCatalog"/>.
/// </summary>
public sealed class DetailsModule
{
    public DetailsModule(
        string id,
        string title,
        DetailsModuleKind kind,
        bool defaultExpanded = true)
    {
        Id = id;
        Title = title;
        Kind = kind;
        DefaultExpanded = defaultExpanded;
    }

    public string Id { get; }

    public string Title { get; }

    public DetailsModuleKind Kind { get; }

    public bool DefaultExpanded { get; }

    public string CollapseSettingsKey => GalleryChrome.DetailsSectionKey(Id);
}

/// <summary>
/// Picks which Details modules belong on a given <see cref="AssetKind"/>.
/// File + Tags always; stills get Color/HDR + Histogram + Gamut; Video gets Media;
/// Audio / 3D reserved for later kinds.
/// </summary>
public static class DetailsModuleCatalog
{
    public static readonly DetailsModule File = new("file", "File", DetailsModuleKind.File);
    public static readonly DetailsModule ColorHdr = new("colorhdr", "Color & HDR", DetailsModuleKind.ColorHdr);
    public static readonly DetailsModule Histogram = new("histogram", "Histogram", DetailsModuleKind.Histogram);
    public static readonly DetailsModule Gamut = new("gamut", "Gamut · CIE 1931 xy", DetailsModuleKind.Gamut);
    public static readonly DetailsModule Tags = new("tags", "Tags", DetailsModuleKind.Tags);
    public static readonly DetailsModule Video = new("video", "Media", DetailsModuleKind.Video);
    public static readonly DetailsModule Audio = new("audio", "Audio", DetailsModuleKind.Audio);
    public static readonly DetailsModule Mesh3D = new("mesh3d", "3D", DetailsModuleKind.Mesh3D);
    public static readonly DetailsModule Generation = new(
        "generation",
        "Generation",
        DetailsModuleKind.Generation,
        defaultExpanded: false);

    /// <summary>Modules shared by every asset kind.</summary>
    public static IReadOnlyList<DetailsModule> Common { get; } = [File, Tags];

    /// <summary>Analysis modules for stills (including GIF).</summary>
    public static IReadOnlyList<DetailsModule> StillModules { get; } =
        [ColorHdr, Histogram, Gamut];

    /// <summary>Modules that apply only when the asset is video.</summary>
    public static IReadOnlyList<DetailsModule> VideoModules { get; } = [Video];

    /// <summary>Reserved for a future Audio asset kind.</summary>
    public static IReadOnlyList<DetailsModule> AudioModules { get; } = [Audio];

    /// <summary>Reserved for a future 3D asset kind.</summary>
    public static IReadOnlyList<DetailsModule> Mesh3DModules { get; } = [Mesh3D];

    public static bool Includes(DetailsModuleKind module, AssetKind kind) => module switch
    {
        DetailsModuleKind.File or DetailsModuleKind.Tags or DetailsModuleKind.Generation => true,
        DetailsModuleKind.ColorHdr or DetailsModuleKind.Histogram or DetailsModuleKind.Gamut =>
            kind is AssetKind.Image or AssetKind.Gif,
        DetailsModuleKind.Video => kind == AssetKind.Video,
        DetailsModuleKind.Audio or DetailsModuleKind.Mesh3D => false,
        _ => false
    };

    public static IReadOnlyList<DetailsModule> ForKind(AssetKind kind)
    {
        var list = new List<DetailsModule> { File };
        if (Includes(DetailsModuleKind.ColorHdr, kind))
        {
            list.AddRange(StillModules);
        }

        if (Includes(DetailsModuleKind.Video, kind))
        {
            list.AddRange(VideoModules);
        }

        list.Add(Tags);
        // Generation is always available; the card hides it when empty.
        list.Add(Generation);
        return list;
    }

    public static DetailsModule? Find(string id) =>
        ForKind(AssetKind.Image).Concat(VideoModules).Concat(AudioModules).Concat(Mesh3DModules)
            .FirstOrDefault(module => string.Equals(module.Id, id, StringComparison.OrdinalIgnoreCase));
}

/// <summary>
/// Floating Details card layout: left dock under the toolbar (Info flyout seat).
/// Off WinUI for tests.
/// </summary>
public static class ViewerDetailsLayout
{
    public const double PanelWidth = 270;
    public const double PanelMinWidth = 240;
    public const double Inset = 16;
    /// <summary>Top inset clears the overlay toolbar chrome (~48 + 16).</summary>
    public const double TopInset = 56;
    public const double MaxHeightFraction = 0.70;
    public const double NarrowBreakpoint = 720;
    public const double EnterOffsetY = -12;
    public const int EnterMs = 180;
    public const int ExitMs = 140;
    public const int NextSlideMs = 200;

    public static bool UsesBottomSheet(double windowWidth) => false;

    public static double EffectivePanelWidth(double availableWidth, bool bottomSheet = false)
    {
        _ = bottomSheet;
        var width = PanelWidth;
        if (availableWidth > 0)
        {
            width = Math.Min(width, Math.Max(PanelMinWidth, availableWidth - (Inset * 2)));
        }

        return Math.Max(PanelMinWidth, width);
    }

    public static double PanelMaxHeight(double windowHeight) =>
        Math.Max(0, windowHeight * MaxHeightFraction);

    /// <summary>Legacy name — left-docked card never steals Next inset.</summary>
    public static double BottomSheetMaxHeight(double windowHeight) =>
        PanelMaxHeight(windowHeight);

    /// <summary>
    /// Next-plate margin. The Info card docks under the left toolbar, so Next
    /// always keeps the default right inset.
    /// </summary>
    public static ThicknessLike NextMargin(bool detailsOpen, bool bottomSheet, double panelWidth)
    {
        _ = detailsOpen;
        _ = bottomSheet;
        _ = panelWidth;
        return new ThicknessLike(0, 0, Inset, 0);
    }

    public static string LandmarkName(string? fileName)
    {
        var name = string.IsNullOrWhiteSpace(fileName) ? "file" : fileName.Trim();
        return $"Details for {name}";
    }
}

/// <summary>DIP thickness without WinUI so layout math stays testable.</summary>
public readonly record struct ThicknessLike(double Left, double Top, double Right, double Bottom);

/// <summary>F6 focus cycle zones for the image viewer.</summary>
public enum ViewerFocusZone
{
    Image = 0,
    Controls = 1,
    Details = 2
}

public static class ViewerFocusCycle
{
    public static ViewerFocusZone Next(ViewerFocusZone current, bool detailsOpen)
    {
        return current switch
        {
            ViewerFocusZone.Image => ViewerFocusZone.Controls,
            ViewerFocusZone.Controls => detailsOpen ? ViewerFocusZone.Details : ViewerFocusZone.Image,
            _ => ViewerFocusZone.Image
        };
    }

    public static bool AllowsArrowNavigate(ViewerFocusZone zone) =>
        zone == ViewerFocusZone.Image;
}
