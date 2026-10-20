using FluentIcons.Common;
using Microsoft.UI.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;
using Palace.Helpers;
using Windows.Foundation;
using Windows.System;
using Windows.UI;
using Windows.UI.Core;
using Windows.UI.ViewManagement;

namespace Palace.Controls;

/// <summary>
/// Reusable mute + expanding volume slider (Gooey). No flyout — click mutes,
/// hover/focus grows a 100 DIP slider to the right. Wire
/// <see cref="InteractionChanged"/> so transport auto-hide stays up while
/// hovered/focused/dragging; collapse the pill before fading chrome.
/// </summary>
public sealed partial class VolumeControl : UserControl
{
    public static readonly DependencyProperty MuteButtonAutomationIdProperty =
        DependencyProperty.Register(
            nameof(MuteButtonAutomationId),
            typeof(string),
            typeof(VolumeControl),
            new PropertyMetadata("BtnGalleryVolume", OnMuteButtonAutomationIdChanged));

    public static readonly DependencyProperty SliderAutomationIdProperty =
        DependencyProperty.Register(
            nameof(SliderAutomationId),
            typeof(string),
            typeof(VolumeControl),
            new PropertyMetadata("SldGalleryVolume", OnSliderAutomationIdChanged));

    public static readonly DependencyProperty RootAutomationIdProperty =
        DependencyProperty.Register(
            nameof(RootAutomationId),
            typeof(string),
            typeof(VolumeControl),
            new PropertyMetadata("VolGalleryVolume", OnRootAutomationIdChanged));

    private readonly DispatcherTimer _collapseTimer = new()
    {
        Interval = TimeSpan.FromMilliseconds(GalleryVolume.CollapseIdleMs)
    };

    private readonly DispatcherTimer _hudTimer = new()
    {
        Interval = TimeSpan.FromMilliseconds(GalleryVolume.HudVisibleMs)
    };

    private bool _expanded;
    private bool _pointerInside;
    private bool _focusInside;
    private bool _dragging;
    private bool _touchArmed;
    private bool _suppressSlider;
    private bool _loaded;
    private DoubleAnimation? _expandAnimation;
    private Storyboard? _expandBoard;

    public VolumeControl()
    {
        _transportKeepAliveHandler = OnTransportKeepAlive;
        InitializeComponent();
        AutomationProperties.SetAutomationId(this, RootAutomationId);
        _collapseTimer.Tick += CollapseTimer_Tick;
        _hudTimer.Tick += HudTimer_Tick;
        Loaded += VolumeControl_Loaded;
        Unloaded += VolumeControl_Unloaded;
        BrdPill.PointerEntered += Pill_PointerEntered;
        BrdPill.PointerExited += Pill_PointerExited;
        BrdPill.PointerPressed += Pill_PointerPressed;
        BrdPill.PointerWheelChanged += Pill_PointerWheelChanged;
        BtnMute.GotFocus += Chrome_GotFocus;
        BtnMute.LostFocus += Chrome_LostFocus;
        BtnMute.PreviewKeyDown += Mute_PreviewKeyDown;
        SldVolume.GotFocus += Chrome_GotFocus;
        SldVolume.LostFocus += Chrome_LostFocus;
        SldVolume.ValueChanged += SldVolume_ValueChanged;
        SldVolume.AddHandler(
            UIElement.PointerPressedEvent,
            new PointerEventHandler(Slider_PointerPressed),
            handledEventsToo: true);
        SldVolume.AddHandler(
            UIElement.PointerReleasedEvent,
            new PointerEventHandler(Slider_PointerReleased),
            handledEventsToo: true);
        SldVolume.AddHandler(
            UIElement.PointerCanceledEvent,
            new PointerEventHandler(Slider_PointerReleased),
            handledEventsToo: true);
        SldVolume.AddHandler(
            UIElement.PointerCaptureLostEvent,
            new PointerEventHandler(Slider_PointerReleased),
            handledEventsToo: true);
        SldVolume.PointerWheelChanged += Pill_PointerWheelChanged;
    }

    /// <summary>Raised when volume or mute changes (after GalleryVolume.Apply).</summary>
    public event EventHandler? VolumeChanged;

