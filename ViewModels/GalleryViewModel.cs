using System.Collections.ObjectModel;
using System.Diagnostics;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Palace.Helpers;
using Palace.Models;
using Palace.Services;
using Palace.Services.Cloud;
using Windows.Storage;

namespace Palace.ViewModels;

public partial class GalleryViewModel : ObservableObject
{
    private readonly CatalogService _catalog;
    private readonly IReadOnlyList<AssetItem> _items;
    private int _loadEpoch;
    private bool _chromeReady;
    private int? _assetDurationMs;

    public GalleryViewModel(IReadOnlyList<AssetItem> items, int startIndex, CatalogService catalog)
    {
        _catalog = catalog;
        _items = items.Count == 0 ? [] : items.ToList();
        var last = Math.Max(0, _items.Count - 1);
        CurrentIndex = _items.Count == 0 ? 0 : Math.Clamp(startIndex, 0, last);
        LoadChromeSettings();
        _ = LoadCurrentAsync();
    }

    public ObservableCollection<AssignedTagItem> Tags { get; } = [];

    [ObservableProperty]
    public partial int CurrentIndex { get; set; }

    [ObservableProperty]
    public partial AssetItem? Current { get; set; }

    [ObservableProperty]
    public partial string? CurrentPath { get; set; }

    [ObservableProperty]
    public partial string Title { get; set; } = "";

    [ObservableProperty]
    public partial string PositionLabel { get; set; } = "";

    [ObservableProperty]
    public partial bool IsImage { get; set; }

    [ObservableProperty]
    public partial bool IsVideo { get; set; }

    [ObservableProperty]
    public partial bool IsMissing { get; set; }

    [ObservableProperty]
    public partial bool CanGoPrevious { get; set; }

    [ObservableProperty]
    public partial bool CanGoNext { get; set; }

    [ObservableProperty]
    public partial string? PreviewImageUri { get; set; }

    [ObservableProperty]
    public partial bool IsCloudPreview { get; set; }

    [ObservableProperty]
    public partial string PreviewStatus { get; set; } = "";

    [ObservableProperty]
    public partial bool CanDownloadOriginal { get; set; }

    [ObservableProperty]
    public partial bool CanOpenInExplorer { get; set; }

    /// <summary>
    /// Sticky Fit / Fill / 1:1 the user chose (toolbar + keep-zoom). HDR stills
    /// show Fit via <see cref="DisplayScaling"/> without overwriting this.
    /// </summary>
    [ObservableProperty]
    public partial ImageScaling Scaling { get; set; } = ImageScaling.Fit;

    private bool _hdrForcesDisplayFit;

    /// <summary>On-screen scale — Fit while the current still is HDR presentable.</summary>
    public ImageScaling DisplayScaling =>
        GalleryScale.DisplayScaling(Scaling, _hdrForcesDisplayFit);

    [ObservableProperty]
    public partial string DetailsText { get; set; } = "";

    [ObservableProperty]
    public partial string HdrStatus { get; set; } = "";

    /// <summary>
    /// Overlay / GalleryWindow corner chip. Catalog <c>IsHdr</c> or live
    /// probe — never Open the original from the VM. Fluent parse stays in XAML.
    /// </summary>
    public bool ShowHdrBadge =>
        GalleryMedia.ShowHdrBadge(
            isFolderHeader: false,
            Current?.IsOrphan == true,
            Current?.Path,
            Current?.IsHdr == true || CurrentProbe.IsHdr);

    /// <summary>
    /// Set-mosaic-thumb chrome: visible only for a playable local MediaPlayer
    /// video. Online-only / API stay hidden (no hydrate-for-poster).
    /// </summary>
    public bool ShowSetVideoThumb => VideoMosaicThumb.IsActionVisible(IsVideo);

    public bool CanSetVideoThumb =>
        VideoMosaicThumb.CanSet(IsVideo, Current?.ContentHash, CurrentPath);

    public string SetVideoThumbTooltip =>
        VideoMosaicThumb.DisabledReason(
            IsVideo,
            Current?.ContentHash,
            CurrentPath,
            isOnlineOnly: Current?.IsOnlineOnly == true
                && !GalleryMedia.CanShowPreview(CurrentPath ?? Current?.Path),
            apiOnly: Current is not null && AssetItemMapper.IsApiOnly(Current))
        ?? "Set Library mosaic thumbnail from this frame";

    [ObservableProperty]
    public partial bool CanScale { get; set; }

    [ObservableProperty]
    public partial bool IsScaleFit { get; set; } = true;

    [ObservableProperty]
    public partial bool IsScaleActual { get; set; }

    [ObservableProperty]
    public partial bool IsScaleFill { get; set; }

    [ObservableProperty]
    public partial HdrProbe CurrentProbe { get; set; } = HdrProbe.None;

    partial void OnCurrentChanged(AssetItem? value)
    {
        OnPropertyChanged(nameof(ShowHdrBadge));
        OnPropertyChanged(nameof(CurrentKind));
        NotifySetVideoThumbChrome();
    }

    partial void OnCurrentProbeChanged(HdrProbe value) => OnPropertyChanged(nameof(ShowHdrBadge));

    partial void OnIsVideoChanged(bool value) => NotifySetVideoThumbChrome();

    partial void OnCurrentPathChanged(string? value) => NotifySetVideoThumbChrome();

    private void NotifySetVideoThumbChrome()
    {
        OnPropertyChanged(nameof(ShowSetVideoThumb));
        OnPropertyChanged(nameof(CanSetVideoThumb));
        OnPropertyChanged(nameof(SetVideoThumbTooltip));
    }

    /// <summary>
    /// Stamp the mosaic cache path onto the live <see cref="AssetItem"/> so
    /// Library / Tags tiles refresh without a rescan. Caller supplies a fresh
    /// <see cref="Microsoft.UI.Xaml.Media.ImageSource"/> (IgnoreImageCache).
    /// </summary>
    public void ApplyMosaicThumb(string thumbPath, Microsoft.UI.Xaml.Media.ImageSource? thumbImage)
    {
        if (Current is null || string.IsNullOrEmpty(thumbPath))
        {
            return;
        }

        Current.ThumbPath = thumbPath;
        Current.ThumbImage = thumbImage;
        Current.ThumbLoadStarted = thumbImage is not null;
    }

