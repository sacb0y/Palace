using Palace.Models;

namespace Palace.Helpers;

/// <summary>
/// Kind of a reusable viewer / moodboard Details section. Off WinUI so Rooms
/// can reuse the same module ids without referencing the card control.
/// </summary>
public enum DetailsModuleKind
{
    File,
    Tags,
    Image,
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
/// File + Tags always; Image (and GIF) get image analysis; Video gets video;
/// Audio / 3D reserved for later kinds.
/// </summary>
public static class DetailsModuleCatalog
{
    public static readonly DetailsModule File = new("file", "File", DetailsModuleKind.File);
    public static readonly DetailsModule Tags = new("tags", "Tags", DetailsModuleKind.Tags);
    public static readonly DetailsModule Image = new("image", "Image", DetailsModuleKind.Image);
    public static readonly DetailsModule Video = new("video", "Video", DetailsModuleKind.Video);
    public static readonly DetailsModule Audio = new("audio", "Audio", DetailsModuleKind.Audio);
    public static readonly DetailsModule Mesh3D = new("mesh3d", "3D", DetailsModuleKind.Mesh3D);
    public static readonly DetailsModule Generation = new(
        "generation",
        "Generation",
        DetailsModuleKind.Generation,
        defaultExpanded: false);

    /// <summary>Modules shared by every asset kind.</summary>
    public static IReadOnlyList<DetailsModule> Common { get; } = [File, Tags];

    /// <summary>Modules that apply only when the asset is a still (including GIF).</summary>
    public static IReadOnlyList<DetailsModule> ImageModules { get; } = [Image];

    /// <summary>Modules that apply only when the asset is video.</summary>
    public static IReadOnlyList<DetailsModule> VideoModules { get; } = [Video];

    /// <summary>Reserved for a future Audio asset kind.</summary>
    public static IReadOnlyList<DetailsModule> AudioModules { get; } = [Audio];

    /// <summary>Reserved for a future 3D asset kind.</summary>
    public static IReadOnlyList<DetailsModule> Mesh3DModules { get; } = [Mesh3D];

    public static bool Includes(DetailsModuleKind module, AssetKind kind) => module switch
    {
        DetailsModuleKind.File or DetailsModuleKind.Tags or DetailsModuleKind.Generation => true,
        DetailsModuleKind.Image => kind is AssetKind.Image or AssetKind.Gif,
        DetailsModuleKind.Video => kind == AssetKind.Video,
        DetailsModuleKind.Audio or DetailsModuleKind.Mesh3D => false,
        _ => false
    };

    public static IReadOnlyList<DetailsModule> ForKind(AssetKind kind)
    {
        var list = new List<DetailsModule>(Common);
        if (Includes(DetailsModuleKind.Image, kind))
        {
            list.AddRange(ImageModules);
        }

        if (Includes(DetailsModuleKind.Video, kind))
        {
            list.AddRange(VideoModules);
        }

        // Generation is always available; the card hides it when empty.
        list.Add(Generation);
        return list;
    }

    public static DetailsModule? Find(string id) =>
        ForKind(AssetKind.Image).Concat(VideoModules).Concat(AudioModules).Concat(Mesh3DModules)
            .FirstOrDefault(module => string.Equals(module.Id, id, StringComparison.OrdinalIgnoreCase));
}

/// <summary>
/// Floating Details card layout: right dock vs bottom sheet, Next-plate inset.
/// Off WinUI for tests.
/// </summary>
public static class ViewerDetailsLayout
{
    public const double PanelWidth = 340;
    public const double PanelMinWidth = 300;
    public const double Inset = 16;
    public const double TopInset = 64;
    public const double NarrowBreakpoint = 720;
    public const double BottomSheetMaxHeightFraction = 0.5;
    public const double EnterOffsetX = 24;
    public const int EnterMs = 200;
    public const int ExitMs = 150;
    public const int NextSlideMs = 200;

    public static bool UsesBottomSheet(double windowWidth) =>
        windowWidth > 0 && windowWidth < NarrowBreakpoint;

    public static double EffectivePanelWidth(double availableWidth, bool bottomSheet)
    {
        if (bottomSheet)
        {
            return Math.Max(0, availableWidth - (Inset * 2));
        }

        var width = PanelWidth;
        if (availableWidth > 0)
        {
            width = Math.Min(width, Math.Max(PanelMinWidth, availableWidth - (Inset * 2)));
        }

        return Math.Max(PanelMinWidth, width);
    }

    public static double BottomSheetMaxHeight(double windowHeight) =>
        Math.Max(0, windowHeight * BottomSheetMaxHeightFraction);

    /// <summary>
    /// Right margin for the Next plate so it sits 16 epx from the card edge when
    /// the right-docked panel is open. Bottom-sheet mode leaves the default inset.
    /// </summary>
    public static ThicknessLike NextMargin(bool detailsOpen, bool bottomSheet, double panelWidth)
    {
        if (!detailsOpen || bottomSheet)
        {
            return new ThicknessLike(0, 0, Inset, 0);
        }

        var right = Inset + panelWidth + Inset;
        return new ThicknessLike(0, 0, right, 0);
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