    /// <summary>
    /// Raised when hover/focus/drag interaction starts or ends — parents keep
    /// MediaPlayer transport visible while true.
    /// </summary>
    public event EventHandler? InteractionChanged;

    /// <summary>True while pointer/focus is inside or the slider is dragging.</summary>
    public bool IsInteracting => _pointerInside || _focusInside || _dragging || _expanded;

    /// <summary>True while the horizontal slider pill is expanded.</summary>
    public bool IsExpanded => _expanded;

    public string MuteButtonAutomationId
    {
        get => (string)GetValue(MuteButtonAutomationIdProperty);
        set => SetValue(MuteButtonAutomationIdProperty, value);
    }

    public string SliderAutomationId
    {
        get => (string)GetValue(SliderAutomationIdProperty);
        set => SetValue(SliderAutomationIdProperty, value);
    }

    public string RootAutomationId
    {
        get => (string)GetValue(RootAutomationIdProperty);
        set => SetValue(RootAutomationIdProperty, value);
    }

    public void SyncFromStore()
    {
        ApplyChrome(showHud: false, raiseChanged: false);
    }

    /// <summary>Push the shared GalleryVolume state onto a MediaPlayer.</summary>
    public static void ApplyToPlayer(Windows.Media.Playback.MediaPlayer? player)
    {
        if (player is null)
        {
            return;
        }

        player.Volume = GalleryVolume.ToPlayerVolume(GalleryVolume.Volume, GalleryVolume.IsMuted);
        player.IsMuted = GalleryVolume.IsMuted;
    }

    private MediaTransportControls? _transport;
    private readonly EventHandler _transportKeepAliveHandler;

    /// <summary>
    /// Hide the stock Mute flyout button and keep transport up while this
    /// control is interacting (hover / focus / drag).
    /// </summary>
    public void AttachTransport(MediaTransportControls? transport)
    {
        if (ReferenceEquals(_transport, transport))
        {
            if (_transport is not null)
            {
                _transport.IsVolumeButtonVisible = false;
            }

            return;
        }

        if (_transport is not null)
        {
            InteractionChanged -= _transportKeepAliveHandler;
        }

        _transport = transport;
        if (_transport is null)
        {
            return;
        }

        _transport.IsVolumeButtonVisible = false;
        InteractionChanged += _transportKeepAliveHandler;
    }

    private void OnTransportKeepAlive(object? sender, EventArgs e)
    {
        if (_transport is null)
        {
            return;
        }

        if (IsInteracting)
        {
            _transport.Show();
            return;
        }

        // Pill collapses on its own timer before transport's idle fade.
        if (_expanded)
        {
            ScheduleCollapse();
        }
    }

    public void CollapsePill(bool animate = true)
    {
        _touchArmed = false;
        SetExpanded(false, animate);
        StopCollapseTimer();
        RaiseInteraction();
    }

    public bool TryHandleViewerKey(bool videoShowing, bool isTyping, bool shiftDown, VirtualKey key)
    {
        if (!GalleryVolume.HandlesViewerKey(videoShowing, isTyping, (int)key))
        {
            return false;
        }

        if (key == VirtualKey.M)
        {
            ToggleMute(announce: true, showHud: true);
            return true;
        }

        if (key is VirtualKey.Up or VirtualKey.Down)
        {
            var delta = GalleryVolume.KeyDelta(up: key == VirtualKey.Up, fine: shiftDown);
            ApplyNudge(delta, showHud: true);
            EnsureExpandedForChange();
            return true;
        }

        return false;
    }

    public bool OwnsFocus(object? source)
    {
        for (var current = source as DependencyObject;
             current is not null;
             current = Microsoft.UI.Xaml.Media.VisualTreeHelper.GetParent(current))
        {
            if (ReferenceEquals(current, this)
                || ReferenceEquals(current, BtnMute)
                || ReferenceEquals(current, SldVolume)
                || ReferenceEquals(current, BrdPill))
            {
                return true;
            }

            if (current is FrameworkElement element
                && GalleryVolume.IsVolumeChromeAutomationId(
                    AutomationProperties.GetAutomationId(element)))
            {
                return true;
            }
        }

        return false;
    }