    /// <summary>
    /// Bumped once after Current / probe / path / preview / IsImage are all
    /// written so the still surface starts one refresh, not one per field.
    /// </summary>
    [ObservableProperty]
    public partial int StillRevision { get; set; }

    public bool HdrPresented { get; private set; }

    public bool DisplayIsHdr { get; private set; }

    public float DisplayPeakNits { get; private set; }

    public float ContentMaxNits { get; private set; }

    public float ContentAvgNits { get; private set; }

    public float ContentMinNits { get; private set; }

    public float ContentMaxScrgb { get; private set; }

    /// <summary>Native-pixel channel of <see cref="ContentMaxScrgb"/> when the decoder knows it, else '\0'.</summary>
    private char _contentMaxScrgbChannel;

    private int _headerPixelWidth;
    private int _headerPixelHeight;
    private int _nativePixelWidth;
    private int _nativePixelHeight;
    private bool _nativeHdrStats;
    private string _displayProbe = "";

    [ObservableProperty]
    public partial string? PreviewModel { get; set; }

    [ObservableProperty]
    public partial string? PreviewPrompt { get; set; }

    [ObservableProperty]
    public partial string? PreviewNegative { get; set; }

    [ObservableProperty]
    public partial string? PreviewSeed { get; set; }

    [ObservableProperty]
    public partial bool ShowGeneration { get; set; }

    [ObservableProperty]
    public partial bool ShowDetails { get; set; } = GalleryChrome.DefaultShowDetails;

    /// <summary>Left-docked Info card never uses a bottom sheet; kept for Next-margin binds.</summary>
    [ObservableProperty]
    public partial bool DetailsUsesBottomSheet { get; set; }

    /// <summary>Live width of the floating Details card (left dock under toolbar).</summary>
    [ObservableProperty]
    public partial double DetailsPanelWidth { get; set; } = ViewerDetailsLayout.PanelWidth;

    [ObservableProperty]
    public partial bool LoopVideo { get; set; } = GalleryChrome.DefaultLoopVideo;

    /// <summary>Asset kind for DetailsModule selection (File/Tags always; Image/Video by kind).</summary>
    public AssetKind CurrentKind => Current?.Kind ?? AssetKind.Other;

    [ObservableProperty]
    public partial string ImageInfoText { get; set; } = "";

    // Structured Info card fields (empty => hide row).
    [ObservableProperty]
    public partial string InfoFileName { get; set; } = "";

    [ObservableProperty]
    public partial string InfoFileNameEllipsis { get; set; } = "";

    [ObservableProperty]
    public partial string InfoPath { get; set; } = "";

    [ObservableProperty]
    public partial string InfoPathEllipsis { get; set; } = "";

    [ObservableProperty]
    public partial string InfoFileMetaLeft { get; set; } = "";

    [ObservableProperty]
    public partial string InfoFileMetaRight { get; set; } = "";

    /// <summary>File section line 3: <c>size · resolution | format · frame</c>.</summary>
    [ObservableProperty]
    public partial string InfoFileMetaLine { get; set; } = "";

    [ObservableProperty]
    public partial string InfoColor { get; set; } = "";

    [ObservableProperty]
    public partial string InfoHdrStatus { get; set; } = "";

    /// <summary>Dense one-line HDR value (no wrap) for Color &amp; HDR grid.</summary>
    [ObservableProperty]
    public partial string InfoHdrLine { get; set; } = "";

    [ObservableProperty]
    public partial string InfoMaxCll { get; set; } = "";

    [ObservableProperty]
    public partial string InfoFall { get; set; } = "";

    [ObservableProperty]
    public partial string InfoMaxLum { get; set; } = "";

    [ObservableProperty]
    public partial string InfoAvgLum { get; set; } = "";

    [ObservableProperty]
    public partial string InfoMinLum { get; set; } = "";

    [ObservableProperty]
    public partial string InfoP99 { get; set; } = "";

    [ObservableProperty]
    public partial string InfoAboveSdr { get; set; } = "";

    [ObservableProperty]
    public partial string InfoDisplayLum { get; set; } = "";

    [ObservableProperty]
    public partial string InfoDisplayProbe { get; set; } = "";

    [ObservableProperty]
    public partial string InfoGamutSummary { get; set; } = "";

    [ObservableProperty]
    public partial bool InfoShowHdrStats { get; set; }

    [ObservableProperty]
    public partial bool InfoHasColorSection { get; set; }

    [ObservableProperty]
    public partial string InfoVideoDuration { get; set; } = "";

    [ObservableProperty]
    public partial string InfoVideoSize { get; set; } = "";

    [ObservableProperty]
    public partial GalleryHistogramBins Histogram { get; set; } = GalleryHistogramBins.Empty;

    [ObservableProperty]
    public partial bool ShowHistogram { get; set; }

    [ObservableProperty]
    public partial bool HistogramShowRgb { get; set; } = GalleryHistogram.DefaultShowRgb;

    [ObservableProperty]
    public partial bool HistogramShowLuma { get; set; } = GalleryHistogram.DefaultShowLuma;

    /// <summary>Shared analysis pass (histogram, light stats, gamut, CIE) for the presented still.</summary>
    [ObservableProperty]
    public partial HdrAnalysisResult Analysis { get; set; } = HdrAnalysisResult.Empty;

    [ObservableProperty]
    public partial bool ShowGamut { get; set; }

    /// <summary>Format · index · size, then camera EXIF or AI settings (under the histogram).</summary>
    [ObservableProperty]
    public partial GalleryDetailsRows DetailsRows { get; set; } = GalleryDetailsRows.Empty;

    /// <summary>Luminance heatmap view (Ctrl+H). Only applies to an HDR swapchain present.</summary>
    [ObservableProperty]
    public partial bool ShowHeatmap { get; set; }

    /// <summary>True while an HDR frame is presented, so the heatmap toggle can apply.</summary>
    [ObservableProperty]
    public partial bool CanShowHeatmap { get; set; }

    public HdrVizMode VizMode => ShowHeatmap && CanShowHeatmap ? HdrVizMode.Heatmap : HdrVizMode.None;

