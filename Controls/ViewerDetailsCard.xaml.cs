using System.ComponentModel;
using System.Numerics;
using Microsoft.UI.Composition;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Hosting;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Palace.Helpers;
using Palace.Models;
using Palace.ViewModels;
using Windows.Foundation.Collections;
using Windows.Storage;
using Windows.UI.ViewManagement;

namespace Palace.Controls;

/// <summary>
/// Floating Details / Info panel for overlay / GalleryWindow. Docks under the
/// left toolbar (~270 epx) — does not consume image layout or cover transport.
/// </summary>
public sealed partial class ViewerDetailsCard : UserControl
{
    public static readonly DependencyProperty GalleryProperty = DependencyProperty.Register(
        nameof(Gallery),
        typeof(GalleryViewModel),
        typeof(ViewerDetailsCard),
        new PropertyMetadata(null, OnGalleryChanged));

    public static readonly DependencyProperty IsOpenProperty = DependencyProperty.Register(
        nameof(IsOpen),
        typeof(bool),
        typeof(ViewerDetailsCard),
        new PropertyMetadata(false, OnIsOpenChanged));

    private bool _sectionLoad;
    private bool _animating;
    private string? _lastAnnouncedFile;
    private GalleryViewModel? _subscribed;

    public ViewerDetailsCard()
    {
        InitializeComponent();
        RequestedTheme = ElementTheme.Dark;
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
    }

    public GalleryViewModel? Gallery
    {
        get => (GalleryViewModel?)GetValue(GalleryProperty);
        set => SetValue(GalleryProperty, value);
    }

    public bool IsOpen
    {
        get => (bool)GetValue(IsOpenProperty);
        set => SetValue(IsOpenProperty, value);
    }

    /// <summary>True while the pointer is over the card — keeps chrome from auto-hiding.</summary>
    public bool PointerOverCard { get; private set; }

    public event EventHandler? ChromePinChanged;

    public void FocusCard()
    {
        ScrDetails.Focus(FocusState.Programmatic);
    }

    public void ResetScroll()
    {
        ScrDetails.ChangeView(0, 0, null, disableAnimation: true);
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        try
        {
            BrdDetailsCard.Shadow = new ThemeShadow();
            BrdDetailsCard.Translation = new Vector3(0, 0, 24);
        }
        catch
        {
            // ThemeShadow / Translation unavailable in some hosts — solid card still works.
        }

        ApplyHighContrastBorder();
        ApplyLayout(GrdCardHost.ActualWidth, GrdCardHost.ActualHeight);
        SyncOpenState(animate: false);
        HookSectionExpanders();
        LoadSectionExpanded();
        UpdateModuleVisibility();
        UpdateLandmark();
    }

    private void HookSectionExpanders()
    {
        foreach (var expander in new[]
                 {
                     ExpFile, ExpColorHdr, ExpHistogram, ExpGamut, ExpVideo, ExpTags, ExpGeneration
                 })
        {
            expander.RegisterPropertyChangedCallback(
                Expander.IsExpandedProperty,
                OnSectionIsExpandedChanged);
        }
    }

    private void OnSectionIsExpandedChanged(DependencyObject sender, DependencyProperty dp)
    {
        if (sender is Expander expander)
        {
            PersistSection(expander, expander.IsExpanded);
        }
    }

    private void OnUnloaded(object sender, RoutedEventArgs e) =>
        UnsubscribeGallery();