    private void VolumeControl_Loaded(object sender, RoutedEventArgs e)
    {
        if (_loaded)
        {
            return;
        }

        _loaded = true;
        AutomationProperties.SetAutomationId(BtnMute, MuteButtonAutomationId);
        AutomationProperties.SetAutomationId(SldVolume, SliderAutomationId);
        AutomationProperties.SetAutomationId(this, RootAutomationId);
        GalleryVolume.Changed += GalleryVolume_Changed;
        ActualThemeChanged += VolumeControl_ActualThemeChanged;
        ApplySliderFill();
        ApplyChrome(showHud: false, raiseChanged: false);
        SldVolume.Visibility = Visibility.Collapsed;
    }

    private void VolumeControl_Unloaded(object sender, RoutedEventArgs e)
    {
        GalleryVolume.Changed -= GalleryVolume_Changed;
        ActualThemeChanged -= VolumeControl_ActualThemeChanged;
        StopCollapseTimer();
        _hudTimer.Stop();
        _loaded = false;
    }

    private void GalleryVolume_Changed(object? sender, EventArgs e) =>
        DispatcherQueue.TryEnqueue(() => ApplyChrome(showHud: false, raiseChanged: false));

    private void VolumeControl_ActualThemeChanged(FrameworkElement sender, object args) =>
        ApplySliderFill();

    private void BtnMute_Click(object sender, RoutedEventArgs e)
    {
        // Touch: first tap expands only; second tap mutes.
        if (_touchArmed && !_expanded)
        {
            SetExpanded(true, animate: true);
            _touchArmed = false;
            ScheduleCollapse();
            RaiseInteraction();
            return;
        }

        if (_touchArmed && _expanded)
        {
            ToggleMute(announce: true, showHud: true);
            _touchArmed = false;
            return;
        }

        ToggleMute(announce: true, showHud: true);
    }

    private void Mute_PreviewKeyDown(object sender, KeyRoutedEventArgs e)
    {
        var shift = IsShiftDown();
        switch (e.Key)
        {
            case VirtualKey.Enter:
            case VirtualKey.Space:
                ToggleMute(announce: true, showHud: true);
                e.Handled = true;
                break;
            case VirtualKey.Right:
                SetExpanded(true, animate: true);
                SldVolume.Visibility = Visibility.Visible;
                SldVolume.Focus(FocusState.Programmatic);
                e.Handled = true;
                break;
            case VirtualKey.Up:
            case VirtualKey.Down:
                ApplyNudge(
                    GalleryVolume.KeyDelta(up: e.Key == VirtualKey.Up, fine: shift),
                    showHud: true);
                EnsureExpandedForChange();
                e.Handled = true;
                break;
            case VirtualKey.M:
                ToggleMute(announce: true, showHud: true);
                e.Handled = true;
                break;
        }
    }

    private void SldVolume_ValueChanged(object sender, RangeBaseValueChangedEventArgs e)
    {
        if (_suppressSlider || !_loaded)
        {
            return;
        }

        var volume = GalleryVolume.Clamp((int)Math.Round(e.NewValue));
        var muted = volume <= GalleryVolume.MinVolume;
        if (!GalleryVolume.IsMuted && muted && GalleryVolume.Volume > GalleryVolume.MinVolume)
        {
            // Nudge path remembers via GalleryVolume.Nudge; direct drag to 0 mutes.
        }

        GalleryVolume.Apply(volume, muted);
        Persist();
        ApplyChrome(showHud: true, raiseChanged: true);
        if (_dragging)
        {
            RaiseInteraction();
        }
    }

    private void Slider_PointerPressed(object sender, PointerRoutedEventArgs e)
    {
        _dragging = true;
        StopCollapseTimer();
        RaiseInteraction();
    }

    private void Slider_PointerReleased(object sender, PointerRoutedEventArgs e)
    {
        if (!_dragging)
        {
            return;
        }

        _dragging = false;
        ScheduleCollapse();
        RaiseInteraction();
    }

    private void Pill_PointerEntered(object sender, PointerRoutedEventArgs e)
    {
        _pointerInside = true;
        StopCollapseTimer();
        SetExpanded(true, animate: true);
        RaiseInteraction();
    }

    private void Pill_PointerExited(object sender, PointerRoutedEventArgs e)
    {
        _pointerInside = false;
        ScheduleCollapse();
        RaiseInteraction();
    }