    /// <summary>Histogram bar shows the heatmap palette only while the heatmap is actually on.</summary>
    public bool HeatmapActive => VizMode == HdrVizMode.Heatmap;

    private CameraExif _cameraExif = CameraExif.None;
    private string? _genSampler;
    private string? _genSteps;

    [ObservableProperty]
    public partial string HistogramCaption { get; set; } = "";

    [ObservableProperty]
    public partial bool CanScrubGif { get; set; }

    [ObservableProperty]
    public partial bool GifPlaying { get; set; }

    [ObservableProperty]
    public partial int GifFrameCount { get; set; }

    [ObservableProperty]
    public partial double GifFrameIndex { get; set; }

    [ObservableProperty]
    public partial double GifFrameMaximum { get; set; }

    [ObservableProperty]
    public partial string GifFrameLabel { get; set; } = "";

    private bool _gifLoad;
    private bool _gifCompositeReady;
    private bool _gifPlayTick;
    private IReadOnlyList<int> _gifDelays = [];
    private CancellationTokenSource? _gifPlayCts;
    private int _gifPlayEpoch;

    partial void OnCurrentIndexChanged(int value) => _ = LoadCurrentAsync();

    partial void OnScalingChanged(ImageScaling value)
    {
        IsScaleFit = value == ImageScaling.Fit;
        IsScaleActual = value == ImageScaling.Actual;
        IsScaleFill = value == ImageScaling.Fill;
        OnPropertyChanged(nameof(DisplayScaling));
    }

    private void SetHdrForcesDisplayFit(bool hdrPresentable)
    {
        if (_hdrForcesDisplayFit == hdrPresentable)
        {
            return;
        }

        _hdrForcesDisplayFit = hdrPresentable;
        OnPropertyChanged(nameof(DisplayScaling));
    }

    [RelayCommand]
    private void SetScaleActual() => Scaling = ImageScaling.Actual;

    /// <summary>
    /// Fit toolbar / shortcut. Pinch keeps <see cref="Scaling"/> at Fit while
    /// zoomed — re-raise so the still surface always clears pinch zoom +
    /// translation (SDR and HDR).
    /// </summary>
    [RelayCommand]
    private void SetScaleFit()
    {
        if (Scaling != ImageScaling.Fit)
        {
            Scaling = ImageScaling.Fit;
            return;
        }

        OnPropertyChanged(nameof(Scaling));
    }

    [RelayCommand]
    private void SetScaleFill() => Scaling = ImageScaling.Fill;

    public bool TryHandleScaleShortcut(bool controlDown, int keyCode)
    {
        if (!controlDown || !CanScale)
        {
            return false;
        }

        var next = GalleryScale.FromKeyCode(keyCode);
        if (next is null)
        {
            return false;
        }

        if (next == ImageScaling.Fit)
        {
            SetScaleFit();
        }
        else
        {
            Scaling = next.Value;
        }

        return true;
    }

    public bool TryHandleViewerShortcut(bool controlDown, int keyCode) =>
        TryHandleViewerShortcut(controlDown, altDown: false, keyCode);

    public bool TryHandleViewerShortcut(bool controlDown, bool altDown, int keyCode)
    {
        if (TryHandleScaleShortcut(controlDown, keyCode))
        {
            return true;
        }

        // Ctrl+D aliases the Info pill; bare I / Alt+Enter also toggle Details.
        if (GalleryScale.TogglesImageInfo(controlDown, keyCode)
            || GalleryScale.TogglesDetails(controlDown, altDown, keyCode))
        {
            ShowDetails = !ShowDetails;
            return true;
        }

        if (GalleryScale.TogglesHeatmap(controlDown, keyCode))
        {
            if (CanShowHeatmap || ShowHeatmap)
            {
                ShowHeatmap = !ShowHeatmap;
            }

            return true;
        }

        return false;
    }

    /// <summary>
    /// Esc while Details is open hides the floating card and leaves the viewer open.
    /// </summary>
    public bool TryCloseDetailsFirst()
    {
        if (!ShowDetails)
        {
            return false;
        }

        ShowDetails = false;
        return true;
    }

    [RelayCommand]
    private void ToggleDetails() => ShowDetails = !ShowDetails;

    partial void OnShowHeatmapChanged(bool value)
    {
        OnPropertyChanged(nameof(VizMode));
        OnPropertyChanged(nameof(HeatmapActive));
    }

    partial void OnCanShowHeatmapChanged(bool value)
    {
        OnPropertyChanged(nameof(VizMode));
        OnPropertyChanged(nameof(HeatmapActive));
    }

    partial void OnShowDetailsChanged(bool value)
    {
        if (_chromeReady)
        {
            ApplicationData.Current.LocalSettings.Values[GalleryChrome.ShowDetailsKey] = value;
        }
    }

    partial void OnLoopVideoChanged(bool value)
    {
        if (_chromeReady)
        {
            ApplicationData.Current.LocalSettings.Values[GalleryChrome.LoopVideoKey] = value;
        }
    }

    private void LoadChromeSettings()
    {
        var values = ApplicationData.Current.LocalSettings.Values;
        ShowDetails = GalleryChrome.ParseShowDetails(values[GalleryChrome.ShowDetailsKey]);
        LoopVideo = GalleryChrome.ParseLoopVideo(values[GalleryChrome.LoopVideoKey]);
        Scaling = ViewerSettings.ParseZoom(values[ViewerSettings.DefaultZoomKey]);
        ViewerSettings.Apply(
            Scaling,
            ViewerSettings.ParseBool(values[ViewerSettings.TouchGesturesKey], ViewerSettings.DefaultTouchGestures),
            ViewerSettings.ParseBool(
                values[ViewerSettings.KeepZoomBetweenImagesKey],
                ViewerSettings.DefaultKeepZoomBetweenImages));
        _chromeReady = true;
    }

    partial void OnGifFrameIndexChanged(double value)
    {
        var clamped = GifFrames.ClampIndex((int)Math.Round(value), GifFrameCount);
        if (Math.Abs(clamped - value) > 0.01)
        {
            GifFrameIndex = clamped;
            return;
        }

        GifFrameLabel = GifFrames.PositionLabel(clamped, GifFrameCount);
        if (!_gifLoad
            && CanScrubGif
            && GifFrames.ShouldStopPlaybackOnIndexChange(_gifPlayTick))
        {
            GifPlaying = false;
        }
    }

