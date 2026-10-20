using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Palace.Helpers;

namespace Palace.Controls;

/// <summary>
/// Picks a DataTemplate for a <see cref="DetailsModule"/> by
/// <see cref="DetailsModuleKind"/>. Moodboard can reuse the same selector
/// with its own templates keyed the same way.
/// </summary>
public sealed class DetailsModuleTemplateSelector : DataTemplateSelector
{
    public DataTemplate? FileTemplate { get; set; }
    public DataTemplate? ColorHdrTemplate { get; set; }
    public DataTemplate? HistogramTemplate { get; set; }
    public DataTemplate? GamutTemplate { get; set; }
    public DataTemplate? TagsTemplate { get; set; }
    public DataTemplate? VideoTemplate { get; set; }
    public DataTemplate? AudioTemplate { get; set; }
    public DataTemplate? Mesh3DTemplate { get; set; }
    public DataTemplate? GenerationTemplate { get; set; }

    /// <summary>Legacy alias — Color &amp; HDR replaces the old Image module template.</summary>
    public DataTemplate? ImageTemplate
    {
        get => ColorHdrTemplate;
        set => ColorHdrTemplate = value;
    }

    protected override DataTemplate? SelectTemplateCore(object item) =>
        Select(item);

    protected override DataTemplate? SelectTemplateCore(object item, DependencyObject container) =>
        Select(item);

    private DataTemplate? Select(object item)
    {
        var kind = item switch
        {
            DetailsModule module => module.Kind,
            DetailsModuleKind moduleKind => moduleKind,
            _ => (DetailsModuleKind?)null
        };

        return kind switch
        {
            DetailsModuleKind.File => FileTemplate,
            DetailsModuleKind.ColorHdr => ColorHdrTemplate,
            DetailsModuleKind.Histogram => HistogramTemplate,
            DetailsModuleKind.Gamut => GamutTemplate,
            DetailsModuleKind.Tags => TagsTemplate,
            DetailsModuleKind.Video => VideoTemplate,
            DetailsModuleKind.Audio => AudioTemplate,
            DetailsModuleKind.Mesh3D => Mesh3DTemplate,
            DetailsModuleKind.Generation => GenerationTemplate,
            _ => null
        };
    }
}