    private void Pill_PointerPressed(object sender, PointerRoutedEventArgs e)
    {
        var point = e.GetCurrentPoint(BrdPill);
        if (point.PointerDeviceType is PointerDeviceType.Touch or PointerDeviceType.Pen)
        {
            _touchArmed = true;
        }

        StopCollapseTimer();
        RaiseInteraction();
    }

    private void Pill_PointerWheelChanged(object sender, PointerRoutedEventArgs e)
    {
        var props = e.GetCurrentPoint(BrdPill).Properties;
        var notches = props.MouseWheelDelta / 120;
        if (notches == 0)
        {
            notches = props.MouseWheelDelta > 0 ? 1 : -1;
        }

        var delta = GalleryVolume.WheelDelta(notches, fine: IsShiftDown());
        ApplyNudge(delta, showHud: true);
        EnsureExpandedForChange();
        e.Handled = true;
    }

    private void Chrome_GotFocus(object sender, RoutedEventArgs e)
    {
        _focusInside = true;
        StopCollapseTimer();
        SetExpanded(true, animate: true);
        RaiseInteraction();
    }

    private void Chrome_LostFocus(object sender, RoutedEventArgs e)
    {
        // Defer — focus may move button → slider inside the pill.
        DispatcherQueue.TryEnqueue(() =>
        {
            _focusInside = OwnsFocus(FocusManager.GetFocusedElement(XamlRoot));
            if (!_focusInside && !_pointerInside && !_dragging)
            {
                ScheduleCollapse();
            }

            RaiseInteraction();
        });
    }

    private void CollapseTimer_Tick(object? sender, object e)
    {
        StopCollapseTimer();
        if (_pointerInside || _focusInside || _dragging)
        {
            return;
        }

        SetExpanded(false, animate: true);
        RaiseInteraction();
    }

    private void HudTimer_Tick(object? sender, object e)
    {
        _hudTimer.Stop();
        HideHud();
    }

    private void ToggleMute(bool announce, bool showHud)
    {
        GalleryVolume.ToggleMute();
        Persist();
        ApplyChrome(showHud, raiseChanged: true);
        if (announce)
        {
            AnnounceMute();
        }
    }

    private void ApplyNudge(int delta, bool showHud)
    {
        GalleryVolume.Nudge(delta);
        Persist();
        ApplyChrome(showHud, raiseChanged: true);
    }

    private void EnsureExpandedForChange()
    {
        if (!_expanded)
        {
            SetExpanded(true, animate: true);
        }

        ScheduleCollapse();
        RaiseInteraction();
    }

