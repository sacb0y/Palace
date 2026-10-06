using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;
using Palace.Helpers;
using Windows.Foundation;

namespace Palace.Controls;

public sealed partial class GalleryHistogramChart : UserControl
{
    public static readonly DependencyProperty BinsProperty = DependencyProperty.Register(
        nameof(Bins),
        typeof(GalleryHistogramBins),
        typeof(GalleryHistogramChart),
        new PropertyMetadata(null, OnChartChanged));

    public static readonly DependencyProperty CompactProperty = DependencyProperty.Register(
        nameof(Compact),
        typeof(bool),
        typeof(GalleryHistogramChart),
        new PropertyMetadata(false, OnChartChanged));

    public static readonly DependencyProperty ShowRgbProperty = DependencyProperty.Register(
        nameof(ShowRgb),
        typeof(bool),
        typeof(GalleryHistogramChart),
        new PropertyMetadata(false, OnChartChanged));

    public static readonly DependencyProperty IdPrefixProperty = DependencyProperty.Register(
        nameof(IdPrefix),
        typeof(string),
        typeof(GalleryHistogramChart),
        new PropertyMetadata("Gallery", OnIdPrefixChanged));

    public GalleryHistogramChart()
    {
        InitializeComponent();
        Loaded += (_, _) =>
        {
            ApplyIds();
            Redraw();
        };
    }

    public GalleryHistogramBins? Bins
    {
        get => (GalleryHistogramBins?)GetValue(BinsProperty);
        set => SetValue(BinsProperty, value);
    }

    public bool Compact
    {
        get => (bool)GetValue(CompactProperty);
        set => SetValue(CompactProperty, value);
    }

    public bool ShowRgb
    {
        get => (bool)GetValue(ShowRgbProperty);
        set => SetValue(ShowRgbProperty, value);
    }

    public string IdPrefix
    {
        get => (string)GetValue(IdPrefixProperty);
        set => SetValue(IdPrefixProperty, value);
    }

    private static void OnChartChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is GalleryHistogramChart chart)
        {
            chart.Redraw();
        }
    }

    private static void OnIdPrefixChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is GalleryHistogramChart chart)
        {
            chart.ApplyIds();
        }
    }

    private void BtnHistogramLuma_Click(object sender, RoutedEventArgs e)
    {
        ShowRgb = false;
        // ToggleButton unchecks itself first. Re-clicking Y must restore
        // the check even when ShowRgb was already false (no DP change).
        Redraw();
    }

    private void BtnHistogramRgb_Click(object sender, RoutedEventArgs e)
    {
        ShowRgb = true;
        Redraw();
    }

    private void CnvHistogram_SizeChanged(object sender, SizeChangedEventArgs e) => Redraw();

    private void ApplyIds()
    {
        var prefix = string.IsNullOrWhiteSpace(IdPrefix) ? "Gallery" : IdPrefix.Trim();
        AutomationProperties.SetAutomationId(PnlHistogram, $"Pnl{prefix}Histogram");
        AutomationProperties.SetName(PnlHistogram, "Histogram");
        AutomationProperties.SetAutomationId(BtnHistogramLuma, $"Btn{prefix}HistogramLuma");
        AutomationProperties.SetName(BtnHistogramLuma, "CIE Y");
        AutomationProperties.SetAutomationId(BtnHistogramRgb, $"Btn{prefix}HistogramRgb");
        AutomationProperties.SetName(BtnHistogramRgb, "scRGB channels");
        AutomationProperties.SetAutomationId(TxtHistogram, $"Txt{prefix}Histogram");
        AutomationProperties.SetName(TxtHistogram, "Histogram");
        AutomationProperties.SetAutomationId(CnvHistogram, $"Cnv{prefix}Histogram");
        AutomationProperties.SetName(CnvHistogram, "Histogram plot");
    }

    private void Redraw()
    {
        if (CnvHistogram is null || TxtHistogram is null)
        {
            return;
        }

        var compact = Compact;
        PnlHistogramModes.Visibility = compact ? Visibility.Collapsed : Visibility.Visible;
        CnvHistogram.Height = compact ? 48 : 96;
        var rgb = !compact && ShowRgb;
        var (lumaOn, rgbOn) = GalleryHistogram.ExclusiveModeChecks(rgb);
        BtnHistogramLuma.IsChecked = lumaOn;
        BtnHistogramRgb.IsChecked = rgbOn;

        var bins = Bins;
        var samples = bins?.SampleCount ?? 0;
        var binCount = bins?.BinCount ?? 0;
        TxtHistogram.Text = GalleryHistogram.Caption(rgb, binCount, samples);

        PlnLuma.Visibility = rgb ? Visibility.Collapsed : Visibility.Visible;
        PlnRed.Visibility = rgb ? Visibility.Visible : Visibility.Collapsed;
        PlnGreen.Visibility = rgb ? Visibility.Visible : Visibility.Collapsed;
        PlnBlue.Visibility = rgb ? Visibility.Visible : Visibility.Collapsed;

        var width = CnvHistogram.ActualWidth;
        var height = CnvHistogram.Height;
        if (width < 2 || height < 2 || bins is null || !bins.HasSamples)
        {
            ClearLine(PlnLuma);
            ClearLine(PlnRed);
            ClearLine(PlnGreen);
            ClearLine(PlnBlue);
            return;
        }

        if (rgb)
        {
            ClearLine(PlnLuma);
            SetLine(PlnRed, bins.Red, width, height);
            SetLine(PlnGreen, bins.Green, width, height);
            SetLine(PlnBlue, bins.Blue, width, height);
            return;
        }

        ClearLine(PlnRed);
        ClearLine(PlnGreen);
        ClearLine(PlnBlue);
        SetLine(PlnLuma, bins.Luma, width, height);
    }

    private static void ClearLine(Polyline line) => line.Points = [];

    private static void SetLine(Polyline line, int[] counts, double width, double height)
    {
        var heights = new float[counts.Length];
        GalleryHistogram.FillHeights(counts, heights);
        var bars = new (double X, double Width, double Height)[counts.Length];
        GalleryHistogram.LayoutBars(counts.Length, width, height, heights, bars);
        var points = new PointCollection();
        for (var i = 0; i < bars.Length; i++)
        {
            var x = bars[i].X + (bars[i].Width * 0.5);
            var y = height - bars[i].Height;
            points.Add(new Point(x, y));
        }

        line.Points = points;
    }
}