    partial void OnGifPlayingChanged(bool value)
    {
        if (_gifLoad)
        {
            return;
        }

        if (value)
        {
            StartGifPlayLoop();
        }
        else
        {
            StopGifPlayback();
        }
    }

    [RelayCommand]
    private void ToggleGifPlay()
    {
        if (!CanScrubGif)
        {
            return;
        }

        GifPlaying = !GifPlaying;
    }

    [RelayCommand]
    private void GifPreviousFrame() => StepGifFrame(-1);

    [RelayCommand]
    private void GifNextFrame() => StepGifFrame(1);

    private void StepGifFrame(int delta)
    {
        if (!CanScrubGif)
        {
            return;
        }

        GifPlaying = false;
        GifFrameIndex = GifFrames.Step((int)Math.Round(GifFrameIndex), GifFrameCount, delta);
    }

    public void StopGifPlayback()
    {
        _gifPlayEpoch++;
        var cts = _gifPlayCts;
        _gifPlayCts = null;
        cts?.Cancel();
        cts?.Dispose();
    }

    /// <summary>
    /// Composite decode failed after scrub was offered. Hide chrome and let
    /// <c>BitmapImage</c> autoplay.
    /// </summary>
    public void AbandonGifScrub() => ApplyGifState(false, null);

    private void StartGifPlayLoop()
    {
        StopGifPlayback();
        if (!GifFrames.ShouldRunGifPlayLoop(CanScrubGif, GifPlaying, _gifCompositeReady))
        {
            return;
        }

        var cts = new CancellationTokenSource();
        _gifPlayCts = cts;
        var epoch = _gifPlayEpoch;
        var delays = _gifDelays;
        var frames = GifFrameCount;
        _ = RunGifPlayLoopAsync(epoch, frames, delays, cts.Token);
    }

    private async Task RunGifPlayLoopAsync(
        int epoch,
        int frames,
        IReadOnlyList<int> delays,
        CancellationToken cancellation)
    {
        try
        {
            while (!cancellation.IsCancellationRequested && epoch == _gifPlayEpoch)
            {
                var index = 0;
                var delayCs = 10;
                await UiDispatch.RunAsync(() =>
                {
                    index = GifFrames.ClampIndex((int)Math.Round(GifFrameIndex), frames);
                    delayCs = index < delays.Count ? delays[index] : 10;
                });
                await Task.Delay(GifFrames.DisplayDelayMs(delayCs), cancellation);
                if (epoch != _gifPlayEpoch || cancellation.IsCancellationRequested)
                {
                    return;
                }

                await UiDispatch.RunAsync(() =>
                {
                    if (epoch != _gifPlayEpoch || !GifPlaying)
                    {
                        return;
                    }

                    _gifPlayTick = true;
                    GifFrameIndex = GifFrames.NextPlayIndex(
                        (int)Math.Round(GifFrameIndex), GifFrameCount);
                    _gifPlayTick = false;
                });
            }
        }
        catch (Exception ex) when (ex is OperationCanceledException or ObjectDisposedException)
        {
        }
    }

    private void ApplyGifState(bool canScrub, GifFrames.Info? info)
    {
        StopGifPlayback();
        var frameCount = info?.FrameCount ?? 0;
        _gifLoad = true;
        _gifCompositeReady = false;
        _gifDelays = info?.DelaysCs ?? [];
        CanScrubGif = GifFrames.ShouldShowScrub(canScrub, frameCount);
        GifFrameCount = frameCount;
        GifFrameMaximum = Math.Max(0, frameCount - 1);
        GifFrameIndex = 0;
        GifPlaying = CanScrubGif;
        GifFrameLabel = GifFrames.PositionLabel(0, frameCount);
        _gifLoad = false;
    }

    /// <summary>
    /// Overlay composite cache is ready. Start the delay clock only now so
    /// Pause / slider during load keep frame 0.
    /// </summary>
    public void NotifyGifCompositeReady()
    {
        _gifCompositeReady = true;
        if (GifPlaying)
        {
            StartGifPlayLoop();
        }
    }

    public void SetHdrPresentResult(
        bool presented,
        bool displayHdr,
        float displayPeakNits = 0,
        float maxNits = 0,
        float avgNits = 0,
        float minNits = 0,
        float maxScrgb = 0,
        int pixelWidth = 0,
        int pixelHeight = 0,
        bool statsAreNative = false,
        string? displayProbe = null,
        char maxScrgbChannel = '\0')
    {
        HdrPresented = presented;
        DisplayIsHdr = displayHdr;
        DisplayPeakNits = displayPeakNits;
        if (displayProbe is not null)
        {
            _displayProbe = displayProbe;
        }
        if (GalleryPresent.ReplaceHdrStats(_nativeHdrStats, statsAreNative))
        {
            ContentMaxNits = maxNits;
            ContentAvgNits = avgNits;
            ContentMinNits = minNits;
            if (maxScrgb > 0 || statsAreNative)
            {
                ContentMaxScrgb = maxScrgb;
                _contentMaxScrgbChannel = maxScrgb > 0 ? maxScrgbChannel : '\0';
            }

            _nativeHdrStats = statsAreNative && (maxNits > 0 || maxScrgb > 0);
        }

        if (pixelWidth > 0 && pixelHeight > 0)
        {
            _nativePixelWidth = pixelWidth;
            _nativePixelHeight = pixelHeight;
        }

        CanShowHeatmap = presented;
        RefreshHdrStatus();
    }

    /// <summary>
    /// One <see cref="HdrAnalysis"/> result feeds the histogram, the Info
    /// figures (P99, FALL, MaxCLL channel, gamut) and the gamut plot.
    /// </summary>
    public void SetAnalysis(HdrAnalysisResult analysis)
    {
        Analysis = analysis.HasSamples ? analysis : HdrAnalysisResult.Empty;
        Histogram = Analysis.HasSamples ? Analysis.Histogram : GalleryHistogramBins.Empty;
        ShowHistogram = Histogram.HasSamples;
        ShowGamut = Analysis.HasSamples;
        HistogramCaption = GalleryHistogram.Caption(Histogram);
        RefreshImageInfo();
    }

