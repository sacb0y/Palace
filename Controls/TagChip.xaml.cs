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
/// Shared Gooey tag pill: accent fill when applied, dashed partial, tinted selected,
/// leading state glyph, optional color / link / × / star. Never shows the word "On".
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

    public static readonly DependencyProperty IsSelectedProperty = DependencyProperty.Register(
        nameof(IsSelected), typeof(bool), typeof(TagChip),
        new PropertyMetadata(false, OnChromeChanged));

    public static readonly DependencyProperty IsStarredProperty = DependencyProperty.Register(
        nameof(IsStarred), typeof(bool), typeof(TagChip),
        new PropertyMetadata(false, OnChromeChanged));

    public static readonly DependencyProperty ShowStarProperty = DependencyProperty.Register(
        nameof(ShowStar), typeof(bool), typeof(TagChip),
        new PropertyMetadata(false, OnChromeChanged));

    public static readonly DependencyProperty ShowRemoveProperty = DependencyProperty.Register(
        nameof(ShowRemove), typeof(bool), typeof(TagChip),
        new PropertyMetadata(false, OnChromeChanged));

    public static readonly DependencyProperty ShowLeadingLinkProperty = DependencyProperty.Register(
        nameof(ShowLeadingLink), typeof(bool), typeof(TagChip),
        new PropertyMetadata(false, OnChromeChanged));

    public static readonly DependencyProperty ShowTrailingLinkProperty = DependencyProperty.Register(
        nameof(ShowTrailingLink), typeof(bool), typeof(TagChip),
        new PropertyMetadata(false, OnChromeChanged));

    public static readonly DependencyProperty ShowNewBadgeProperty = DependencyProperty.Register(
        nameof(ShowNewBadge), typeof(bool), typeof(TagChip),
        new PropertyMetadata(false, OnChromeChanged));

    /// <summary>
    /// Leading ✓ for "on every selected file" without forcing ApplyState.On accent fill
    /// (preview Manual chips stay ControlFill; board filter On still uses ApplyState).
    /// </summary>
    public static readonly DependencyProperty ShowAppliedCheckProperty = DependencyProperty.Register(
        nameof(ShowAppliedCheck), typeof(bool), typeof(TagChip),
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

    public static readonly DependencyProperty DotOpacityProperty = DependencyProperty.Register(
        nameof(DotOpacity), typeof(double), typeof(TagChip),
        new PropertyMetadata(1.0, OnChromeChanged));

    public static readonly DependencyProperty ChipTooltipProperty = DependencyProperty.Register(
        nameof(ChipTooltip), typeof(string), typeof(TagChip),
        new PropertyMetadata(null, OnChromeChanged));

    public static readonly DependencyProperty ChipAutomationIdProperty = DependencyProperty.Register(
        nameof(ChipAutomationId), typeof(string), typeof(TagChip),
        new PropertyMetadata(null, OnChromeChanged));

    public static readonly DependencyProperty ChipAutomationNameProperty = DependencyProperty.Register(
        nameof(ChipAutomationName), typeof(string), typeof(TagChip),
        new PropertyMetadata(null, OnChromeChanged));

    public static readonly DependencyProperty RemoveAutomationIdProperty = DependencyProperty.Register(
        nameof(RemoveAutomationId), typeof(string), typeof(TagChip),
        new PropertyMetadata(null, OnChromeChanged));

    public static readonly DependencyProperty RemoveAutomationNameProperty = DependencyProperty.Register(
        nameof(RemoveAutomationName), typeof(string), typeof(TagChip),
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

    public static readonly DependencyProperty RemoveCommandProperty = DependencyProperty.Register(
        nameof(RemoveCommand), typeof(ICommand), typeof(TagChip),
        new PropertyMetadata(null));

    public static readonly DependencyProperty RemoveCommandParameterProperty = DependencyProperty.Register(
        nameof(RemoveCommandParameter), typeof(object), typeof(TagChip),
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

    /// <summary>T2 selected for Remove from all — tinted accent + ✓ (t8-02 #3).</summary>
    public bool IsSelected
    {
        get => (bool)GetValue(IsSelectedProperty);
        set => SetValue(IsSelectedProperty, value);
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

    public bool ShowRemove
    {
        get => (bool)GetValue(ShowRemoveProperty);
        set => SetValue(ShowRemoveProperty, value);
    }

    public bool ShowLeadingLink
    {
        get => (bool)GetValue(ShowLeadingLinkProperty);
        set => SetValue(ShowLeadingLinkProperty, value);
    }

    public bool ShowTrailingLink
    {
        get => (bool)GetValue(ShowTrailingLinkProperty);
        set => SetValue(ShowTrailingLinkProperty, value);
    }

    public bool ShowNewBadge
    {
        get => (bool)GetValue(ShowNewBadgeProperty);
        set => SetValue(ShowNewBadgeProperty, value);
    }

    public bool ShowAppliedCheck
    {
        get => (bool)GetValue(ShowAppliedCheckProperty);
        set => SetValue(ShowAppliedCheckProperty, value);
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

    public double DotOpacity
    {
        get => (double)GetValue(DotOpacityProperty);
        set => SetValue(DotOpacityProperty, value);
    }

    /// <summary>When set, replaces the default AssignTooltip.</summary>
    public string? ChipTooltip
    {
        get => (string?)GetValue(ChipTooltipProperty);
        set => SetValue(ChipTooltipProperty, value);
    }

    public string? ChipAutomationId
    {
        get => (string?)GetValue(ChipAutomationIdProperty);
        set => SetValue(ChipAutomationIdProperty, value);
    }

    public string? ChipAutomationName
    {
        get => (string?)GetValue(ChipAutomationNameProperty);
        set => SetValue(ChipAutomationNameProperty, value);
    }

    public string? RemoveAutomationId
    {
        get => (string?)GetValue(RemoveAutomationIdProperty);
        set => SetValue(RemoveAutomationIdProperty, value);
    }

    public string? RemoveAutomationName
    {
        get => (string?)GetValue(RemoveAutomationNameProperty);
        set => SetValue(RemoveAutomationNameProperty, value);
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

    public ICommand? RemoveCommand
    {
        get => (ICommand?)GetValue(RemoveCommandProperty);
        set => SetValue(RemoveCommandProperty, value);
    }

    public object? RemoveCommandParameter
    {
        get => GetValue(RemoveCommandParameterProperty);
        set => SetValue(RemoveCommandParameterProperty, value);
    }

    public event RoutedEventHandler? ChipClick;

    public event RoutedEventHandler? StarClick;

    public event RoutedEventHandler? RemoveClick;

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
        var radius = Compact ? TagChipVisual.CompactCornerRadiusDip : TagChipVisual.CornerRadiusDip;
        BrdChip.MinHeight = height;
        GrdRoot.MinHeight = height;
        BrdChip.CornerRadius = new CornerRadius(radius);
        RectPartialDash.RadiusX = radius;
        RectPartialDash.RadiusY = radius;
        BrdChip.Padding = Compact
            ? new Thickness(8, 0, 2, 0)
            : new Thickness(8, 0, 4, 0);

        TxtLabel.Text = Label ?? "";
        TxtLabel.FontSize = Compact ? 13 : 14;
        var highContrast = ShellBackdrop.IsHighContrast();
        var state = ApplyState;
        var light = ActualTheme == ElementTheme.Light
            || (ActualTheme == ElementTheme.Default
                && Application.Current?.RequestedTheme == ApplicationTheme.Light);

        ApplyFillAndBorder(state, highContrast);
        ApplyStateGlyph(state, highContrast);
        ApplyColorDot();
        ApplyLinks();
        ApplyNewBadge();
        ApplyPartialBadge(state);
        ApplyStar(light, state, highContrast);
        ApplyRemove(state, highContrast);
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
            if (IsSelected || state == TagChipApplyState.On)
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

        // Selected (Remove from all): tinted accent fill + accent outline + primary text.
        if (IsSelected)
        {
            BrdChip.Background = ThemeBrush("AccentFillColorTertiaryBrush");
            BrdChip.BorderBrush = ThemeBrush("AccentFillColorDefaultBrush");
            TxtLabel.Foreground = ThemeBrush("TextFillColorPrimaryBrush");
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

        // Pending "+ new" gets a light accent tint (t8-02 #13).
        if (ShowNewBadge)
        {
            BrdChip.Background = _pressed || _pointerOver
                ? ThemeBrush("AccentFillColorTertiaryBrush")
                : SoftAccentFill();
            BrdChip.BorderBrush = ThemeBrush("AccentFillColorDefaultBrush");
            TxtLabel.Foreground = ThemeBrush("TextFillColorPrimaryBrush");
            return;
        }

        // Preview Manual / pending existing: control fill + stroke (not filter On accent).
        if (ShowRemove)
        {
            BrdChip.Background = _pressed || _pointerOver
                ? ThemeBrush("SubtleFillColorSecondaryBrush")
                : ThemeBrush("ControlFillColorDefaultBrush");
            BrdChip.BorderBrush = ThemeBrush("ControlStrokeColorDefaultBrush");
            TxtLabel.Foreground = ThemeBrush("TextFillColorPrimaryBrush");
            return;
        }

        BrdChip.Background = TransparentOrSubtle();
        BrdChip.BorderBrush = ThemeBrush("ControlStrokeColorDefaultBrush");
        TxtLabel.Foreground = ShowLeadingLink
            ? ThemeBrush("TextFillColorSecondaryBrush")
            : ThemeBrush("TextFillColorPrimaryBrush");
    }

    private Brush SoftAccentFill()
    {
        if (Application.Current?.Resources.TryGetValue("AccentFillColorDefaultBrush", out var value) == true
            && value is SolidColorBrush accent)
        {
            var c = accent.Color;
            return new SolidColorBrush(Color.FromArgb(0x1F, c.R, c.G, c.B));
        }

        return ThemeBrush("AccentFillColorTertiaryBrush");
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
        if (ShowLeadingLink)
        {
            VbStateGlyph.Visibility = Visibility.Collapsed;
            return;
        }

        var showPartial = !IsSelected && state == TagChipApplyState.Partial;
        var showOn = IsSelected || ShowAppliedCheck || state == TagChipApplyState.On;
        if (!showPartial && !showOn)
        {
            VbStateGlyph.Visibility = Visibility.Collapsed;
            return;
        }

        VbStateGlyph.Visibility = Visibility.Visible;
        IcnState.Icon = showPartial && !showOn
            ? FluentIcons.Common.Icon.Subtract
            : FluentIcons.Common.Icon.Checkmark;

        if (highContrast)
        {
            IcnState.Foreground = IsSelected || state == TagChipApplyState.On
                ? ThemeBrush("SystemColorHighlightTextColorBrush")
                : ThemeBrush("SystemColorButtonTextColorBrush");
        }
        else if (IsSelected || ShowAppliedCheck)
        {
            IcnState.Foreground = ThemeBrush("AccentFillColorDefaultBrush");
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
        EllColorDot.Opacity = DotOpacity > 0 ? DotOpacity : 1;
    }

    private void ApplyLinks()
    {
        VbLeadingLink.Visibility = ShowLeadingLink ? Visibility.Visible : Visibility.Collapsed;
        VbTrailingLink.Visibility = ShowTrailingLink ? Visibility.Visible : Visibility.Collapsed;
    }

    private void ApplyNewBadge()
    {
        TxtNewBadge.Visibility = ShowNewBadge ? Visibility.Visible : Visibility.Collapsed;
    }

    private void ApplyPartialBadge(TagChipApplyState state)
    {
        if (state != TagChipApplyState.Partial || IsSelected)
        {
            TxtPartialBadge.Visibility = Visibility.Collapsed;
            TxtPartialBadge.Text = "";
            return;
        }

        var badge = TagChipVisual.PartialBadge(PartialCount, SelectionTotal);
        TxtPartialBadge.Text = badge;
        TxtPartialBadge.Visibility = string.IsNullOrEmpty(badge) ? Visibility.Collapsed : Visibility.Visible;
        TxtPartialBadge.Foreground = ThemeBrush("TextFillColorSecondaryBrush");
    }

    private void ApplyStar(bool light, TagChipApplyState state, bool highContrast)
    {
        if (!ShowStar)
        {
            BtnStar.Visibility = Visibility.Collapsed;
            return;
        }

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
            if (state == TagChipApplyState.On && !IsSelected)
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
            IcnStar.Foreground = state == TagChipApplyState.On && !IsSelected
                ? ThemeBrush("TextOnAccentFillColorPrimaryBrush")
                : ThemeBrush("TextFillColorSecondaryBrush");
        }
    }

    private void ApplyRemove(TagChipApplyState state, bool highContrast)
    {
        if (!ShowRemove)
        {
            BtnRemove.Visibility = Visibility.Collapsed;
            return;
        }

        BtnRemove.Visibility = Visibility.Visible;
        var tip = string.IsNullOrWhiteSpace(RemoveAutomationName)
            ? $"Remove {Label}"
            : RemoveAutomationName!;
        ToolTipService.SetToolTip(BtnRemove, tip);
        AutomationProperties.SetName(BtnRemove, tip);
        if (!string.IsNullOrEmpty(RemoveAutomationId))
        {
            AutomationProperties.SetAutomationId(BtnRemove, RemoveAutomationId);
        }

        if (highContrast)
        {
            IcnRemove.Foreground = IsSelected || state == TagChipApplyState.On
                ? ThemeBrush("SystemColorHighlightTextColorBrush")
                : ThemeBrush("SystemColorButtonTextColorBrush");
            return;
        }

        if (state == TagChipApplyState.On && !IsSelected)
        {
            IcnRemove.Foreground = _pointerOver
                ? ThemeBrush("TextOnAccentFillColorPrimaryBrush")
                : ThemeBrush("TextOnAccentFillColorSecondaryBrush");
        }
        else
        {
            IcnRemove.Foreground = _pointerOver
                ? ThemeBrush("TextFillColorPrimaryBrush")
                : ThemeBrush("TextFillColorSecondaryBrush");
        }
    }

    private void ApplyTooltip(TagChipApplyState state)
    {
        var tip = !string.IsNullOrWhiteSpace(ChipTooltip)
            ? ChipTooltip
            : TagChipVisual.AssignTooltip(state, Label, PartialCount, SelectionTotal);
        ToolTipService.SetToolTip(this, string.IsNullOrEmpty(tip) ? null : tip);
    }

    private void ApplyAutomation(TagChipApplyState state)
    {
        var name = !string.IsNullOrWhiteSpace(ChipAutomationName)
            ? ChipAutomationName!
            : TagChipVisual.AutomationName(Label, state, IsStarred);
        AutomationProperties.SetName(this, name);
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
        if (e.OriginalSource is DependencyObject source
            && (IsUnderStar(source) || IsUnderRemove(source)))
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

        // Del for Remove from all is handled on LibraryRoot (selected chips), not as ×.

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

    private void BtnStar_Click(object sender, RoutedEventArgs e) => InvokeStar();

    private void BtnRemove_Click(object sender, RoutedEventArgs e) => InvokeRemove();

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

    private void InvokeRemove()
    {
        RemoveClick?.Invoke(this, new RoutedEventArgs());
        if (RemoveCommand?.CanExecute(RemoveCommandParameter) == true)
        {
            RemoveCommand.Execute(RemoveCommandParameter);
        }
    }

    private bool IsUnderStar(DependencyObject? source) => IsUnder(source, BtnStar);

    private bool IsUnderRemove(DependencyObject? source) => IsUnder(source, BtnRemove);

    private static bool IsUnder(DependencyObject? source, DependencyObject target)
    {
        while (source is not null)
        {
            if (ReferenceEquals(source, target))
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
        if (IsUnderStar(e.OriginalSource as DependencyObject)
            || IsUnderRemove(e.OriginalSource as DependencyObject))
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
