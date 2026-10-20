using System.Windows.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Palace.Helpers;
using Windows.System;
using Windows.UI;

namespace Palace.Controls;

/// <summary>
/// Shared Gooey tag pill: accent fill when applied, dashed partial, leading state
/// glyph, optional color dot, separate star button. Never shows the word "On".
/// </summary>
public sealed partial class TagChip : UserControl
{
    public static readonly DependencyProperty LabelProperty = DependencyProperty.Register(
        nameof(Label), typeof(string), typeof(TagChip),
        new PropertyMetadata("", OnChromeChanged));

    public static readonly DependencyProperty TagColorProperty = DependencyProperty.Register(
        nameof(TagColor), typeof(string), typeof(TagChip),
        new PropertyMetadata(null, OnChromeChanged));

    public static readonly DependencyProperty ApplyStateProperty = DependencyProperty.Register(
        nameof(ApplyState), typeof(TagChipApplyState), typeof(TagChip),
        new PropertyMetadata(TagChipApplyState.Off, OnChromeChanged));

    public static readonly DependencyProperty IsStarredProperty = DependencyProperty.Register(
        nameof(IsStarred), typeof(bool), typeof(TagChip),
        new PropertyMetadata(false, OnChromeChanged));

    public static readonly DependencyProperty ShowStarProperty = DependencyProperty.Register(
        nameof(ShowStar), typeof(bool), typeof(TagChip),
        new PropertyMetadata(false, OnChromeChanged));

    public static readonly DependencyProperty CompactProperty = DependencyProperty.Register(
        nameof(Compact), typeof(bool), typeof(TagChip),
        new PropertyMetadata(false, OnChromeChanged));

    public static readonly DependencyProperty PartialCountProperty = DependencyProperty.Register(
        nameof(PartialCount), typeof(int), typeof(TagChip),
        new PropertyMetadata(0, OnChromeChanged));

    public static readonly DependencyProperty SelectionTotalProperty = DependencyProperty.Register(
        nameof(SelectionTotal), typeof(int), typeof(TagChip),
        new PropertyMetadata(0, OnChromeChanged));

    public static readonly DependencyProperty ChipAutomationIdProperty = DependencyProperty.Register(
        nameof(ChipAutomationId), typeof(string), typeof(TagChip),
        new PropertyMetadata(null, OnChromeChanged));

    public static readonly DependencyProperty CommandProperty = DependencyProperty.Register(
        nameof(Command), typeof(ICommand), typeof(TagChip),
        new PropertyMetadata(null));

    public static readonly DependencyProperty CommandParameterProperty = DependencyProperty.Register(
        nameof(CommandParameter), typeof(object), typeof(TagChip),
        new PropertyMetadata(null));

    public static readonly DependencyProperty StarCommandProperty = DependencyProperty.Register(
        nameof(StarCommand), typeof(ICommand), typeof(TagChip),
        new PropertyMetadata(null));

    public static readonly DependencyProperty StarCommandParameterProperty = DependencyProperty.Register(
        nameof(StarCommandParameter), typeof(object), typeof(TagChip),
        new PropertyMetadata(null));

    private bool _pointerOver;
    private bool _pressed;

    public TagChip()
    {
        InitializeComponent();
        ActualThemeChanged += (_, _) => ApplyChrome();
        GotFocus += (_, _) => ApplyChrome();
        LostFocus += (_, _) => ApplyChrome();
        Loaded += (_, _) => ApplyChrome();
        IsEnabledChanged += (_, _) => ApplyChrome();
    }

    public string Label
    {
        get => (string)GetValue(LabelProperty);
        set => SetValue(LabelProperty, value);
    }

    public string? TagColor
    {
        get => (string?)GetValue(TagColorProperty);
        set => SetValue(TagColorProperty, value);
    }

    public TagChipApplyState ApplyState
    {
        get => (TagChipApplyState)GetValue(ApplyStateProperty);
        set => SetValue(ApplyStateProperty, value);
    }

    public bool IsStarred
    {
        get => (bool)GetValue(IsStarredProperty);
        set => SetValue(IsStarredProperty, value);
    }

    public bool ShowStar
    {
        get => (bool)GetValue(ShowStarProperty);
        set => SetValue(ShowStarProperty, value);
    }

    public bool Compact
    {
        get => (bool)GetValue(CompactProperty);
        set => SetValue(CompactProperty, value);
    }

    public int PartialCount
    {
        get => (int)GetValue(PartialCountProperty);
        set => SetValue(PartialCountProperty, value);
    }