    public void ClearHistogram() => SetAnalysis(HdrAnalysisResult.Empty);

    /// <summary>
    /// The gallery decoded the original still (e.g. a file the scan flagged undecodable):
    /// use its oriented pixel size for Info / Details / the bottom bar.
    /// </summary>
    public void SetDecodedPixelSize(int width, int height)
    {
        if (width <= 0 || height <= 0 || Current is not { } item)
        {
            return;
        }

        _nativePixelWidth = width;
        _nativePixelHeight = height;
        DetailsText = GalleryPresent.DetailsLine(item.FileName, width, height, item.Kind, item.FileSize);
        RefreshImageInfo();
    }

    public void SetHdrStats(float maxNits, float avgNits, float minNits, float maxScrgb, char maxScrgbChannel = '\0')
    {
        ContentMaxNits = maxNits;
        ContentAvgNits = avgNits;
        ContentMinNits = minNits;
        ContentMaxScrgb = maxScrgb;
        _contentMaxScrgbChannel = maxScrgb > 0 ? maxScrgbChannel : '\0';
        _nativeHdrStats = maxNits > 0 || maxScrgb > 0;
        RefreshImageInfo();
    }

    private void RefreshHdrStatus()
    {
        HdrStatus = GalleryPresent.StatusLine(
            CurrentProbe,
            HdrPresented,
            DisplayIsHdr,
            GalleryPeak.Enabled && DisplayIsHdr,
            DisplayIsHdr && GalleryPeak.Enabled ? GalleryPeak.Nits : DisplayPeakNits) ?? "";
        RefreshImageInfo();
    }

    private void RefreshImageInfo()
    {
        var item = Current;
        var pixels = GalleryPresent.FilePixelSize(
            _nativePixelWidth,
            _nativePixelHeight,
            _headerPixelWidth,
            _headerPixelHeight,
            item?.Width,
            item?.Height,
            item?.Undecodable == true);
        var light = Analysis.Light;
        var hdrFigures = HdrPresented && Analysis.HasSamples && Analysis.IsHdr;
        var maxCllChannel = hdrFigures
            ? GalleryPresent.MaxCllChannelFor(
                light.MaxCllChannel, light.MaxCllScrgb, ContentMaxScrgb, _contentMaxScrgbChannel)
            : null;
        var maxLum = HdrPresented && ContentMaxNits > 0 ? ContentMaxNits : (float?)null;
        var avgLum = HdrPresented && ContentMaxNits > 0 ? ContentAvgNits : (float?)null;
        var minLum = HdrPresented && ContentMaxNits > 0 ? ContentMinNits : (float?)null;
        var displayLum = HdrPresented && DisplayIsHdr && DisplayPeakNits > 0 ? DisplayPeakNits : (float?)null;
        var maxScrgb = HdrPresented && ContentMaxScrgb > 0 ? ContentMaxScrgb : (float?)null;
        var p99 = hdrFigures && light.P99Nits > 0 ? light.P99Nits : (float?)null;
        var fall = hdrFigures && light.FallNits > 0 ? light.FallNits : (float?)null;
        var aboveSdr = hdrFigures ? light.AboveSdrWhitePercent : (double?)null;
        var displayProbe = string.IsNullOrWhiteSpace(_displayProbe) ? null : _displayProbe;
        var gamut = Analysis.HasSamples ? Analysis.Gamut.Summary() : null;
        var frames = GifFrameCount > 1 ? GifFrameCount : (int?)null;

        ImageInfoText = GalleryPresent.ImageInfoText(new GalleryImageInfo(
            item?.FileName,
            item?.FileSize,
            pixels?.Width,
            pixels?.Height,
            CurrentProbe,
            HdrStatus,
            maxLum,
            avgLum,
            minLum,
            displayLum,
            maxScrgb,
            displayProbe,
            maxCllChannel,
            p99,
            fall,
            aboveSdr,
            gamut));

        InfoFileName = item?.FileName?.Trim() ?? "";
        InfoFileNameEllipsis = MiddleEllipsis.Truncate(InfoFileName);
        InfoPath = item?.Path?.Trim() ?? "";
        InfoPathEllipsis = MiddleEllipsis.Truncate(InfoPath, 48);
        InfoFileMetaLeft = ViewerInfoFields.FileMetaLeft(item?.FileSize, pixels?.Width, pixels?.Height);
        InfoFileMetaRight = ViewerInfoFields.FormatMetaRight(
            item?.FileName,
            frames,
            CurrentProbe.IsHdr || item?.IsHdr == true,
            PositionLabel);
        InfoFileMetaLine = ViewerInfoFields.FileMetaLine(
            item?.FileSize,
            pixels?.Width,
            pixels?.Height,
            item?.FileName,
            frames,
            CurrentProbe.IsHdr || item?.IsHdr == true,
            PositionLabel);
        InfoColor = GalleryPresent.ColorLabel(CurrentProbe) ?? "";
        InfoHdrStatus = HdrStatus?.Trim() ?? "";
        InfoHdrLine = ViewerInfoFields.ShortHdrLine(InfoHdrStatus);
        InfoMaxCll = ViewerInfoFields.ScrgbLabel(maxScrgb, maxCllChannel);
        InfoFall = ViewerInfoFields.NitsLabel(fall);
        InfoMaxLum = ViewerInfoFields.NitsLabel(maxLum);
        InfoAvgLum = ViewerInfoFields.NitsLabel(avgLum);
        InfoMinLum = ViewerInfoFields.NitsLabel(minLum);
        InfoP99 = ViewerInfoFields.NitsLabel(p99);
        InfoAboveSdr = ViewerInfoFields.PercentLabel(aboveSdr);
        InfoDisplayLum = ViewerInfoFields.NitsLabel(displayLum);
        InfoDisplayProbe = displayProbe?.Trim() ?? "";
        InfoGamutSummary = gamut?.Trim() ?? "";
        InfoShowHdrStats = ViewerInfoFields.HasHdrStatRows(
            HdrPresented, Analysis.IsHdr, maxScrgb, maxLum, p99, fall, aboveSdr);
        InfoHasColorSection = ViewerInfoFields.HasColorHdrRows(
            InfoColor, InfoHdrStatus, InfoShowHdrStats, InfoDisplayLum, InfoDisplayProbe);
        InfoVideoDuration = ViewerInfoFields.DurationLabel(_assetDurationMs);
        InfoVideoSize = ViewerInfoFields.SizeLabel(item?.FileSize);

        DetailsRows = GalleryDetails.Build(
            item?.Path,
            CurrentProbe,
            item is null ? -1 : CurrentIndex,
            _items.Count,
            pixels?.Width,
            pixels?.Height,
            item?.FileSize,
            _cameraExif,
            PreviewModel,
            _genSampler,
            _genSteps,
            PreviewSeed);
    }

