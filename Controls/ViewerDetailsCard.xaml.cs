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
/// Floating Details panel for overlay / GalleryWindow. Does not consume image
/// layout — docks right (340 epx) or bottom sheet under 720 epx width.
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
            BrdDetailsCard.Translation = new Vector3(0, 0, 32);
        }
        catch
        {
            // ThemeShadow / Translation unavailable in some hosts — solid card still works.
        }

        ApplyHighContrastBorder();
        ApplyLayout(GrdCardHost.ActualWidth, GrdCardHost.ActualHeight);
        SyncOpenState(animate: false);
        LoadSectionExpanded();
        UpdateModuleVisibility();
        UpdateLandmark();
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
            or nameof(GalleryViewModel.CurrentIndex))
        {
            UpdateModuleVisibility();
            UpdateLandmark();
            ResetScroll();
            AnnounceFileChange();
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
        var bottom = ViewerDetailsLayout.UsesBottomSheet(width);
        var panelWidth = ViewerDetailsLayout.EffectivePanelWidth(width, bottom);

        if (Gallery is not null)
        {
            Gallery.DetailsUsesBottomSheet = bottom;
            Gallery.DetailsPanelWidth = panelWidth;
        }

        if (bottom)
        {
            BrdDetailsCard.Width = double.NaN;
            BrdDetailsCard.MinWidth = 0;
            BrdDetailsCard.MaxHeight = ViewerDetailsLayout.BottomSheetMaxHeight(height);
            BrdDetailsCard.HorizontalAlignment = HorizontalAlignment.Stretch;
            BrdDetailsCard.VerticalAlignment = VerticalAlignment.Bottom;
            BrdDetailsCard.Margin = new Thickness(
                ViewerDetailsLayout.Inset,
                0,
                ViewerDetailsLayout.Inset,
                ViewerDetailsLayout.Inset);
        }
        else
        {
            BrdDetailsCard.Width = panelWidth;
            BrdDetailsCard.MinWidth = ViewerDetailsLayout.PanelMinWidth;
            BrdDetailsCard.MaxHeight = double.PositiveInfinity;
            BrdDetailsCard.HorizontalAlignment = HorizontalAlignment.Right;
            BrdDetailsCard.VerticalAlignment = VerticalAlignment.Stretch;
            BrdDetailsCard.Margin = new Thickness(
                0,
                ViewerDetailsLayout.TopInset,
                ViewerDetailsLayout.Inset,
                ViewerDetailsLayout.Inset);
        }
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
            SetTranslationX(0);
            return;
        }

        Animate(fromX: ViewerDetailsLayout.EnterOffsetX, toX: 0, fromOpacity: 0, toOpacity: 1, ViewerDetailsLayout.EnterMs, easeOut: true);
    }

    private void PlayCloseAnimation(bool animate)
    {
        var reduce = IsAnimationDisabled();
        if (!animate || reduce)
        {
            BrdDetailsCard.Opacity = 0;
            BrdDetailsCard.Visibility = Visibility.Collapsed;
            SetTranslationX(ViewerDetailsLayout.EnterOffsetX);
            return;
        }

        Animate(
            fromX: 0,
            toX: ViewerDetailsLayout.EnterOffsetX,
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
        double fromX,
        double toX,
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
        SetTranslationX(fromX);

        var opacityAnim = compositor.CreateScalarKeyFrameAnimation();
        opacityAnim.InsertKeyFrame(0, (float)fromOpacity);
        opacityAnim.InsertKeyFrame(1, (float)toOpacity, easing);
        opacityAnim.Duration = duration;

        var offsetAnim = compositor.CreateScalarKeyFrameAnimation();
        offsetAnim.InsertKeyFrame(0, (float)fromX);
        offsetAnim.InsertKeyFrame(1, (float)toX, easing);
        offsetAnim.Duration = duration;

        var batch = compositor.CreateScopedBatch(CompositionBatchTypes.Animation);
        batch.Completed += (_, _) =>
        {
            _animating = false;
            BrdDetailsCard.Opacity = toOpacity;
            SetTranslationX(toX);
            onComplete?.Invoke();
        };
        visual.StartAnimation("Opacity", opacityAnim);
        visual.StartAnimation("Offset.X", offsetAnim);
        batch.End();
    }

    private void SetTranslationX(double x)
    {
        try
        {
            var current = BrdDetailsCard.Translation;
            BrdDetailsCard.Translation = new Vector3((float)x, current.Y, current.Z == 0 ? 32 : current.Z);
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
        ExpImage.Visibility = BindHelpers.BoolToVisibility(DetailsModuleCatalog.Includes(DetailsModuleKind.Image, kind));
        ExpVideo.Visibility = BindHelpers.BoolToVisibility(DetailsModuleCatalog.Includes(DetailsModuleKind.Video, kind));
    }

    private void UpdateLandmark()
    {
        var name = helpersName();
        AutomationProperties.SetName(BrdDetailsCard, name);
        AutomationProperties.SetLocalizedLandmarkType(BrdDetailsCard, "Details");

        string helpersName() => BindHelpers.DetailsLandmarkName(Gallery?.Title);
    }

    private void AnnounceOpenState()
    {
        var peer = FrameworkElementAutomationPeer.FromElement(BrdDetailsCard)
            ?? FrameworkElementAutomationPeer.CreatePeerForElement(BrdDetailsCard);
        peer?.RaiseAutomationEvent(
            IsOpen ? AutomationEvents.LiveRegionChanged : AutomationEvents.LiveRegionChanged);
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
            ApplyExpanded(ExpTags, DetailsModuleCatalog.Tags, values);
            ApplyExpanded(ExpImage, DetailsModuleCatalog.Image, values);
            ApplyExpanded(ExpVideo, DetailsModuleCatalog.Video, values);
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

    private void Section_Expanded(Expander sender, ExpanderExpandedEventArgs args) =>
        PersistSection(sender, expanded: true);

    private void Section_Collapsed(Expander sender, ExpanderCollapsedEventArgs args) =>
        PersistSection(sender, expanded: false);

    private void PersistSection(Expander sender, bool expanded)
    {
        if (_sectionLoad || sender.Tag is not string id)
        {
            return;
        }

        ApplicationData.Current.LocalSettings.Values[GalleryChrome.DetailsSectionKey(id)] = expanded;
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