    private static void OnGalleryChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is ViewerDetailsCard card)
        {
            card.SubscribeGallery(e.NewValue as GalleryViewModel);
            card.UpdateModuleVisibility();
            card.UpdateLandmark();
            card.ResetScroll();
            card.AnnounceFileChange();
        }
    }

    private static void OnIsOpenChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is ViewerDetailsCard card)
        {
            card.SyncOpenState(animate: true);
            card.AnnounceOpenState();
        }
    }

    private void SubscribeGallery(GalleryViewModel? gallery)
    {
        UnsubscribeGallery();
        _subscribed = gallery;
        if (gallery is not null)
        {
            gallery.PropertyChanged += Gallery_PropertyChanged;
        }
    }

    private void UnsubscribeGallery()
    {
        if (_subscribed is not null)
        {
            _subscribed.PropertyChanged -= Gallery_PropertyChanged;
            _subscribed = null;
        }
    }

    private void Gallery_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(GalleryViewModel.Current)
            or nameof(GalleryViewModel.CurrentKind)
            or nameof(GalleryViewModel.Title)
            or nameof(GalleryViewModel.CurrentIndex)
            or nameof(GalleryViewModel.ShowHistogram)
            or nameof(GalleryViewModel.ShowGamut)
            or nameof(GalleryViewModel.InfoHasColorSection)
            or nameof(GalleryViewModel.IsVideo))
        {
            UpdateModuleVisibility();
            UpdateLandmark();
            if (e.PropertyName is nameof(GalleryViewModel.Current)
                or nameof(GalleryViewModel.CurrentIndex)
                or nameof(GalleryViewModel.Title))
            {
                ResetScroll();
                AnnounceFileChange();
            }
        }
        else if (e.PropertyName is nameof(GalleryViewModel.ShowGeneration))
        {
            UpdateModuleVisibility();
        }
        else if (e.PropertyName is nameof(GalleryViewModel.ShowDetails))
        {
            IsOpen = Gallery?.ShowDetails == true;
        }
    }

    private void GrdCardHost_SizeChanged(object sender, SizeChangedEventArgs e) =>
        ApplyLayout(e.NewSize.Width, e.NewSize.Height);

    private void ApplyLayout(double width, double height)
    {
        var panelWidth = ViewerDetailsLayout.EffectivePanelWidth(width);
        if (Gallery is not null)
        {
            Gallery.DetailsUsesBottomSheet = false;
            Gallery.DetailsPanelWidth = panelWidth;
        }

        BrdDetailsCard.Width = panelWidth;
        BrdDetailsCard.MinWidth = ViewerDetailsLayout.PanelMinWidth;
        BrdDetailsCard.MaxHeight = ViewerDetailsLayout.PanelMaxHeight(height);
        BrdDetailsCard.HorizontalAlignment = HorizontalAlignment.Left;
        BrdDetailsCard.VerticalAlignment = VerticalAlignment.Top;
        BrdDetailsCard.Margin = new Thickness(
            ViewerDetailsLayout.Inset,
            ViewerDetailsLayout.TopInset,
            ViewerDetailsLayout.Inset,
            ViewerDetailsLayout.Inset);
    }

    private void SyncOpenState(bool animate)
    {
        var open = IsOpen || Gallery?.ShowDetails == true;
        if (open)
        {
            BrdDetailsCard.Visibility = Visibility.Visible;
            PlayOpenAnimation(animate);
        }
        else if (BrdDetailsCard.Visibility == Visibility.Visible)
        {
            PlayCloseAnimation(animate);
        }
    }

    private void PlayOpenAnimation(bool animate)
    {
        var reduce = IsAnimationDisabled();
        if (!animate || reduce)
        {
            BrdDetailsCard.Opacity = 1;
            SetTranslationY(0);
            return;
        }

        Animate(fromY: ViewerDetailsLayout.EnterOffsetY, toY: 0, fromOpacity: 0, toOpacity: 1, ViewerDetailsLayout.EnterMs, easeOut: true);
    }

    private void PlayCloseAnimation(bool animate)
    {
        var reduce = IsAnimationDisabled();
        if (!animate || reduce)
        {
            BrdDetailsCard.Opacity = 0;
            BrdDetailsCard.Visibility = Visibility.Collapsed;
            SetTranslationY(ViewerDetailsLayout.EnterOffsetY);
            return;
        }

        Animate(
            fromY: 0,
            toY: ViewerDetailsLayout.EnterOffsetY,
            fromOpacity: 1,
            toOpacity: 0,
            ViewerDetailsLayout.ExitMs,
            easeOut: false,
            onComplete: () =>
            {
                BrdDetailsCard.Visibility = Visibility.Collapsed;
            });
    }

    private void Animate(
        double fromY,
        double toY,
        double fromOpacity,
        double toOpacity,
        int ms,
        bool easeOut,
        Action? onComplete = null)
    {
        if (_animating)
        {
            onComplete?.Invoke();
        }

        _animating = true;
        var visual = ElementCompositionPreview.GetElementVisual(BrdDetailsCard);
        var compositor = visual.Compositor;
        var duration = TimeSpan.FromMilliseconds(ms);
        var easing = easeOut
            ? compositor.CreateCubicBezierEasingFunction(new Vector2(0.1f, 0.9f), new Vector2(0.2f, 1.0f))
            : compositor.CreateCubicBezierEasingFunction(new Vector2(0.4f, 0.0f), new Vector2(1.0f, 1.0f));

        BrdDetailsCard.Opacity = fromOpacity;
        SetTranslationY(fromY);

        var opacityAnim = compositor.CreateScalarKeyFrameAnimation();
        opacityAnim.InsertKeyFrame(0, (float)fromOpacity);
        opacityAnim.InsertKeyFrame(1, (float)toOpacity, easing);
        opacityAnim.Duration = duration;

        var offsetAnim = compositor.CreateScalarKeyFrameAnimation();
        offsetAnim.InsertKeyFrame(0, (float)fromY);
        offsetAnim.InsertKeyFrame(1, (float)toY, easing);
        offsetAnim.Duration = duration;

        var batch = compositor.CreateScopedBatch(CompositionBatchTypes.Animation);
        batch.Completed += (_, _) =>
        {
            _animating = false;
            BrdDetailsCard.Opacity = toOpacity;
            SetTranslationY(toY);
            onComplete?.Invoke();
        };
        visual.StartAnimation("Opacity", opacityAnim);
        visual.StartAnimation("Offset.Y", offsetAnim);
        batch.End();
    }

    private void SetTranslationY(double y)
    {
        try
        {
            var current = BrdDetailsCard.Translation;
            BrdDetailsCard.Translation = new Vector3(current.X, (float)y, current.Z == 0 ? 24 : current.Z);
        }
        catch
        {
            // Ignore when Translation is unavailable.
        }
    }

    private static bool IsAnimationDisabled()
    {
        try
        {
            var settings = new UISettings();
            return !settings.AnimationsEnabled;
        }
        catch
        {
            return false;
        }
    }

    private void UpdateModuleVisibility()
    {
        var kind = Gallery?.CurrentKind ?? AssetKind.Other;
        var showStill = DetailsModuleCatalog.Includes(DetailsModuleKind.ColorHdr, kind);
        ExpColorHdr.Visibility = BindHelpers.BoolToVisibility(
            showStill && Gallery?.InfoHasColorSection == true);
        ExpHistogram.Visibility = BindHelpers.BoolToVisibility(
            showStill && Gallery?.ShowHistogram == true);
        ExpGamut.Visibility = BindHelpers.BoolToVisibility(
            showStill && Gallery?.ShowGamut == true);
        ExpVideo.Visibility = BindHelpers.BoolToVisibility(
            DetailsModuleCatalog.Includes(DetailsModuleKind.Video, kind));
    }

    private void UpdateLandmark()
    {
        var name = BindHelpers.DetailsLandmarkName(Gallery?.Title);
        AutomationProperties.SetName(BrdDetailsCard, name);
        AutomationProperties.SetLocalizedLandmarkType(BrdDetailsCard, "Details");
    }

    private void AnnounceOpenState()
    {
        var peer = FrameworkElementAutomationPeer.FromElement(BrdDetailsCard)
            ?? FrameworkElementAutomationPeer.CreatePeerForElement(BrdDetailsCard);
        peer?.RaiseAutomationEvent(AutomationEvents.LiveRegionChanged);
        try
        {
            var text = IsOpen ? "Details shown" : "Details hidden";
            peer?.RaiseNotificationEvent(
                AutomationNotificationKind.Other,
                AutomationNotificationProcessing.ImportantMostRecent,
                text,
                "ViewerDetailsOpen");
        }
        catch
        {
            // RaiseNotificationEvent needs a newer contract on some builds.
        }
    }

    private void AnnounceFileChange()
    {
        var title = Gallery?.Title;
        if (string.IsNullOrWhiteSpace(title) || title == _lastAnnouncedFile || !IsOpen)
        {
            return;
        }

        _lastAnnouncedFile = title;
        try
        {
            var peer = FrameworkElementAutomationPeer.FromElement(BrdDetailsCard)
                ?? FrameworkElementAutomationPeer.CreatePeerForElement(BrdDetailsCard);
            peer?.RaiseNotificationEvent(
                AutomationNotificationKind.Other,
                AutomationNotificationProcessing.ImportantMostRecent,
                title,
                "ViewerDetailsFile");
        }
        catch
        {
            // Optional polite announcement.
        }
    }

    private void LoadSectionExpanded()
    {
        _sectionLoad = true;
        try
        {
            var values = ApplicationData.Current.LocalSettings.Values;
            ApplyExpanded(ExpFile, DetailsModuleCatalog.File, values);
            ApplyExpanded(ExpColorHdr, DetailsModuleCatalog.ColorHdr, values);
            ApplyExpanded(ExpHistogram, DetailsModuleCatalog.Histogram, values);
            ApplyExpanded(ExpGamut, DetailsModuleCatalog.Gamut, values);
            ApplyExpanded(ExpVideo, DetailsModuleCatalog.Video, values);
            ApplyExpanded(ExpTags, DetailsModuleCatalog.Tags, values);
            ApplyExpanded(ExpGeneration, DetailsModuleCatalog.Generation, values);
        }
        finally
        {
            _sectionLoad = false;
        }
    }

    private static void ApplyExpanded(
        Expander expander,
        DetailsModule module,
        IPropertySet values)
    {
        expander.IsExpanded = GalleryChrome.ParseSectionExpanded(
            values[module.CollapseSettingsKey],
            module.DefaultExpanded);
    }

    private void PersistSection(Expander sender, bool expanded)
    {
        if (_sectionLoad || sender.Tag is not string id)
        {
            return;
        }

        ApplicationData.Current.LocalSettings.Values[GalleryChrome.DetailsSectionKey(id)] = expanded;
    }

    private void BtnDetailsClose_Click(object sender, RoutedEventArgs e)
    {
        if (Gallery is not null)
        {
            Gallery.ShowDetails = false;
        }

        IsOpen = false;
    }

    private void BrdDetailsCard_PointerEntered(object sender, PointerRoutedEventArgs e)
    {
        PointerOverCard = true;
        ChromePinChanged?.Invoke(this, EventArgs.Empty);
    }

    private void BrdDetailsCard_PointerExited(object sender, PointerRoutedEventArgs e)
    {
        PointerOverCard = false;
        ChromePinChanged?.Invoke(this, EventArgs.Empty);
    }

    private void ScrDetails_PointerWheelChanged(object sender, PointerRoutedEventArgs e)
    {
        // Keep wheel scrolling inside the card; do not bubble to the still.
        e.Handled = true;
    }

    private void ApplyHighContrastBorder()
    {
        try
        {
            var accessibility = new AccessibilitySettings();
            if (accessibility.HighContrast)
            {
                BrdDetailsCard.BorderThickness = new Thickness(2);
            }
        }
        catch
        {
            // AccessibilitySettings may throw off the UI thread / in tests.
        }
    }
}