    [RelayCommand]
    private void GoPrevious()
    {
        if (CurrentIndex > 0)
        {
            CurrentIndex--;
        }
    }

    [RelayCommand]
    private void GoNext()
    {
        if (CurrentIndex < _items.Count - 1)
        {
            CurrentIndex++;
        }
    }

    /// <summary>
    /// Peek ±1 for the visual pager preload (docs/specs/swipe-nav.md). Null at ends.
    /// </summary>
    public AssetItem? PeekNeighbor(int delta)
    {
        if (delta is not (-1 or 1) || _items.Count == 0)
        {
            return null;
        }

        var i = CurrentIndex + delta;
        if (i < 0 || i >= _items.Count)
        {
            return null;
        }

        return _items[i];
    }

    /// <summary>
    /// Neighbor surface source: video → mosaic poster; still → preview URI or path.
    /// Never hydrates On-Demand originals for preload.
    /// </summary>
    public static string? NeighborSourcePath(AssetItem? item)
    {
        if (item is null || item.IsFolderHeader)
        {
            return null;
        }

        if (item.Kind == AssetKind.Video)
        {
            return string.IsNullOrWhiteSpace(item.ThumbPath) ? null : item.ThumbPath;
        }

        if (!string.IsNullOrWhiteSpace(item.ThumbPath))
        {
            return item.ThumbPath;
        }

        return string.IsNullOrWhiteSpace(item.Path) ? null : item.Path;
    }

    /// <summary>Jump to an asset in this gallery snapshot (Review mode advance / undo).</summary>
    public bool TryShowAsset(string assetId)
    {
        for (var i = 0; i < _items.Count; i++)
        {
            if (!string.Equals(_items[i].Id, assetId, StringComparison.Ordinal))
            {
                continue;
            }

            if (CurrentIndex == i)
            {
                _ = LoadCurrentAsync();
            }
            else
            {
                CurrentIndex = i;
            }

            return true;
        }

        return false;
    }

    /// <summary>Rewrite paths after a Review Reject move without rebuilding the queue.</summary>
    public void ApplyPathMoves(IReadOnlyList<FileOpMove> moves)
    {
        if (moves.Count == 0)
        {
            return;
        }

        var byId = moves.ToDictionary(m => m.AssetId, StringComparer.Ordinal);
        foreach (var item in _items)
        {
            if (!byId.TryGetValue(item.Id, out var move))
            {
                continue;
            }

            item.Path = move.NewPath;
            item.FileName = Path.GetFileName(move.NewPath);
            item.SourceFolderId = move.NewSourceId;
            item.IsOrphan = false;
        }

        if (Current is not null && byId.ContainsKey(Current.Id))
        {
            _ = LoadCurrentAsync();
        }
    }

    [RelayCommand]
    private Task DownloadOriginalAsync() =>
        ErrorReporter.RunAsync("Download original", null, async () =>
        {
            var item = Current;
            if (item is null || string.IsNullOrEmpty(item.CloudItemId))
            {
                return;
            }

            var folder = await AppServices.Access.PickFolderAsync();
            if (folder is null)
            {
                return;
            }

            try
            {
                var url = await ResolveOriginalUrlAsync(item);
                if (string.IsNullOrEmpty(url))
                {
                    PreviewStatus = "Could not get a download link.";
                    return;
                }

                var dest = Path.Combine(folder.Path, item.FileName);
                dest = PathSafe.UniquePath(dest);
                await using var stream = await CloudOAuth.GetStreamAsync(url, null, CancellationToken.None);
                if (stream is null)
                {
                    PreviewStatus = "Download failed.";
                    return;
                }

                await using var output = File.Create(dest);
                await stream.CopyToAsync(output);
                PreviewStatus = "Downloaded " + Path.GetFileName(dest) + ".";
            }
            catch (Exception)
            {
                PreviewStatus = "Download failed.";
            }
        });

    [RelayCommand]
    private void OpenInExplorer()
    {
        var path = Current?.Path;
        if (string.IsNullOrEmpty(path) || !CloudFile.Exists(path))
        {
            return;
        }

        Process.Start(new ProcessStartInfo
        {
            FileName = "explorer.exe",
            Arguments = $"/select,\"{path}\"",
            UseShellExecute = true
        });
    }

    [RelayCommand]
    private void CopyPath()
    {
        var path = Current?.Path;
        if (string.IsNullOrWhiteSpace(path))
        {
            return;
        }

        if (AccessService.ShouldSkipClipboardCopy(path))
        {
            return;
        }

        var package = new Windows.ApplicationModel.DataTransfer.DataPackage();
        package.SetText(path);
        Windows.ApplicationModel.DataTransfer.Clipboard.SetContent(package);
    }

    [RelayCommand]
    private void CopyFileName()
    {
        var name = Current?.FileName;
        if (string.IsNullOrWhiteSpace(name))
        {
            return;
        }

        var package = new Windows.ApplicationModel.DataTransfer.DataPackage();
        package.SetText(name);
        Windows.ApplicationModel.DataTransfer.Clipboard.SetContent(package);
    }

    /// <summary>Open with the default associated app (shell).</summary>
    [RelayCommand]
    private void OpenWith()
    {
        var path = Current?.Path;
        if (string.IsNullOrEmpty(path) || !CloudFile.Exists(path) || CloudFile.IsOnlineOnly(path))
        {
            return;
        }

        Process.Start(new ProcessStartInfo
        {
            FileName = path,
            UseShellExecute = true
        });
    }

