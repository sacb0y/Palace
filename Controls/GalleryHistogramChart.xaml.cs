using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;
using Palace.Helpers;
using Windows.Foundation;

namespace Palace.Controls;

/// <summary>
/// Lightroom-style histogram: grey luminance area under translucent R/G/B,
/// an SDR section then an HDR section in stops (dashed whole-stop lines,
/// accent tint), a gradient bar (heatmap palette when the heatmap is on),
/// then format · index · size and camera EXIF / AI settings. No clipping
/// warning. Geometry comes from <see cref="HistogramScale"/>.
/// </summary>
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
        new PropertyMetadata(GalleryHistogram.DefaultShowRgb, OnChartChanged));

    public static readonly DependencyProperty ShowLumaProperty = DependencyProperty.Register(
        nameof(ShowLuma),
        typeof(bool),
        typeof(GalleryHistogramChart),
        new PropertyMetadata(GalleryHistogram.DefaultShowLuma, OnChartChanged));

    public static readonly DependencyProperty HeatmapProperty = DependencyProperty.Register(
        nameof(Heatmap),
        typeof(bool),
        typeof(GalleryHistogramChart),
        new PropertyMetadata(false, OnChartChanged));

    public static readonly DependencyProperty DetailsProperty = DependencyProperty.Register(
        nameof(Details),
        typeof(GalleryDetailsRows),
        typeof(GalleryHistogramChart),
        new PropertyMetadata(null, OnChartChanged));

    public static readonly DependencyProperty IdPrefixProperty = DependencyProperty.Register(
        nameof(IdPrefix),
        typeof(string),
        typeof(GalleryHistogramChart),
        new PropertyMetadata("Gallery", OnIdPrefixChanged));

    private readonly ToolTip _hoverTip = new();

    public GalleryHistogramChart()
    {
        InitializeComponent();
        ToolTipService.SetToolTip(CnvHistogram, _hoverTip);
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

    public bool ShowLuma
    {
        get => (bool)GetValue(ShowLumaProperty);
        set => SetValue(ShowLumaProperty, value);
    }

    /// <summary>Heatmap view is on: the bar shows the heatmap palette (same function as the image).</summary>
    public bool Heatmap
    {
        get => (bool)GetValue(HeatmapProperty);
        set => SetValue(HeatmapProperty, value);
    }

    public GalleryDetailsRows? Details
    {
        get => (GalleryDetailsRows?)GetValue(DetailsProperty);
        set => SetValue(DetailsProperty, value);
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
        var (luma, rgb) = GalleryHistogram.ToggleLayer(ShowLuma, ShowRgb, clickedLuma: true);
        ShowLuma = luma;
        ShowRgb = rgb;
        // ToggleButton flips itself first; re-sync even when no DP changed.
        Redraw();
    }

    private void BtnHistogramRgb_Click(object sender, RoutedEventArgs e)
    {
        var (luma, rgb) = GalleryHistogram.ToggleLayer(ShowLuma, ShowRgb, clickedLuma: false);
        ShowLuma = luma;
        ShowRgb = rgb;
        Redraw();
    }

    private void CnvHistogram_SizeChanged(object sender, SizeChangedEventArgs e) => Redraw();

    private void CnvHistogram_PointerMoved(object sender, PointerRoutedEventArgs e)
    {
        var bins = Bins;
        var width = CnvHistogram.ActualWidth;
        if (bins is null || !bins.HasSamples || width < 2)
        {
            return;
        }

        var x = Math.Clamp(e.GetCurrentPoint(CnvHistogram).Position.X, 0, width);
        _hoverTip.Content = HistogramScale.HoverLabel(x / width, bins.StopCount, bins.SdrWhiteNits);
        LnHover.X1 = x;
        LnHover.X2 = x;
        LnHover.Y1 = 0;
        LnHover.Y2 = CnvHistogram.Height;
        LnHover.Visibility = Visibility.Visible;
    }

    private void CnvHistogram_PointerExited(object sender, PointerRoutedEventArgs e) =>
        LnHover.Visibility = Visibility.Collapsed;

    private void ApplyIds()
    {
        var prefix = string.IsNullOrWhiteSpace(IdPrefix) ? "Gallery" : IdPrefix.Trim();
        AutomationProperties.SetAutomationId(PnlHistogram, $"Pnl{prefix}Histogram");
        AutomationProperties.SetName(PnlHistogram, "Histogram");
        AutomationProperties.SetAutomationId(BtnHistogramLuma, $"Btn{prefix}HistogramLuma");
        AutomationProperties.SetName(BtnHistogramLuma, "Luma");
        AutomationProperties.SetAutomationId(BtnHistogramRgb, $"Btn{prefix}HistogramRgb");
        AutomationProperties.SetName(BtnHistogramRgb, "RGB");
        AutomationProperties.SetAutomationId(TxtHistogram, $"Txt{prefix}Histogram");
        AutomationProperties.SetName(TxtHistogram, "Histogram");
        AutomationProperties.SetAutomationId(CnvHistogram, $"Cnv{prefix}Histogram");
        AutomationProperties.SetName(CnvHistogram, "Histogram plot");
        AutomationProperties.SetAutomationId(TxtHistogramFormat, $"Txt{prefix}HistogramFormat");
        AutomationProperties.SetName(TxtHistogramFormat, "Format");
        AutomationProperties.SetAutomationId(TxtHistogramIndex, $"Txt{prefix}HistogramIndex");
        AutomationProperties.SetName(TxtHistogramIndex, "Position in this view");
        AutomationProperties.SetAutomationId(TxtHistogramSize, $"Txt{prefix}HistogramSize");
        AutomationProperties.SetName(TxtHistogramSize, "Pixel size and file size");
        AutomationProperties.SetAutomationId(TxtHistogramExif, $"Txt{prefix}HistogramExif");
        AutomationProperties.SetName(TxtHistogramExif, "Camera or generation settings");
        AutomationProperties.SetAutomationId(TxtHistogramHdr, $"Txt{prefix}HistogramHdr");
        AutomationProperties.SetName(TxtHistogramHdr, "HDR section");
    }

    private void Redraw()
    {
        if (CnvHistogram is null || TxtHistogram is null)
        {
            return;
        }

        var compact = Compact;
        PnlHistogramModes.Visibility = compact ? Visibility.Collapsed : Visibility.Visible;
        CnvHistogram.Height = compact ? 64 : 120;
        var luma = ShowLuma;
        var rgb = ShowRgb;
        BtnHistogramLuma.IsChecked = luma;
        BtnHistogramRgb.IsChecked = rgb;

        var bins = Bins;
        TxtHistogram.Text = GalleryHistogram.Caption(bins);
        ApplyDetails(Details);

        var width = CnvHistogram.ActualWidth;
        var height = CnvHistogram.Height;
        var stops = bins?.StopCount ?? 0;
        ApplyBar(width, stops, bins?.SdrWhiteNits ?? HistogramScale.SdrWhiteNits, bins?.HasSamples == true);
        CnvStopLines.Children.Clear();
        if (width < 2 || height < 2 || bins is null || !bins.HasSamples)
        {
            ClearShapes();
            RctHdrZone.Visibility = Visibility.Collapsed;
            return;
        }

        var hdrX = HistogramScale.SdrFraction(stops) * width;
        RctHdrZone.Visibility = stops > 0 ? Visibility.Visible : Visibility.Collapsed;
        Canvas.SetLeft(RctHdrZone, hdrX);
        RctHdrZone.Width = Math.Max(0, width - hdrX);
        RctHdrZone.Height = height;

        SetArea(PlgLuma, null, luma ? bins.Luma : null, stops, width, height);
        SetArea(PlgRed, PlnRed, rgb ? bins.Red : null, stops, width, height);
        SetArea(PlgGreen, PlnGreen, rgb ? bins.Green : null, stops, width, height);
        SetArea(PlgBlue, PlnBlue, rgb ? bins.Blue : null, stops, width, height);

        // SDR white + each whole HDR stop: dashed verticals.
        for (var s = 0; s <= stops && stops > 0; s++)
        {
            var x = HistogramScale.StopX01(s, stops) * width;
            CnvStopLines.Children.Add(new Line
            {
                X1 = x,
                X2 = x,
                Y1 = 0,
                Y2 = height,
                Stroke = PlgLuma.Fill,
                StrokeThickness = 1,
                StrokeDashArray = new DoubleCollection { 3, 3 },
                Opacity = s == 0 ? 0.8 : 0.55
            });
        }
    }

    private void ApplyDetails(GalleryDetailsRows? rows)
    {
        var show = rows is not null && rows.HasAny;
        GrdHistogramRows.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
        TxtHistogramFormat.Text = rows?.Format ?? "";
        TxtHistogramIndex.Text = rows?.Index ?? "";
        TxtHistogramSize.Text = rows?.Size ?? "";
        TxtHistogramExif.Text = rows?.Settings ?? "";
        TxtHistogramExif.Visibility = string.IsNullOrEmpty(rows?.Settings) ? Visibility.Collapsed : Visibility.Visible;
        ToolTipService.SetToolTip(TxtHistogramExif, string.IsNullOrEmpty(rows?.SettingsTooltip) ? null : rows.SettingsTooltip);
    }

    private void ApplyBar(double width, int stops, float sdrWhite, bool hasSamples)
    {
        GrdHistogramBar.Visibility = hasSamples ? Visibility.Visible : Visibility.Collapsed;
        GrdHistogramBarLabels.Visibility = hasSamples ? Visibility.Visible : Visibility.Collapsed;
        if (!hasSamples || width < 2)
        {
            return;
        }

        var sdrW = HistogramScale.SdrFraction(stops) * width;
        var heatmap = Heatmap;
        RctHistogramHeatmap.Visibility = heatmap ? Visibility.Visible : Visibility.Collapsed;
        RctHistogramSdr.Visibility = heatmap ? Visibility.Collapsed : Visibility.Visible;
        RctHistogramHdr.Visibility = !heatmap && stops > 0 ? Visibility.Visible : Visibility.Collapsed;
        RctHistogramSdr.Width = Math.Max(0, sdrW - (stops > 0 ? 1 : 0));
        RctHistogramHdr.Width = Math.Max(0, width - sdrW - 1);
        if (heatmap)
        {
            // Same palette function as the swapchain heatmap, sampled along the axis.
            var brush = new LinearGradientBrush { StartPoint = new Point(0, 0.5), EndPoint = new Point(1, 0.5) };
            const int steps = 48;
            for (var i = 0; i <= steps; i++)
            {
                var x01 = i / (double)steps;
                var (r, g, b) = HdrHeatmap.Srgb8At(HistogramScale.NitsAtX01(x01, stops, sdrWhite));
                brush.GradientStops.Add(new GradientStop { Offset = x01, Color = ColorHelper.FromArgb(255, r, g, b) });
            }

            RctHistogramHeatmap.Fill = brush;
        }

        TxtHistogramHdr.Visibility = stops > 0 ? Visibility.Visible : Visibility.Collapsed;
        TxtHistogramHdr.Text = stops > 0 ? $"HDR +{stops} stops" : "HDR";
        TxtHistogramHdr.Margin = new Thickness(sdrW + 2, 0, 0, 0);
    }

    private void ClearShapes()
    {
        PlgLuma.Points = [];
        PlgRed.Points = [];
        PlgGreen.Points = [];
        PlgBlue.Points = [];
        PlnRed.Points = [];
        PlnGreen.Points = [];
        PlnBlue.Points = [];
    }

    private static void SetArea(Polygon area, Polyline? line, int[]? counts, int stops, double width, double height)
    {
        if (counts is null || counts.Length == 0)
        {
            area.Points = [];
            if (line is not null)
            {
                line.Points = [];
            }

            return;
        }

        var pts = GalleryHistogram.AreaPoints(counts, stops, width, height);
        var poly = new PointCollection();
        foreach (var (x, y) in pts)
        {
            poly.Add(new Point(x, y));
        }

        area.Points = poly;
        if (line is null)
        {
            return;
        }

        var curve = new PointCollection();
        for (var i = 1; i < pts.Length - 1; i++)
        {
            curve.Add(new Point(pts[i].X, pts[i].Y));
        }

        line.Points = curve;
    }
}