    public int SelectionTotal
    {
        get => (int)GetValue(SelectionTotalProperty);
        set => SetValue(SelectionTotalProperty, value);
    }

    public string? ChipAutomationId
    {
        get => (string?)GetValue(ChipAutomationIdProperty);
        set => SetValue(ChipAutomationIdProperty, value);
    }

    public ICommand? Command
    {
        get => (ICommand?)GetValue(CommandProperty);
        set => SetValue(CommandProperty, value);
    }

    public object? CommandParameter
    {
        get => GetValue(CommandParameterProperty);
        set => SetValue(CommandParameterProperty, value);
    }

    public ICommand? StarCommand
    {
        get => (ICommand?)GetValue(StarCommandProperty);
        set => SetValue(StarCommandProperty, value);
    }

    public object? StarCommandParameter
    {
        get => GetValue(StarCommandParameterProperty);
        set => SetValue(StarCommandParameterProperty, value);
    }

    public event RoutedEventHandler? ChipClick;

    public event RoutedEventHandler? StarClick;

    private static void OnChromeChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is TagChip chip)
        {
            chip.ApplyChrome();
        }
    }

    private void ApplyChrome()
    {
        if (BrdChip is null)
        {
            return;
        }

        var height = Compact ? TagChipVisual.CompactHeightDip : TagChipVisual.HeightDip;
        BrdChip.MinHeight = height;
        GrdRoot.MinHeight = height;
        BrdChip.CornerRadius = new CornerRadius(TagChipVisual.CornerRadiusDip);
        RectPartialDash.RadiusX = TagChipVisual.CornerRadiusDip;
        RectPartialDash.RadiusY = TagChipVisual.CornerRadiusDip;

        TxtLabel.Text = Label ?? "";
        var highContrast = ShellBackdrop.IsHighContrast();
        var state = ApplyState;
        var light = ActualTheme == ElementTheme.Light
            || (ActualTheme == ElementTheme.Default
                && Application.Current?.RequestedTheme == ApplicationTheme.Light);

        ApplyFillAndBorder(state, highContrast);
        ApplyStateGlyph(state, highContrast);
        ApplyColorDot();
        ApplyPartialBadge(state);
        ApplyStar(light, state, highContrast);
        ApplyTooltip(state);
        ApplyAutomation(state);

        if (!string.IsNullOrEmpty(ChipAutomationId))
        {
            AutomationProperties.SetAutomationId(this, ChipAutomationId);
        }
    }

    private void ApplyFillAndBorder(TagChipApplyState state, bool highContrast)
    {
        RectPartialDash.Visibility = Visibility.Collapsed;
        BrdChip.BorderThickness = new Thickness(1);

        if (highContrast)
        {
            if (state == TagChipApplyState.On)
            {
                BrdChip.Background = ThemeBrush("SystemColorHighlightColorBrush");
                BrdChip.BorderBrush = ThemeBrush("SystemColorHighlightColorBrush");
                TxtLabel.Foreground = ThemeBrush("SystemColorHighlightTextColorBrush");
            }
            else if (state == TagChipApplyState.Partial)
            {
                BrdChip.Background = ThemeBrush("SystemColorButtonFaceColorBrush");
                BrdChip.BorderBrush = ThemeBrush("SystemColorHighlightColorBrush");
                BrdChip.BorderThickness = new Thickness(0);
                RectPartialDash.Stroke = ThemeBrush("SystemColorHighlightColorBrush");
                RectPartialDash.Visibility = Visibility.Visible;
                TxtLabel.Foreground = ThemeBrush("SystemColorButtonTextColorBrush");
            }
            else
            {
                BrdChip.Background = ThemeBrush("SystemColorButtonFaceColorBrush");
                BrdChip.BorderBrush = ThemeBrush("SystemColorButtonTextColorBrush");
                TxtLabel.Foreground = ThemeBrush("SystemColorButtonTextColorBrush");
            }

            return;
        }

        if (state == TagChipApplyState.On)
        {
            var fillKey = _pressed
                ? "AccentFillColorTertiaryBrush"
                : _pointerOver ? "AccentFillColorSecondaryBrush" : "AccentFillColorDefaultBrush";
            BrdChip.Background = ThemeBrush(fillKey);
            BrdChip.BorderBrush = ThemeBrush(fillKey);
            TxtLabel.Foreground = ThemeBrush("TextOnAccentFillColorPrimaryBrush");
            return;
        }

        if (state == TagChipApplyState.Partial)
        {
            BrdChip.Background = TransparentOrSubtle();
            BrdChip.BorderThickness = new Thickness(0);
            BrdChip.BorderBrush = null;
            RectPartialDash.Stroke = ThemeBrush("AccentFillColorDefaultBrush");
            RectPartialDash.StrokeThickness = TagChipVisual.PartialBorderThickness;
            RectPartialDash.Visibility = Visibility.Visible;
            TxtLabel.Foreground = ThemeBrush("TextFillColorPrimaryBrush");
            return;
        }

        BrdChip.Background = TransparentOrSubtle();
        BrdChip.BorderBrush = ThemeBrush("ControlStrokeColorDefaultBrush");
        TxtLabel.Foreground = ThemeBrush("TextFillColorPrimaryBrush");
    }

    private Brush TransparentOrSubtle()
    {
        if (_pressed)
        {
            return ThemeBrush("SubtleFillColorTertiaryBrush");
        }

        if (_pointerOver)
        {
            return ThemeBrush("SubtleFillColorSecondaryBrush");
        }

        return ThemeBrush("ControlFillColorTransparentBrush");
    }

    private void ApplyStateGlyph(TagChipApplyState state, bool highContrast)
    {
        if (state == TagChipApplyState.Off)
        {
            VbStateGlyph.Visibility = Visibility.Collapsed;
            return;
        }

        VbStateGlyph.Visibility = Visibility.Visible;
        // FluentIcon.Icon is FluentIcons.Common.Icon (not Symbol).
        IcnState.Icon = state == TagChipApplyState.Partial
            ? FluentIcons.Common.Icon.Subtract
            : FluentIcons.Common.Icon.Checkmark;

        if (highContrast)
        {
            IcnState.Foreground = state == TagChipApplyState.On
                ? ThemeBrush("SystemColorHighlightTextColorBrush")
                : ThemeBrush("SystemColorButtonTextColorBrush");
        }
        else if (state == TagChipApplyState.On)
        {
            IcnState.Foreground = ThemeBrush("TextOnAccentFillColorPrimaryBrush");
        }
        else
        {
            IcnState.Foreground = ThemeBrush("AccentFillColorDefaultBrush");
        }
    }

    private void ApplyColorDot()
    {
        var hex = TagColor;
        if (!Helpers.TagColor.HasColor(hex) || !Helpers.TagColor.TryParse(hex, out var color))
        {
            EllColorDot.Visibility = Visibility.Collapsed;
            EllColorDot.Fill = null;
            return;
        }

        EllColorDot.Visibility = Visibility.Visible;
        EllColorDot.Fill = new SolidColorBrush(color);
    }

    private void ApplyPartialBadge(TagChipApplyState state)
    {
        if (state != TagChipApplyState.Partial)
        {
            TxtPartialBadge.Visibility = Visibility.Collapsed;
            TxtPartialBadge.Text = "";
            return;
        }

        var badge = TagChipVisual.PartialBadge(PartialCount, SelectionTotal);
        TxtPartialBadge.Text = badge;
        TxtPartialBadge.Visibility = string.IsNullOrEmpty(badge) ? Visibility.Collapsed : Visibility.Visible;
        TxtPartialBadge.Foreground = TxtLabel.Foreground;
    }

    private void ApplyStar(bool light, TagChipApplyState state, bool highContrast)
    {
        if (!ShowStar)
        {
            BtnStar.Visibility = Visibility.Collapsed;
            return;
        }

        // Keep star in layout when ShowStar; fade unstarred until hover/focus.
        BtnStar.Visibility = Visibility.Visible;
        var reveal = IsStarred || _pointerOver || FocusState != FocusState.Unfocused;
        BtnStar.Opacity = reveal ? 1 : 0;
        BtnStar.IsHitTestVisible = reveal;

        IcnStar.IconVariant = IsStarred
            ? FluentIcons.Common.IconVariant.Filled
            : FluentIcons.Common.IconVariant.Regular;

        var tip = TagChipVisual.StarTooltip(IsStarred);
        ToolTipService.SetToolTip(BtnStar, tip);
        AutomationProperties.SetName(BtnStar, tip);

        if (highContrast)
        {
            IcnStar.Foreground = state == TagChipApplyState.On
                ? ThemeBrush("SystemColorHighlightTextColorBrush")
                : ThemeBrush("SystemColorButtonTextColorBrush");
            return;
        }

        if (IsStarred)
        {
            if (state == TagChipApplyState.On)
            {
                IcnStar.Foreground = ThemeBrush("TextOnAccentFillColorPrimaryBrush");
            }
            else if (Helpers.TagColor.TryParse(TagChipVisual.StarGoldHex(light), out var gold))
            {
                IcnStar.Foreground = new SolidColorBrush(gold);
            }
        }
        else
        {
            IcnStar.Foreground = state == TagChipApplyState.On
                ? ThemeBrush("TextOnAccentFillColorPrimaryBrush")
                : ThemeBrush("TextFillColorSecondaryBrush");
        }
    }

    private void ApplyTooltip(TagChipApplyState state)
    {
        var tip = TagChipVisual.AssignTooltip(state, Label, PartialCount, SelectionTotal);
        ToolTipService.SetToolTip(this, string.IsNullOrEmpty(tip) ? null : tip);
    }

    private void ApplyAutomation(TagChipApplyState state)
    {
        AutomationProperties.SetName(this, TagChipVisual.AutomationName(Label, state, IsStarred));
        AutomationProperties.SetFullDescription(
            this,
            state == TagChipApplyState.Partial
                ? TagChipVisual.PartialTooltip(PartialCount, SelectionTotal)
                : TagChipVisual.ToggleAutomationState(state));
    }

    private static Brush ThemeBrush(string key)
    {
        if (Application.Current?.Resources.TryGetValue(key, out var value) == true && value is Brush brush)
        {
            return brush;
        }

        return key.Contains("Transparent", StringComparison.Ordinal)
            ? new SolidColorBrush(Color.FromArgb(0, 0, 0, 0))
            : new SolidColorBrush(Color.FromArgb(255, 128, 128, 128));
    }

    private void Root_Tapped(object sender, TappedRoutedEventArgs e)
    {
        if (e.OriginalSource is DependencyObject source && IsUnderStar(source))
        {
            return;
        }

        InvokeChip();
        e.Handled = true;
    }

    private void Root_KeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key is VirtualKey.Space or VirtualKey.Enter)
        {
            InvokeChip();
            e.Handled = true;
            return;
        }

        if (!ShowStar)
        {
            return;
        }

        var ctrl = Microsoft.UI.Input.InputKeyboardSource.GetKeyStateForCurrentThread(VirtualKey.Control)
            .HasFlag(Windows.UI.Core.CoreVirtualKeyStates.Down);
        if (e.Key == VirtualKey.S || (ctrl && e.Key == VirtualKey.D))
        {
            InvokeStar();
            e.Handled = true;
        }
    }

    private void BtnStar_Click(object sender, RoutedEventArgs e)
    {
        // WinUI RoutedEventArgs has no Handled; star Click already stops at the Button.
        InvokeStar();
    }

    private void InvokeChip()
    {
        ChipClick?.Invoke(this, new RoutedEventArgs());
        if (Command?.CanExecute(CommandParameter) == true)
        {
            Command.Execute(CommandParameter);
        }
    }

    private void InvokeStar()
    {
        StarClick?.Invoke(this, new RoutedEventArgs());
        if (StarCommand?.CanExecute(StarCommandParameter) == true)
        {
            StarCommand.Execute(StarCommandParameter);
        }
    }

    private bool IsUnderStar(DependencyObject? source)
    {
        while (source is not null)
        {
            if (ReferenceEquals(source, BtnStar))
            {
                return true;
            }

            source = VisualTreeHelper.GetParent(source);
        }

        return false;
    }

    private void Root_PointerEntered(object sender, PointerRoutedEventArgs e)
    {
        _pointerOver = true;
        ApplyChrome();
    }

    private void Root_PointerExited(object sender, PointerRoutedEventArgs e)
    {
        _pointerOver = false;
        _pressed = false;
        ApplyChrome();
    }

    private void Root_PointerPressed(object sender, PointerRoutedEventArgs e)
    {
        if (IsUnderStar(e.OriginalSource as DependencyObject))
        {
            return;
        }

        _pressed = true;
        CapturePointer(e.Pointer);
        ApplyChrome();
    }

    private void Root_PointerReleased(object sender, PointerRoutedEventArgs e)
    {
        _pressed = false;
        ReleasePointerCapture(e.Pointer);
        ApplyChrome();
    }

    private void Root_PointerCanceled(object sender, PointerRoutedEventArgs e) => ClearPointer();

    private void Root_PointerCaptureLost(object sender, PointerRoutedEventArgs e) => ClearPointer();

    private void ClearPointer()
    {
        _pressed = false;
        _pointerOver = false;
        ApplyChrome();
    }
}