    private async Task LoadCurrentAsync()
    {
        var epoch = Interlocked.Increment(ref _loadEpoch);
        await UiDispatch.RunAsync(() =>
        {
            if (epoch == _loadEpoch)
            {
                ApplyGenerationPreview(GenerationFields.ForPreview(null, null, null, null));
            }
        });
        if (epoch != _loadEpoch)
        {
            return;
        }

        if (_items.Count == 0)
        {
            await UiDispatch.RunAsync(() =>
            {
                if (epoch != _loadEpoch)
                {
                    return;
                }

                Current = null;
                CurrentProbe = HdrProbe.None;
                _assetDurationMs = null;
                _cameraExif = CameraExif.None;
                _genSampler = null;
                _genSteps = null;
                CanShowHeatmap = false;
                _headerPixelWidth = 0;
                _headerPixelHeight = 0;
                _nativePixelWidth = 0;
                _nativePixelHeight = 0;
                _nativeHdrStats = false;
                _displayProbe = "";
                ContentMaxScrgb = 0;
                _contentMaxScrgbChannel = '\0';
                CurrentPath = null;
                PreviewImageUri = null;
                Title = "";
                PositionLabel = "0 / 0";
                IsImage = false;
                IsVideo = false;
                IsMissing = false;
                IsCloudPreview = false;
                PreviewStatus = "";
                CanDownloadOriginal = false;
                CanOpenInExplorer = false;
                CanGoPrevious = false;
                CanGoNext = false;
                CanScale = false;
                DetailsText = "";
                HdrStatus = "";
                HdrPresented = false;
                SetHdrForcesDisplayFit(false);
                ClearHistogram();
                ApplyGifState(false, null);
                RefreshImageInfo();
                ApplyGenerationPreview(GenerationFields.ForPreview(null, null, null, null));
                Tags.Clear();
                StillRevision++;
            });
            return;
        }

        var item = _items[CurrentIndex];
        IReadOnlyList<AssignedTag> assigned = [];
        GenerationFields.Snapshot generation = GenerationFields.ForPreview(null, null, null, null);
        try
        {
            assigned = await _catalog.GetAssignedTagsAsync(item.Id);
        }
        catch
        {
            assigned = [];
        }

        int? durationMs = null;
        try
        {
            var asset = await _catalog.GetAssetByIdAsync(item.Id);
            generation = GenerationFields.ForPreview(
                asset?.Model, asset?.Prompt, asset?.NegativePrompt, asset?.Seed);
            durationMs = asset?.DurationMs;
        }
        catch
        {
            generation = GenerationFields.ForPreview(null, null, null, null);
        }

        var onDisk = CloudFile.TryGetAttributes(item.Path, out var diskAttrs);
        var onlineOnly = onDisk && CloudFile.IsOnlineOnly(diskAttrs);
        var apiOnly = AssetItemMapper.IsApiOnly(item);
        string? previewUrl = null;
        string? previewCache = null;
        if (apiOnly && !string.IsNullOrEmpty(item.CloudItemId))
        {
            (previewUrl, previewCache) = await LoadCloudPreviewAsync(item);
        }

        if (epoch != _loadEpoch)
        {
            return;
        }

        var probe = HdrProbe.None;
        (int Width, int Height)? headerPixels = null;
        var camera = CameraExif.None;
        string? sampler = null;
        string? steps = null;
        if (item.Kind == AssetKind.Image && onDisk && !onlineOnly && !apiOnly && !item.IsOrphan)
        {
            probe = HdrFile.ProbePath(item.Path);
            headerPixels = ImageDimensions.TryRead(item.Path);
            (camera, sampler, steps) = await ReadDetailsMetadataAsync(item.Path, generation.HasAny);
            if (epoch != _loadEpoch)
            {
                return;
            }
        }

        var playableVideo = GalleryMedia.IsPlayableLocalVideo(
            item.Kind, apiOnly, onlineOnly, onDisk, item.Path);
        var stillPath = GalleryMedia.OverlayStillPath(
            item.IsOrphan,
            onlineOnly,
            onDisk,
            item.Path,
            item.ThumbPath,
            previewCache);
        var gifCanScrub = GifFrames.CanScrub(
            item.Kind, apiOnly, onlineOnly, onDisk, item.Path);
        GifFrames.Info? gifInfo = null;
        if (gifCanScrub)
        {
            gifInfo = await Task.Run(() => GifFrames.TryRead(item.Path));
            if (gifInfo is { } read && !GifFrames.FitsCacheBudget(read.Width, read.Height, read.FrameCount))
            {
                gifInfo = null;
            }
        }

        await UiDispatch.RunAsync(() =>
        {
            if (epoch != _loadEpoch)
            {
                return;
            }

            Current = item;
            CurrentProbe = probe;
            _assetDurationMs = durationMs;
            _cameraExif = camera;
            _genSampler = sampler;
            _genSteps = steps;
            CanShowHeatmap = false;
            _headerPixelWidth = headerPixels?.Width ?? 0;
            _headerPixelHeight = headerPixels?.Height ?? 0;
            _nativePixelWidth = 0;
            _nativePixelHeight = 0;
            _nativeHdrStats = false;
            _displayProbe = "";
            ContentMaxNits = 0;
            ContentAvgNits = 0;
            ContentMinNits = 0;
            ContentMaxScrgb = 0;
            _contentMaxScrgbChannel = '\0';
            DisplayPeakNits = 0;
            CurrentPath = playableVideo ? item.Path : stillPath;
            PreviewImageUri = previewUrl ?? stillPath;
            Title = item.FileName;
            PositionLabel = $"{CurrentIndex + 1} / {_items.Count}";
            IsCloudPreview = apiOnly && (previewUrl is not null || previewCache is not null);
            PreviewStatus = IsCloudPreview
                ? "Online preview — the original stays in the cloud."
                : onlineOnly
                    ? "Online-only file. Opening downloads it."
                    : "";
            IsVideo = playableVideo;
            IsImage = !playableVideo && (!string.IsNullOrEmpty(PreviewImageUri) || !string.IsNullOrEmpty(stillPath));
            IsMissing = !IsVideo && CurrentPath is null && string.IsNullOrEmpty(PreviewImageUri);
            CanDownloadOriginal = apiOnly && !string.IsNullOrEmpty(item.CloudItemId);
            CanOpenInExplorer = onDisk && !apiOnly;
            CanGoPrevious = CurrentIndex > 0;
            CanGoNext = CurrentIndex < _items.Count - 1;
            CanScale = !playableVideo && (!string.IsNullOrEmpty(PreviewImageUri) || !string.IsNullOrEmpty(stillPath));
            HdrPresented = false;
            ClearHistogram();
            ApplyGifState(gifCanScrub, gifInfo);
            var detailsPixels = GalleryPresent.FilePixelSize(
                null, null, _headerPixelWidth, _headerPixelHeight, item.Width, item.Height, item.Undecodable);
            DetailsText = GalleryPresent.DetailsLine(
                item.FileName, detailsPixels?.Width, detailsPixels?.Height, item.Kind, item.FileSize);
            HdrStatus = GalleryPresent.StatusLine(probe, false, false, GalleryPeak.Enabled, GalleryPeak.Nits) ?? "";
            ApplyGenerationPreview(generation);
            RefreshImageInfo();

            Tags.Clear();
            foreach (var tag in assigned)
            {
                Tags.Add(new AssignedTagItem
                {
                    TagId = tag.TagId,
                    TagName = tag.TagName,
                    Display = tag.TagName,
                    Source = tag.Source,
                    SourceLabel = tag.Source == TagSource.Implied ? "Implied" : tag.Source.ToString(),
                    EffectiveColor = tag.EffectiveColor
                });
            }

            // Sticky keep-zoom / default only — HDR Fit is DisplayScaling, not Scaling.
            Scaling = GalleryScale.StickyScalingAfterStillOpen(
                Scaling,
                ViewerSettings.KeepZoomBetweenImages,
                ViewerSettings.DefaultZoomMode);
            SetHdrForcesDisplayFit(probe.CanPresentHdr);
            StillRevision++;
        });

        if (!apiOnly && onDisk && onlineOnly)
        {
            _ = HydrateThenReloadAsync(item.Id, epoch);
        }
    }