    private void ApplyChrome(bool showHud, bool raiseChanged)
    {
        var volume = GalleryVolume.Volume;
        var muted = GalleryVolume.IsMuted;
        var icon = GalleryVolume.IconFor(volume, muted);
        var fluent = ToFluent(icon);

        IcnSpeaker.Icon = fluent;
        IcnHud.Icon = fluent;
        TxtHud.Text = GalleryVolume.HudText(volume, muted);

        AutomationProperties.SetName(BtnMute, GalleryVolume.MuteButtonName(muted));
        AutomationProperties.SetItemStatus(BtnMute, GalleryVolume.VolumeStatus(volume, muted));
        ToolTipService.SetToolTip(BtnMute, GalleryVolume.MuteButtonTooltip(muted));
        ToolTipService.SetToolTip(SldVolume, GalleryVolume.SliderTooltip(volume, muted));

        _suppressSlider = true;
        SldVolume.Value = GalleryVolume.EffectiveVolume(volume, muted);
        _suppressSlider = false;

        ApplySliderFill();

        if (showHud)
        {
            ShowHud();
        }

        if (raiseChanged)
        {
            VolumeChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    private void ApplySliderFill()
    {
        Brush fill;
        if (new AccessibilitySettings().HighContrast)
        {
            fill = (Brush)Resources["VolumeSliderFillBrush"];
        }
        else if (GalleryVolume.UsePalacePinkFill(
                     Windows.Storage.ApplicationData.Current.LocalSettings.Values[BrandAccent.SettingsKey]))
        {
            fill = (Brush)Application.Current.Resources["AccentFillColorDefaultBrush"];
        }
        else
        {
            fill = (Brush)Resources["VolumeSliderFillBrush"];
        }

        SldVolume.Resources["SliderTrackValueFill"] = fill;
        SldVolume.Resources["SliderTrackValueFillPointerOver"] = fill;
        SldVolume.Resources["SliderTrackValueFillPressed"] = fill;
        SldVolume.Resources["SliderTrackValueFillDisabled"] = fill;
    }

    private void SetExpanded(bool expanded, bool animate)
    {
        if (_expanded == expanded && _expandBoard is null)
        {
            // Still ensure slider visibility matches.
        }

        _expanded = expanded;
        var target = expanded ? GalleryVolume.SliderWidthDip : 0;

        if (!animate || !_loaded)
        {
            _expandBoard?.Stop();
            GrdSliderHost.Width = target;
            SldVolume.Visibility = expanded ? Visibility.Visible : Visibility.Collapsed;
            return;
        }

        SldVolume.Visibility = Visibility.Visible;
        _expandBoard?.Stop();
        _expandAnimation = new DoubleAnimation
        {
            To = target,
            Duration = TimeSpan.FromMilliseconds(GalleryVolume.ExpandMs),
            EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut }
        };
        Storyboard.SetTarget(_expandAnimation, GrdSliderHost);
        Storyboard.SetTargetProperty(_expandAnimation, "Width");
        _expandBoard = new Storyboard();
        _expandBoard.Children.Add(_expandAnimation);
        _expandBoard.Completed += (_, _) =>
        {
            if (!_expanded)
            {
                SldVolume.Visibility = Visibility.Collapsed;
            }
        };
        _expandBoard.Begin();
    }

    private void ScheduleCollapse()
    {
        StopCollapseTimer();
        if (_pointerInside || _focusInside || _dragging)
        {
            return;
        }

        _collapseTimer.Start();
    }

    private void StopCollapseTimer() => _collapseTimer.Stop();

    private void ShowHud()
    {
        BrdHud.Visibility = Visibility.Visible;
        BrdHud.Opacity = 1;
        _hudTimer.Stop();
        _hudTimer.Start();
    }

    private void HideHud()
    {
        BrdHud.Opacity = 0;
        BrdHud.Visibility = Visibility.Collapsed;
    }

    private void AnnounceMute()
    {
        var peer = FrameworkElementAutomationPeer.FromElement(BtnMute)
            ?? FrameworkElementAutomationPeer.CreatePeerForElement(BtnMute);
        peer?.RaiseNotificationEvent(
            AutomationNotificationKind.ActionCompleted,
            AutomationNotificationProcessing.ImportantMostRecent,
            GalleryVolume.MuteNotification(GalleryVolume.IsMuted),
            "PalaceVolumeMute");
    }

    private void Persist()
    {
        var values = Windows.Storage.ApplicationData.Current.LocalSettings.Values;
        values[GalleryVolume.VolumeKey] = GalleryVolume.Volume;
        values[GalleryVolume.MuteKey] = GalleryVolume.IsMuted;
    }

    private void RaiseInteraction() => InteractionChanged?.Invoke(this, EventArgs.Empty);

    private static Icon ToFluent(GalleryVolume.SpeakerIcon icon) => icon switch
    {
        GalleryVolume.SpeakerIcon.Mute => Icon.SpeakerMute,
        GalleryVolume.SpeakerIcon.Level0 => Icon.Speaker0,
        GalleryVolume.SpeakerIcon.Level1 => Icon.Speaker1,
        _ => Icon.Speaker2
    };

    private static bool IsShiftDown() =>
        InputKeyboardSource.GetKeyStateForCurrentThread(VirtualKey.Shift)
            .HasFlag(CoreVirtualKeyStates.Down);

    private static void OnMuteButtonAutomationIdChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is VolumeControl control && control._loaded)
        {
            AutomationProperties.SetAutomationId(control.BtnMute, (string)e.NewValue);
        }
    }

    private static void OnSliderAutomationIdChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is VolumeControl control && control._loaded)
        {
            AutomationProperties.SetAutomationId(control.SldVolume, (string)e.NewValue);
        }
    }

    private static void OnRootAutomationIdChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is VolumeControl control)
        {
            AutomationProperties.SetAutomationId(control, (string)e.NewValue);
        }
    }
}
