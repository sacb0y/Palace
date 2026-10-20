using System.Runtime.InteropServices.WindowsRuntime;
using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Microsoft.UI.Xaml.Shapes;
using Palace.Helpers;

namespace Palace.Controls;

/// <summary>
/// SKIV-style gamut view: CIE 1931 xy plot (spectral locus, Rec.709 / P3 /
/// Rec.2020 triangles, D65, pixel density) plus coverage percentages, both
/// from the shared <see cref="HdrAnalysis"/> pass.
/// </summary>
public sealed partial class GalleryGamutChart : UserControl
{
    public static readonly DependencyProperty AnalysisProperty = DependencyProperty.Register(
        nameof(Analysis),
        typeof(HdrAnalysisResult),
        typeof(GalleryGamutChart),
        new PropertyMetadata(null, OnAnalysisChanged));

    public static readonly DependencyProperty CompactProperty = DependencyProperty.Register(
        nameof(Compact),
        typeof(bool),
        typeof(GalleryGamutChart),
        new PropertyMetadata(false, OnAnalysisChanged));

    public static readonly DependencyProperty IdPrefixProperty = DependencyProperty.Register(
        nameof(IdPrefix),
        typeof(string),
        typeof(GalleryGamutChart),
        new PropertyMetadata("Gallery", OnIdPrefixChanged));

    private int _renderEpoch;

    public GalleryGamutChart()
    {
        InitializeComponent();
        Loaded += (_, _) =>
        {
            ApplyIds();
            Redraw();
        };
    }

    public HdrAnalysisResult? Analysis
    {
        get => (HdrAnalysisResult?)GetValue(AnalysisProperty);
        set => SetValue(AnalysisProperty, value);
    }

    public bool Compact
    {
        get => (bool)GetValue(CompactProperty);
        set => SetValue(CompactProperty, value);
    }

    public string IdPrefix
    {
        get => (string)GetValue(IdPrefixProperty);
        set => SetValue(IdPrefixProperty, value);
    }

    private static void OnAnalysisChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is GalleryGamutChart chart)
        {
            chart.Redraw();
        }
    }

    private static void OnIdPrefixChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is GalleryGamutChart chart)
        {
            chart.ApplyIds();
        }
    }

    private void ApplyIds()
    {
        var prefix = string.IsNullOrWhiteSpace(IdPrefix) ? "Gallery" : IdPrefix.Trim();
        AutomationProperties.SetAutomationId(PnlGamut, $"Pnl{prefix}Gamut");
        AutomationProperties.SetName(PnlGamut, "Gamut");
        AutomationProperties.SetAutomationId(ImgCie, $"Img{prefix}Cie");
        AutomationProperties.SetName(ImgCie, "CIE 1931 chromaticity plot");
        AutomationProperties.SetAutomationId(TxtGamut, $"Txt{prefix}Gamut");
        AutomationProperties.SetName(TxtGamut, "Gamut coverage");
    }

    private void Redraw()
    {
        if (ImgCie is null)
        {
            return;
        }

        var analysis = Analysis;
        var compact = Compact;
        var size = compact ? 120 : 160;
        ImgCie.Width = size;
        ImgCie.Height = size;
        // Compact Details expander already titles "Gamut · CIE 1931 xy".
        TxtGamutTitle.Visibility = compact ? Visibility.Collapsed : Visibility.Visible;
        var gamut = analysis?.HasSamples == true ? analysis.Gamut : GamutCoverage.Empty;
        TxtGamut.Text = analysis?.HasSamples == true ? gamut.Summary() : "";
        BuildLegend(gamut, analysis?.HasSamples == true);

        var epoch = ++_renderEpoch;
        var density = analysis?.HasSamples == true ? analysis.CieDensity : [];
        _ = RenderAsync(density, epoch);
    }

    private async Task RenderAsync(int[] density, int epoch)
    {
        byte[] bgra;
        try
        {
            bgra = await Task.Run(() => CieDiagram.RenderBgra(density));
        }
        catch
        {
            return;
        }

        if (epoch != _renderEpoch)
        {
            return;
        }

        var bitmap = new WriteableBitmap(CieDiagram.GridSize, CieDiagram.GridSize);
        using (var stream = bitmap.PixelBuffer.AsStream())
        {
            await stream.WriteAsync(bgra);
        }

        bitmap.Invalidate();
        if (epoch == _renderEpoch)
        {
            ImgCie.Source = bitmap;
        }
    }

    private void BuildLegend(GamutCoverage gamut, bool has)
    {
        PnlGamutLegend.Children.Clear();
        foreach (var kind in Enum.GetValues<GamutKind>())
        {
            var (r, g, b) = CieDiagram.Hue(kind);
            var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
            row.Children.Add(new Rectangle
            {
                Width = 10,
                Height = 10,
                RadiusX = 2,
                RadiusY = 2,
                VerticalAlignment = VerticalAlignment.Center,
                Fill = new SolidColorBrush(ColorHelper.FromArgb(255, r, g, b))
            });
            var text = new TextBlock
            {
                Text = has ? $"{CieGamut.Label(kind)}  {gamut[kind]:0.0}%" : CieGamut.Label(kind)
            };
            if (Application.Current.Resources.TryGetValue("CaptionTextBlockStyle", out var style) && style is Style s)
            {
                text.Style = s;
            }

            AutomationProperties.SetAutomationId(text, $"Txt{(string.IsNullOrWhiteSpace(IdPrefix) ? "Gallery" : IdPrefix.Trim())}Gamut{kind}");
            row.Children.Add(text);
            PnlGamutLegend.Children.Add(row);
        }
    }
}