    private async Task HydrateThenReloadAsync(string assetId, int epoch)
    {
        try
        {
            await AppServices.Hydration.HydrateAfterOpenAsync(assetId);
        }
        catch
        {
            return;
        }

        if (epoch != _loadEpoch)
        {
            return;
        }

        var current = _items.ElementAtOrDefault(CurrentIndex);
        if (current is null)
        {
            return;
        }

        var refreshed = await _catalog.GetAssetByIdAsync(assetId);
        if (refreshed is null || refreshed.IsOnlineOnly)
        {
            return;
        }

        current.IsOnlineOnly = refreshed.IsOnlineOnly;
        current.ContentHash = refreshed.ContentHash;
        current.ThumbPath = AppServices.Thumbnails.ExistingPathForHash(refreshed.ContentHash) ?? current.ThumbPath;
        current.Width = refreshed.Width ?? current.Width;
        current.Height = refreshed.Height ?? current.Height;
        current.Undecodable = refreshed.Undecodable;
        await LoadCurrentAsync();
    }

    private async Task<(string? Url, string? CachedPath)> LoadCloudPreviewAsync(AssetItem item)
    {
        try
        {
            var source = await _catalog.GetSourceFolderAsync(item.SourceFolderId);
            if (source is null)
            {
                return default;
            }

            var library = await AppServices.CloudLibraries.ForSourceAsync(source);
            if (library is null || string.IsNullOrEmpty(item.CloudItemId))
            {
                return default;
            }

            var url = await library.GetLargePreviewUrlAsync(item.CloudItemId);
            var asset = await _catalog.GetAssetByIdAsync(item.Id);
            var hash = asset?.ContentHash;
            if (string.IsNullOrEmpty(hash))
            {
                return (url, null);
            }

            var existing = AppServices.Thumbnails.ExistingPathForHash(hash, ThumbnailService.LargePreviewSuffix)
                ?? AppServices.Thumbnails.ExistingPathForHash(hash);
            if (existing is not null)
            {
                return (url, existing);
            }

            await using var stream = await library.OpenLargePreviewAsync(item.CloudItemId);
            if (stream is null)
            {
                return (url, null);
            }

            var cached = await AppServices.Thumbnails.CacheJpegAsync(hash, stream, ThumbnailService.LargePreviewSuffix);
            return (url, cached?.Path);
        }
        catch
        {
            return default;
        }
    }

    /// <summary>
    /// Camera EXIF + sampler / steps (extracted but not stored in the
    /// catalog) for the details rows. Local, hydrated originals only; the
    /// caller already excluded online-only / API / orphan.
    /// </summary>
    private static Task<(CameraExif Camera, string? Sampler, string? Steps)> ReadDetailsMetadataAsync(
        string path,
        bool hasGeneration) =>
        Task.Run(() =>
        {
            try
            {
                var metadata = AppServices.Metadata;
                if (metadata is null)
                {
                    return (CameraExif.None, (string?)null, (string?)null);
                }

                var camera = metadata.ReadCamera(path);
                if (camera.HasExposureRow || !hasGeneration)
                {
                    // Camera row wins; non-AI files skip the PNG text parse.
                    return (camera, (string?)null, (string?)null);
                }

                var generation = metadata.Extract(path);
                return (camera, generation.Sampler, generation.Steps);
            }
            catch
            {
                return (CameraExif.None, (string?)null, (string?)null);
            }
        });

    private void ApplyGenerationPreview(GenerationFields.Snapshot generation)
    {
        PreviewModel = generation.Model;
        PreviewPrompt = generation.Prompt;
        PreviewNegative = generation.Negative;
        PreviewSeed = generation.Seed;
        ShowGeneration = generation.HasAny;
    }

    private async Task<string?> ResolveOriginalUrlAsync(AssetItem item)
    {
        var source = await _catalog.GetSourceFolderAsync(item.SourceFolderId);
        if (source is null || string.IsNullOrEmpty(item.CloudItemId))
        {
            return null;
        }

        var library = await AppServices.CloudLibraries.ForSourceAsync(source);
        return library is null ? null : await library.GetOriginalDownloadUrlAsync(item.CloudItemId);
    }
}
