using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media.Animation;
using Windows.UI.ViewManagement;

namespace Palace.Helpers;

/// <summary>
/// Pointer/key idle auto-hide for a viewer chrome host. Fades opacity (keeps tab
/// order); never hides while hovered/focused, while Narrator is on, or when
/// animations are off. WinUI — not linked into Palace.Tests.
/// </summary>
public sealed class ViewerOverlayAutoHide
{
    private readonly UIElement _root;
    private readonly FrameworkElement _chrome;
    private readonly FrameworkElement[] _focusElements;
    private readonly DispatcherQueueTimer _idleTimer;
    private readonly PointerEventHandler _rootPointerPressed;
    private bool _visible = true;
    private bool _hooked;
    private bool _chromePointerOver;
    private Storyboard? _fade;

    public ViewerOverlayAutoHide(
        UIElement root,
        FrameworkElement chrome,
        params FrameworkElement[] focusElements)
    {
        _root = root;
        _chrome = chrome;
        _focusElements = focusElements;
        _rootPointerPressed = Root_PointerPressed;
        _idleTimer = root.DispatcherQueue.CreateTimer();
        _idleTimer.IsRepeating = false;
        _idleTimer.Interval = TimeSpan.FromMilliseconds(ViewerOverlayChrome.IdleHideMs);
        _idleTimer.Tick += IdleTimer_Tick;
    }

    public void Attach()
    {
        if (_hooked)
        {
            return;
        }

        _root.PointerMoved += Root_PointerMoved;
        // Tap with no move must still wake faded chrome — including presses
        // already Handled (mouse pan on still, buttons, media transport).
        _root.AddHandler(UIElement.PointerPressedEvent, _rootPointerPressed, handledEventsToo: true);
        _root.PreviewKeyDown += Root_PreviewKeyDown;
        _chrome.PointerEntered += Chrome_PointerEntered;
        _chrome.PointerExited += Chrome_PointerExited;
        _hooked = true;
        Show(resetIdle: true);
    }

    public void Detach()
    {
        if (!_hooked)
        {
            return;
        }

        _root.PointerMoved -= Root_PointerMoved;
        _root.RemoveHandler(UIElement.PointerPressedEvent, _rootPointerPressed);
        _root.PreviewKeyDown -= Root_PreviewKeyDown;
        _chrome.PointerEntered -= Chrome_PointerEntered;
        _chrome.PointerExited -= Chrome_PointerExited;
        _idleTimer.Stop();
        _fade?.Stop();
        _hooked = false;
        _chromePointerOver = false;
        SetOpacity(1, animate: false);
        _visible = true;
        _chrome.IsHitTestVisible = true;
    }

    public void Show(bool resetIdle = true)
    {
        if (!_visible)
        {
            SetOpacity(1, animate: ShouldAnimate());
            _visible = true;
        }

        // Faded chrome must not steal clicks; tab order stays via IsTabStop on buttons.
        _chrome.IsHitTestVisible = true;

        if (resetIdle)
        {
            RestartIdle();
        }
    }

    private void Root_PointerMoved(object sender, PointerRoutedEventArgs e) => Show(resetIdle: true);

    private void Root_PointerPressed(object sender, PointerRoutedEventArgs e) => Show(resetIdle: true);

    private void Root_PreviewKeyDown(object sender, KeyRoutedEventArgs e) => Show(resetIdle: true);

    private void Chrome_PointerEntered(object sender, PointerRoutedEventArgs e)
    {
        _chromePointerOver = true;
        Show(resetIdle: true);
    }

    private void Chrome_PointerExited(object sender, PointerRoutedEventArgs e)
    {
        _chromePointerOver = false;
        RestartIdle();
    }

    private void IdleTimer_Tick(DispatcherQueueTimer sender, object args)
    {
        var blocked = IsHoveredOrFocused();
        if (!ViewerOverlayChrome.ShouldHideAfterIdle(
                ViewerOverlayChrome.IdleHideMs,
                isHovered: blocked,
                isFocused: false,
                animationsEnabled: AnimationsEnabled(),
                screenReaderRunning: ScreenReaderRunning()))
        {
            if (blocked || !ViewerOverlayChrome.AllowsAutoHide(AnimationsEnabled(), ScreenReaderRunning()))
            {
                RestartIdle();
            }

            return;
        }

        Hide();
    }

    private void Hide()
    {
        if (!_visible)
        {
            return;
        }

        if (!ViewerOverlayChrome.MayHide(
                isHovered: IsHoveredOrFocused(),
                isFocused: false,
                animationsEnabled: AnimationsEnabled(),
                screenReaderRunning: ScreenReaderRunning()))
        {
            RestartIdle();
            return;
        }

        SetOpacity(0, animate: ShouldAnimate());
        _chrome.IsHitTestVisible = false;
        _visible = false;
    }

    private bool IsHoveredOrFocused()
    {
        if (_chromePointerOver)
        {
            return true;
        }

        foreach (var element in _focusElements)
        {
            if (element.FocusState != FocusState.Unfocused)
            {
                return true;
            }
        }

        return false;
    }

    private void RestartIdle()
    {
        if (!ViewerOverlayChrome.AllowsAutoHide(AnimationsEnabled(), ScreenReaderRunning()))
        {
            _idleTimer.Stop();
            return;
        }

        _idleTimer.Stop();
        _idleTimer.Start();
    }

    private bool ShouldAnimate() =>
        ViewerOverlayChrome.ShouldAnimateFade(AnimationsEnabled(), ScreenReaderRunning());

    private static bool AnimationsEnabled()
    {
        try
        {
            return new UISettings().AnimationsEnabled;
        }
        catch
        {
            return true;
        }
    }

    private static bool ScreenReaderRunning()
    {
        try
        {
            return AutomationPeer.ListenerExists(AutomationEvents.AutomationFocusChanged)
                || AutomationPeer.ListenerExists(AutomationEvents.LiveRegionChanged);
        }
        catch
        {
            return false;
        }
    }

    private void SetOpacity(double opacity, bool animate)
    {
        _fade?.Stop();
        if (!animate)
        {
            _chrome.Opacity = opacity;
            return;
        }

        var ms = ViewerOverlayChrome.FadeDurationMs(visible: opacity >= 0.5);
        var anim = new DoubleAnimation
        {
            To = opacity,
            Duration = new Duration(TimeSpan.FromMilliseconds(ms)),
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseInOut }
        };
        Storyboard.SetTarget(anim, _chrome);
        Storyboard.SetTargetProperty(anim, "Opacity");
        _fade = new Storyboard();
        _fade.Children.Add(anim);
        _fade.Begin();
    }
}
